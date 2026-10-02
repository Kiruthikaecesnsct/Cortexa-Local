import subprocess
from pathlib import Path

import pytest

from ingestion.domain.enums.git_host import GitHost
from ingestion.domain.errors.clone_errors import CloneTimeoutError, GitExecutionError
from ingestion.domain.errors.scan_errors import CloneExecutionError
from ingestion.domain.models.repository_clone import CheckoutSpec
from ingestion.infrastructure.config.settings import IngestionSettings
from ingestion.infrastructure.git import git_workspace
from ingestion.infrastructure.git.git_workspace import GitRepositoryWorkspace

SYMLINK_MODE = "120000"
GITHUB_SPEC = CheckoutSpec(GitHost.GITHUB, "https://github.com/acme/api.git", "main", "tok")
AZURE_SPEC = CheckoutSpec(
    GitHost.AZURE_DEVOPS, "https://dev.azure.com/contoso/P/_git/api", "main", "tok"
)


def _git(cwd: Path, *args: str, stdin: str | None = None) -> str:
    return subprocess.run(
        ["git", *args], cwd=cwd, input=stdin, capture_output=True, text=True, check=True
    ).stdout.strip()


def _repo_with_symlink_to(secret: Path, root: Path) -> Path:
    repo = root / "repo"
    repo.mkdir()
    _git(repo, "init", "-q")
    _git(repo, "config", "user.email", "t@example.com")
    _git(repo, "config", "user.name", "t")
    (repo / "README.md").write_text("hello")
    _git(repo, "add", "README.md")
    # Record a symlink in the index directly so the test works where the OS can't create one.
    blob = _git(repo, "hash-object", "-w", "--stdin", stdin=str(secret))
    _git(repo, "update-index", "--add", "--cacheinfo", f"{SYMLINK_MODE},{blob},leak")
    _git(repo, "commit", "-q", "-m", "init")
    return repo


@pytest.fixture
def workspace(tmp_path: Path) -> GitRepositoryWorkspace:
    workdir = tmp_path / "work"
    workdir.mkdir()
    return GitRepositoryWorkspace(IngestionSettings(clone_workdir=str(workdir)))


def test_regular_files_skips_symlinks_missing_and_outside_entries(tmp_path: Path) -> None:
    root = tmp_path / "checkout"
    (root / "src").mkdir(parents=True)
    (root / "README.md").write_text("hello")
    (root / "src" / "app.py").write_text("print(1)")
    secret = tmp_path / "server-secret.txt"
    secret.write_text("TOP-SECRET-CONTENT")
    listing = "README.md\0src/app.py\0gone.txt\0"
    try:
        (root / "leak").symlink_to(secret)
        listing += "leak\0"
    except OSError:
        pass  # The OS cannot create symlinks here; the other cases still apply.

    assert list(git_workspace._regular_files(root, listing)) == ["README.md", "src/app.py"]


async def test_checkout_lists_tracked_files_and_keeps_folder_until_discard(
    workspace: GitRepositoryWorkspace, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    secret = tmp_path / "server-secret.txt"
    secret.write_text("TOP-SECRET-CONTENT")
    source = _repo_with_symlink_to(secret, tmp_path)
    checkout_dir = tmp_path / "work" / "checkout"
    subprocess.run(["git", "clone", "-q", str(source), str(checkout_dir)], check=True)

    async def fake_clone(ref, token, settings, branch):  # noqa: ANN001, ANN202
        assert ref.normalized_https_url == "https://github.com/acme/api.git"
        return str(checkout_dir)

    monkeypatch.setattr(git_workspace.git_runner, "run_clone", fake_clone)

    checkout = await workspace.checkout(GITHUB_SPEC)

    assert checkout.path == checkout_dir
    assert "README.md" in checkout.files
    assert not any(name.startswith(".git/") for name in checkout.files)
    for name in checkout.files:
        assert b"TOP-SECRET-CONTENT" not in (checkout_dir / name).read_bytes()
    assert checkout.commit_sha == _git(source, "rev-parse", "HEAD")
    workspace.discard(checkout)
    assert not checkout_dir.exists()


@pytest.mark.parametrize(
    ("error", "message"),
    [
        (CloneTimeoutError(5), "timed out after 5 seconds"),
        (GitExecutionError(exit_code=128, message="auth failed"), "Could not download"),
    ],
)
async def test_clone_errors_become_user_facing_messages(
    workspace: GitRepositoryWorkspace,
    monkeypatch: pytest.MonkeyPatch,
    error: Exception,
    message: str,
) -> None:
    async def failing_clone(ref, token, settings, branch):  # noqa: ANN001, ANN202
        raise error

    monkeypatch.setattr(git_workspace.git_runner, "run_clone", failing_clone)

    with pytest.raises(CloneExecutionError, match=message):
        await workspace.checkout(GITHUB_SPEC)


async def test_azure_clone_passes_url_and_names_azure_in_errors(
    workspace: GitRepositoryWorkspace, monkeypatch: pytest.MonkeyPatch
) -> None:
    seen: list[tuple[str, str, str | None]] = []

    async def failing_clone(ref, token, settings, branch):  # noqa: ANN001, ANN202
        seen.append((ref.normalized_https_url, token, branch))
        raise GitExecutionError(exit_code=128, message="denied")

    monkeypatch.setattr(git_workspace.git_runner, "run_clone", failing_clone)

    with pytest.raises(CloneExecutionError, match="from Azure DevOps"):
        await workspace.checkout(AZURE_SPEC)
    assert seen == [("https://dev.azure.com/contoso/P/_git/api", "tok", "main")]
