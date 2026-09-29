from pydantic import BaseModel

from ingestion.domain.enums.git_host import GitHost


class RepoRef(BaseModel):
    host: GitHost
    owner: str
    repo: str
    normalized_https_url: str
    use_ssh: bool = False
