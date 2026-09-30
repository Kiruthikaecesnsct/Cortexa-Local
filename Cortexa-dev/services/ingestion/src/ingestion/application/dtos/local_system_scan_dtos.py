from pydantic import BaseModel, Field, SecretStr

from ingestion.domain.models.local_system_scan import DirectoryEntry

_MAX_HOST_LENGTH = 255
_MAX_USERNAME_LENGTH = 64
_MAX_KEY_LENGTH = 16_384
_MAX_PATH_LENGTH = 4096
_DEFAULT_SSH_PORT = 22


class ListDirectoryRequest(BaseModel):
    host: str = Field(min_length=1, max_length=_MAX_HOST_LENGTH)
    port: int = Field(default=_DEFAULT_SSH_PORT, ge=1, le=65535)
    username: str = Field(min_length=1, max_length=_MAX_USERNAME_LENGTH)
    # PEM/OpenSSH private key content, pasted by the user. Used only for this
    # request and never persisted.
    private_key: SecretStr = Field(min_length=1, max_length=_MAX_KEY_LENGTH)
    passphrase: SecretStr | None = None
    path: str = Field(min_length=1, max_length=_MAX_PATH_LENGTH)


class ListDirectoryResponse(BaseModel):
    path: str
    entries: list[DirectoryEntry]
    truncated: bool = False
