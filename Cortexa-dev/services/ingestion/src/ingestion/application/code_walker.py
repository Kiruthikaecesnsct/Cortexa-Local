from collections.abc import Iterator
from pathlib import Path

MAX_FILE_BYTES = 1 * 1024 * 1024
BINARY_CHECK_BYTES = 8000


def walk_code_files(repo_path: Path) -> Iterator[tuple[str, str]]:
    for file_path in sorted(repo_path.rglob("*")):
        if not file_path.is_file():
            continue
        if file_path.stat().st_size > MAX_FILE_BYTES:
            continue
        raw = file_path.read_bytes()
        if b"\x00" in raw[:BINARY_CHECK_BYTES]:
            continue
        text = raw.decode("utf-8", errors="replace")
        relative = file_path.relative_to(repo_path).as_posix()
        yield (relative, text)
