import asyncio
from collections.abc import Callable, Iterator
from datetime import datetime
from typing import Any

import boto3
from botocore.config import Config
from botocore.exceptions import BotoCoreError, ClientError

from ingestion.domain.enums.clone_status import CloneStatus
from ingestion.domain.enums.source_provider import SourceProvider
from ingestion.domain.errors.scan_errors import (
    CloneNotFoundError,
    CloneStorageUnavailableError,
)
from ingestion.domain.models.repository_clone import (
    ArchiveDownload,
    PackedArchive,
    RepositoryClone,
    SaveTarget,
)
from ingestion.infrastructure.config.settings import IngestionSettings

_NOT_FOUND_CODES = {"404", "NoSuchKey", "NoSuchBucket", "NotFound"}
_MAX_LISTED_OBJECTS = 1000
_DOWNLOAD_CHUNK_BYTES = 1024 * 1024
_UNREACHABLE = "Storage is not reachable right now. Try again in a moment."
# Path segments that make up the repository part of a key: GitHub "repo",
# Azure DevOps "project/repo".
_REPOSITORY_SEGMENTS = {SourceProvider.GITHUB: 1, SourceProvider.AZURE_DEVOPS: 2}


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


def object_key(target: SaveTarget) -> str:
    """provider/owner/repository/branch/repo.zip - a branch with slashes becomes nested folders.

    GitHub:       github/{owner}/{repo}/{branch}/{repo}.zip
    Azure DevOps: azure-devops/{organization}/{project}/{repo}/{branch}/{repo}.zip
    """
    return (
        f"{target.provider}/{target.owner}/{target.repository}/{target.branch}/"
        f"{target.repository_name}.zip"
    )


def target_from_key(provider: SourceProvider, key: str) -> SaveTarget | None:
    """Reverse of object_key; None for objects that are not saved branches."""
    parts = key.split("/")[1:]
    repo_segments = _REPOSITORY_SEGMENTS[provider]
    if len(parts) < repo_segments + 3:
        return None
    repository = parts[1 : 1 + repo_segments]
    if parts[-1] != f"{repository[-1]}.zip":
        return None
    branch = "/".join(parts[1 + repo_segments : -1])
    return SaveTarget(provider, parts[0], "/".join(repository), branch)


def _error_code(exc: ClientError) -> str:
    return str(exc.response.get("Error", {}).get("Code", ""))


def _metadata(clone: RepositoryClone) -> dict[str, str]:
    return {
        "provider": clone.provider,
        "owner": clone.owner,
        "repository": clone.repository,
        "branch": clone.branch,
        "commit-sha": clone.commit_sha or "",
        "created-at": clone.created_at.isoformat(),
        "saved-by": clone.saved_by,
    }


def _stored_clone(target: SaveTarget, meta: dict[str, str], obj: dict[str, Any]) -> RepositoryClone:
    modified: datetime = obj["LastModified"]
    return RepositoryClone(
        clone_id=target.clone_id,
        provider=target.provider,
        owner=target.owner,
        repository=target.repository,
        branch=target.branch,
        status=CloneStatus.STORED,
        created_at=modified,
        updated_at=modified,
        size_bytes=obj.get("Size"),
        commit_sha=meta.get("commit-sha") or None,
        saved_by=meta.get("saved-by", ""),
    )


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
        key = object_key(clone.target)
        await self._call(
            self._client.upload_file, str(archive.path), self._bucket, key, ExtraArgs=extra
        )
        return archive.path.stat().st_size

    async def list_clones(self, provider: SourceProvider) -> list[RepositoryClone]:
        return await self._call(self._list_sync, provider)

    async def open_download(self, target: SaveTarget) -> ArchiveDownload:
        key = object_key(target)
        response = await self._call(self._client.get_object, Bucket=self._bucket, Key=key)
        return ArchiveDownload(
            chunks=_closing_chunks(response["Body"]),
            size_bytes=int(response.get("ContentLength", 0)),
            filename=f"{target.repository_name}.zip",
        )

    def _ensure_bucket(self) -> None:
        try:
            self._client.head_bucket(Bucket=self._bucket)
        except ClientError as exc:
            if _error_code(exc) not in _NOT_FOUND_CODES:
                raise
            self._client.create_bucket(Bucket=self._bucket)

    def _list_sync(self, provider: SourceProvider) -> list[RepositoryClone]:
        paginator = self._client.get_paginator("list_objects_v2")
        pages = paginator.paginate(
            Bucket=self._bucket,
            Prefix=f"{provider}/",
            PaginationConfig={"MaxItems": _MAX_LISTED_OBJECTS},
        )
        saved = [
            (target, obj)
            for page in pages
            for obj in page.get("Contents", [])
            if (target := target_from_key(provider, obj["Key"])) is not None
        ]
        saved.sort(key=lambda pair: pair[1]["LastModified"], reverse=True)
        return [self._describe(target, obj) for target, obj in saved[: self._list_limit]]

    def _describe(self, target: SaveTarget, obj: dict[str, Any]) -> RepositoryClone:
        head = self._client.head_object(Bucket=self._bucket, Key=obj["Key"])
        return _stored_clone(target, head.get("Metadata", {}), obj)

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

    async def list_clones(self, provider: SourceProvider) -> list[RepositoryClone]:
        raise CloneStorageUnavailableError(self._MESSAGE)

    async def open_download(self, target: SaveTarget) -> ArchiveDownload:
        raise CloneStorageUnavailableError(self._MESSAGE)


def build_clone_store(settings: IngestionSettings) -> S3CloneArchiveStore | UnconfiguredCloneStore:
    if not settings.s3_endpoint_url:
        return UnconfiguredCloneStore()
    client = create_s3_client(settings)
    return S3CloneArchiveStore(client, settings.s3_clone_bucket, settings.clone_list_limit)
