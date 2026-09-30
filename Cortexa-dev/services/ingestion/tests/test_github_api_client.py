from collections.abc import AsyncIterator

import httpx
import pytest
import respx

from ingestion.domain.errors.github_scan_errors import (
    GitHubAccessDeniedError,
    GitHubAuthError,
    GitHubNotFoundError,
    GitHubRateLimitError,
    GitHubUpstreamError,
)
from ingestion.infrastructure.config.settings import IngestionSettings
from ingestion.infrastructure.github.github_api_client import (
    GitHubApiClient,
    create_github_http_client,
)

API = "https://api.github.com"
TOKEN = "ghp_testtoken"
MAX_PAGES = 3


def _repo_json(name: str) -> dict:
    return {
        "name": name,
        "full_name": f"acme/{name}",
        "private": True,
        "default_branch": "main",
        "html_url": f"https://github.com/acme/{name}",
        "size": 12,
        "updated_at": "2026-09-01T10:00:00Z",
    }


@pytest.fixture
async def client() -> AsyncIterator[GitHubApiClient]:
    http = create_github_http_client(IngestionSettings(github_api_base_url=API))
    yield GitHubApiClient(http, MAX_PAGES)
    await http.aclose()


@respx.mock
async def test_list_repositories_follows_pagination_and_sends_token(
    client: GitHubApiClient,
) -> None:
    page2 = f"{API}/organizations/1/repos?per_page=100&page=2"
    first = respx.get(f"{API}/orgs/acme/repos").mock(
        return_value=httpx.Response(
            200, json=[_repo_json("a")], headers={"Link": f'<{page2}>; rel="next"'}
        )
    )
    respx.get(page2).mock(return_value=httpx.Response(200, json=[_repo_json("b")]))

    repos = await client.list_repositories("acme", TOKEN)

    assert [r.name for r in repos] == ["a", "b"]
    assert repos[0].private is True
    request = first.calls.last.request
    assert request.headers["Authorization"] == f"Bearer {TOKEN}"
    assert request.headers["X-GitHub-Api-Version"] == "2026-03-10"
    assert request.url.params["per_page"] == "100"


@respx.mock
async def test_list_repositories_ignores_next_link_on_foreign_host(
    client: GitHubApiClient,
) -> None:
    respx.get(f"{API}/orgs/acme/repos").mock(
        return_value=httpx.Response(
            200, json=[_repo_json("a")], headers={"Link": '<https://evil.io/x>; rel="next"'}
        )
    )

    repos = await client.list_repositories("acme", TOKEN)

    assert [r.name for r in repos] == ["a"]


@pytest.mark.parametrize(
    "link",
    [
        "https://api.github.com.evil.example/x",
        "http://api.github.com/x",
        "https://api.github.com:8443/x",
    ],
)
@respx.mock
async def test_list_repositories_ignores_lookalike_next_link(
    client: GitHubApiClient, link: str
) -> None:
    respx.get(f"{API}/orgs/acme/repos").mock(
        return_value=httpx.Response(
            200, json=[_repo_json("a")], headers={"Link": f'<{link}>; rel="next"'}
        )
    )

    repos = await client.list_repositories("acme", TOKEN)

    assert [r.name for r in repos] == ["a"]


@respx.mock
async def test_list_repositories_stops_at_max_pages(client: GitHubApiClient) -> None:
    loop = f"{API}/orgs/acme/repos?page=next"
    respx.get(url__startswith=f"{API}/orgs/acme/repos").mock(
        return_value=httpx.Response(
            200, json=[_repo_json("a")], headers={"Link": f'<{loop}>; rel="next"'}
        )
    )

    repos = await client.list_repositories("acme", TOKEN)

    assert len(repos) == MAX_PAGES


@respx.mock
async def test_list_repositories_falls_back_to_own_user_account(client: GitHubApiClient) -> None:
    respx.get(f"{API}/orgs/octo/repos").mock(return_value=httpx.Response(404))
    respx.get(f"{API}/user").mock(return_value=httpx.Response(200, json={"login": "Octo"}))
    own = respx.get(f"{API}/user/repos").mock(
        return_value=httpx.Response(200, json=[_repo_json("private-one")])
    )

    repos = await client.list_repositories("octo", TOKEN)

    assert [r.name for r in repos] == ["private-one"]
    assert own.calls.last.request.url.params["affiliation"] == "owner"


@respx.mock
async def test_list_repositories_falls_back_to_other_user_account(
    client: GitHubApiClient,
) -> None:
    respx.get(f"{API}/orgs/someone/repos").mock(return_value=httpx.Response(404))
    respx.get(f"{API}/user").mock(return_value=httpx.Response(200, json={"login": "me"}))
    respx.get(f"{API}/users/someone/repos").mock(
        return_value=httpx.Response(200, json=[_repo_json("public-one")])
    )

    repos = await client.list_repositories("someone", TOKEN)

    assert [r.name for r in repos] == ["public-one"]


