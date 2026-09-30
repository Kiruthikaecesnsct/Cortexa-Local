import re

from ingestion.domain.errors.scan_errors import MissingUserContextError

_USER_ID = re.compile(r"^[A-Za-z0-9-]{1,64}$")


def validate_user_id(user_id: str | None) -> str:
    if not user_id or not _USER_ID.match(user_id):
        raise MissingUserContextError("Sign in to save repositories.")
    return user_id
