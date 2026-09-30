import asyncio
from collections.abc import Callable, Iterator
from datetime import datetime
from typing import Any

import boto3
from botocore.config import Config
from botocore.exceptions import BotoCoreError, ClientError

from ingestion.domain.enums.clone_status import CloneStatus
from ingestion.domain.errors.github_scan_errors import (
    CloneNotFoundError,
    CloneStorageUnavailableError,
)
from ingestion.domain.models.repository_clone import (
    ArchiveDownload,
    PackedArchive,
    RepositoryClone,
    clone_id_for,
)
from ingestion.infrastructure.config.settings import IngestionSettings

_NOT_FOUND_CODES = {"404", "NoSuchKey", "NoSuchBucket", "NotFound"}
_MAX_LISTED_OBJECTS = 1000
_DOWNLOAD_CHUNK_BYTES = 1024 * 1024
_UNREACHABLE = "Storage is not reachable right now. Try again in a moment."


def create_s3_client(settings: IngestionSettings) -> Any:
    return boto3.client(
        "s3",
        endpoint_url=settings.s3_endpoint_url,
        aws_access_key_id=settings.s3_access_key,
        aws_secret_access_key=settings.s3_secret_key,
        region_name=settings.s3_region,
        config=Config(
            signature_version="s3v4",
            s3={"addressing_style": "path"},
            # Newer boto3 adds checksums by default that older S3-compatible servers reject.
            request_checksum_calculation="when_required",
            response_checksum_validation="when_required",
            connect_timeout=5,
            retries={"max_attempts": 2},
        ),
    )


def object_key(owner: str, repository: str, branch: str) -> str:
    """owner/repo/branch/repo.zip - a branch with slashes becomes nested folders."""
    return f"{owner}/{repository}/{branch}/{repository}.zip"


def _error_code(exc: ClientError) -> str:
    return str(exc.response.get("Error", {}).get("Code", ""))


def _metadata(clone: RepositoryClone) -> dict[str, str]:
    return {
        "owner": clone.owner,
        "repository": clone.repository,
        "branch": clone.branch,
        "commit-sha": clone.commit_sha or "",
        "created-at": clone.created_at.isoformat(),
        "saved-by": clone.saved_by,
    }


def _key_parts(key: str) -> tuple[str, str, str]:
    owner, repository, *branch, _file = key.split("/")
    return owner, repository, "/".join(branch)


def _stored_clone(meta: dict[str, str], obj: dict[str, Any]) -> RepositoryClone:
    key_owner, key_repo, key_branch = _key_parts(obj["Key"])
    owner = meta.get("owner") or key_owner
    repository = meta.get("repository") or key_repo
    branch = meta.get("branch") or key_branch
    modified: datetime = obj["LastModified"]
    return RepositoryClone(
        clone_id=clone_id_for(owner, repository, branch),
        owner=owner,
        repository=repository,
        branch=branch,
        status=CloneStatus.STORED,
        created_at=modified,
        updated_at=modified,
        size_bytes=obj.get("Size"),
        commit_sha=meta.get("commit-sha") or None,
        saved_by=meta.get("saved-by", ""),
    )


def _is_saved_archive(key: str) -> bool:
    # Only owner/repo/branch.../repo.zip objects belong to this feature.
    parts = key.split("/")
    return len(parts) >= 4 and parts[-1] == f"{parts[1]}.zip"


def _closing_chunks(body: Any) -> Iterator[bytes]:
    try:
        yield from body.iter_chunks(chunk_size=_DOWNLOAD_CHUNK_BYTES)
    finally:
        body.close()


class S3CloneArchiveStore:
    """Stores packed clones in an S3-compatible bucket (MinIO locally)."""

    def __init__(self, client: Any, bucket: str, list_limit: int) -> None:
        self._client = client
        self._bucket = bucket
        self._list_limit = list_limit

    async def ensure_ready(self) -> None:
        await self._call(self._ensure_bucket)

    async def save(self, archive: PackedArchive, clone: RepositoryClone) -> int:
        extra = {"Metadata": _metadata(clone), "ContentType": "application/zip"}
        key = object_key(clone.owner, clone.repository, clone.branch)
        await self._call(
            self._client.upload_file, str(archive.path), self._bucket, key, ExtraArgs=extra
        )
        return archive.path.stat().st_size

    async def list_clones(self) -> list[RepositoryClone]:
        return await self._call(self._list_sync)

    async def open_download(self, owner: str, repository: str, branch: str) -> ArchiveDownload:
        key = object_key(owner, repository, branch)
        response = await self._call(self._client.get_object, Bucket=self._bucket, Key=key)
        return ArchiveDownload(
            chunks=_closing_chunks(response["Body"]),
            size_bytes=int(response.get("ContentLength", 0)),
            filename=f"{repository}.zip",
        )

    def _ensure_bucket(self) -> None:
        try:
            self._client.head_bucket(Bucket=self._bucket)
        except ClientError as exc:
            if _error_code(exc) not in _NOT_FOUND_CODES:
                raise
            self._client.create_bucket(Bucket=self._bucket)

    def _list_sync(self) -> list[RepositoryClone]:
        paginator = self._client.get_paginator("list_objects_v2")
        pages = paginator.paginate(
            Bucket=self._bucket, PaginationConfig={"MaxItems": _MAX_LISTED_OBJECTS}
        )
        objects = [
            obj
            for page in pages
            for obj in page.get("Contents", [])
            if _is_saved_archive(obj["Key"])
        ]
        newest = sorted(objects, key=lambda o: o["LastModified"], reverse=True)[: self._list_limit]
        return [self._describe(obj) for obj in newest]

    def _describe(self, obj: dict[str, Any]) -> RepositoryClone:
        head = self._client.head_object(Bucket=self._bucket, Key=obj["Key"])
        return _stored_clone(head.get("Metadata", {}), obj)

    async def _call(self, func: Callable[..., Any], *args: Any, **kwargs: Any) -> Any:
        try:
            return await asyncio.to_thread(func, *args, **kwargs)
        except ClientError as exc:
            if _error_code(exc) in _NOT_FOUND_CODES:
                raise CloneNotFoundError("Saved repository not found.") from exc
            raise CloneStorageUnavailableError(_UNREACHABLE) from exc
        except BotoCoreError as exc:
            raise CloneStorageUnavailableError(_UNREACHABLE) from exc


class UnconfiguredCloneStore:
    """Used when no S3 endpoint is set, so clone calls fail clearly instead of hitting AWS."""

    _MESSAGE = "Saving repositories is not set up on this server."

    async def ensure_ready(self) -> None:
        raise CloneStorageUnavailableError(self._MESSAGE)

    async def save(self, archive: PackedArchive, clone: RepositoryClone) -> int:
        raise CloneStorageUnavailableError(self._MESSAGE)

    async def list_clones(self) -> list[RepositoryClone]:
        raise CloneStorageUnavailableError(self._MESSAGE)

    async def open_download(self, owner: str, repository: str, branch: str) -> ArchiveDownload:
        raise CloneStorageUnavailableError(self._MESSAGE)


def build_clone_store(settings: IngestionSettings) -> S3CloneArchiveStore | UnconfiguredCloneStore:
    if not settings.s3_endpoint_url:
        return UnconfiguredCloneStore()
    client = create_s3_client(settings)
    return S3CloneArchiveStore(client, settings.s3_clone_bucket, settings.clone_list_limit)