@pytest.mark.parametrize(
    ("response", "error"),
    [
        (httpx.Response(401), GitHubAuthError),
        (httpx.Response(403, headers={"x-ratelimit-remaining": "0"}), GitHubRateLimitError),
        (httpx.Response(429), GitHubRateLimitError),
        (httpx.Response(403, headers={"x-github-sso": "required; url=x"}), GitHubAccessDeniedError),
        (httpx.Response(403), GitHubAccessDeniedError),
        (httpx.Response(500), GitHubUpstreamError),
        (httpx.Response(200, content=b"not-json"), GitHubUpstreamError),
    ],
)
@respx.mock
async def test_list_repositories_maps_errors(
    client: GitHubApiClient, response: httpx.Response, error: type[Exception]
) -> None:
    respx.get(f"{API}/orgs/acme/repos").mock(return_value=response)

    with pytest.raises(error) as exc_info:
        await client.list_repositories("acme", TOKEN)
    assert TOKEN not in str(exc_info.value)


@respx.mock
async def test_list_repositories_maps_transport_error(client: GitHubApiClient) -> None:
    respx.get(f"{API}/orgs/acme/repos").mock(side_effect=httpx.ConnectError("boom"))

    with pytest.raises(GitHubUpstreamError):
        await client.list_repositories("acme", TOKEN)


@respx.mock
async def test_list_repositories_not_found_everywhere(client: GitHubApiClient) -> None:
    respx.get(f"{API}/orgs/ghost/repos").mock(return_value=httpx.Response(404))
    respx.get(f"{API}/user").mock(return_value=httpx.Response(200, json={"login": "me"}))
    respx.get(f"{API}/users/ghost/repos").mock(return_value=httpx.Response(404))

    with pytest.raises(GitHubNotFoundError):
        await client.list_repositories("ghost", TOKEN)


@respx.mock
async def test_get_tree_resolves_branch_with_slash(client: GitHubApiClient) -> None:
    respx.get(f"{API}/repos/acme/api/git/ref/heads/feature/scan").mock(
        return_value=httpx.Response(200, json={"object": {"sha": "abc123"}})
    )
    respx.get(f"{API}/repos/acme/api/git/trees/abc123").mock(
        return_value=httpx.Response(
            200,
            json={
                "truncated": True,
                "tree": [
                    {"path": "src", "type": "tree"},
                    {"path": "src/main.py", "type": "blob", "size": 42},
                    {"path": "vendor/lib", "type": "commit"},
                    {"path": "weird", "type": "unknown"},
                ],
            },
        )
    )

    tree = await client.get_tree("acme", "api", "feature/scan", TOKEN)

    assert [(e.path, e.type, e.size) for e in tree.entries] == [
        ("src", "tree", None),
        ("src/main.py", "blob", 42),
        ("vendor/lib", "commit", None),
    ]
    assert tree.truncated is True
    assert tree.branch == "feature/scan"


@respx.mock
async def test_get_tree_returns_empty_for_empty_repository(client: GitHubApiClient) -> None:
    respx.get(f"{API}/repos/acme/empty/git/ref/heads/main").mock(
        return_value=httpx.Response(409, json={"message": "Git Repository is empty."})
    )

    tree = await client.get_tree("acme", "empty", "main", TOKEN)

    assert tree.entries == []
    assert tree.truncated is False


@respx.mock
async def test_get_tree_missing_branch_raises_not_found(client: GitHubApiClient) -> None:
    respx.get(f"{API}/repos/acme/api/git/ref/heads/nope").mock(return_value=httpx.Response(404))

    with pytest.raises(GitHubNotFoundError):
        await client.get_tree("acme", "api", "nope", TOKEN)


@respx.mock
async def test_list_branches_paginates_and_maps_fields(client: GitHubApiClient) -> None:
    page2 = f"{API}/repositories/1/branches?per_page=100&page=2"
    respx.get(f"{API}/repos/acme/api/branches").mock(
        return_value=httpx.Response(
            200,
            json=[{"name": "main", "protected": True, "commit": {"sha": "abc"}}],
            headers={"Link": f'<{page2}>; rel="next"'},
        )
    )
    respx.get(page2).mock(
        return_value=httpx.Response(200, json=[{"name": "feature/scan", "commit": {}}])
    )

    branches = await client.list_branches("acme", "api", TOKEN)

    assert [(b.name, b.protected, b.commit_sha) for b in branches] == [
        ("main", True, "abc"),
        ("feature/scan", False, None),
    ]


@respx.mock
async def test_list_branches_empty_repository_returns_empty(client: GitHubApiClient) -> None:
    respx.get(f"{API}/repos/acme/empty/branches").mock(return_value=httpx.Response(200, json=[]))

    assert await client.list_branches("acme", "empty", TOKEN) == []


@respx.mock
async def test_list_branches_missing_repository_raises_not_found(
    client: GitHubApiClient,
) -> None:
    respx.get(f"{API}/repos/acme/gone/branches").mock(return_value=httpx.Response(404))

    with pytest.raises(GitHubNotFoundError):
        await client.list_branches("acme", "gone", TOKEN)
