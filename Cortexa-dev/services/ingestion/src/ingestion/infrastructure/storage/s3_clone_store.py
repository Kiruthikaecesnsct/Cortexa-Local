import asyncio
import json
import logging
import shutil
import tempfile
from collections.abc import Callable, Iterator
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
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
    RepositoryCheckout,
    RepositoryClone,
    SaveTarget,
)
from ingestion.infrastructure.config.settings import IngestionSettings
from ingestion.infrastructure.storage.clone_keys import (
    folder_prefix,
    legacy_zip_name,
    local_path,
    marker_key,
    marker_prefix,
    safe_relative_path,
    target_from_marker_key,
)
from ingestion.infrastructure.storage.s3_transfers import (
    Bucket,
    delete_keys,
    download_files,
    upload_files,
    write_zip,
)

_logger = logging.getLogger(__name__)

_NOT_FOUND_CODES = {"404", "NoSuchKey", "NoSuchBucket", "NotFound"}
_MAX_LISTED_OBJECTS = 1000
_DOWNLOAD_CHUNK_BYTES = 1024 * 1024
_UNREACHABLE = "Storage is not reachable right now. Try again in a moment."
_TOTAL_BYTES = "total-bytes"


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
            # One connection per transfer thread, so parallel uploads do not queue on the pool.
            max_pool_connections=max(10, settings.clone_transfer_concurrency),
        ),
    )


@dataclass(frozen=True)
class CloneStoreOptions:
    bucket: str
    list_limit: int
    workdir: str
    transfer_concurrency: int


def _error_code(exc: ClientError) -> str:
    return str(exc.response.get("Error", {}).get("Code", ""))


def _metadata(clone: RepositoryClone, total_bytes: int) -> dict[str, str]:
    return {
        "provider": clone.provider,
        "owner": clone.owner,
        "repository": clone.repository,
        "branch": clone.branch,
        "commit-sha": clone.commit_sha or "",
        "created-at": clone.created_at.isoformat(),
        "saved-by": clone.saved_by,
        _TOTAL_BYTES: str(total_bytes),
    }


def _total_bytes(meta: dict[str, str]) -> int | None:
    value = meta.get(_TOTAL_BYTES, "")
    return int(value) if value.isdigit() else None


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
        size_bytes=_total_bytes(meta),
        commit_sha=meta.get("commit-sha") or None,
        saved_by=meta.get("saved-by", ""),
    )


def _manifest_files(raw: bytes) -> list[str]:
    try:
        manifest = json.loads(raw)
    except ValueError:
        # A damaged marker lists nothing; saving the branch again rewrites it.
        _logger.warning("Saved repository marker is not valid JSON")
        return []
    files = manifest.get("files", []) if isinstance(manifest, dict) else []
    return [name for name in files if isinstance(name, str) and safe_relative_path(name)]


def _zip_chunks(folder: Path, zip_path: Path) -> Iterator[bytes]:
    try:
        with zip_path.open("rb") as handle:
            while chunk := handle.read(_DOWNLOAD_CHUNK_BYTES):
                yield chunk
    finally:
        shutil.rmtree(folder, ignore_errors=True)


class S3CloneFolderStore:
    """Stores each saved branch as a plain folder in an S3-compatible bucket (MinIO locally)."""

    def __init__(self, client: Any, options: CloneStoreOptions) -> None:
        self._client = client
        self._options = options
        self._bucket = Bucket(client, options.bucket, options.transfer_concurrency)

    async def ensure_ready(self) -> None:
        await self._call(self._ensure_bucket)

    async def save(self, checkout: RepositoryCheckout, clone: RepositoryClone) -> int:
        return await self._call(self._save_sync, checkout, clone)

    async def list_clones(self, provider: SourceProvider) -> list[RepositoryClone]:
        return await self._call(self._list_sync, provider)

    async def open_download(self, target: SaveTarget) -> ArchiveDownload:
        folder, zip_path = await self._call(self._zip_sync, target)
        return ArchiveDownload(
            chunks=_zip_chunks(folder, zip_path),
            size_bytes=zip_path.stat().st_size,
            filename=legacy_zip_name(target),
        )

    async def fetch_folder(self, target: SaveTarget, destination: Path) -> None:
        await self._call(self._fetch_sync, target, destination)

    def _ensure_bucket(self) -> None:
        try:
            self._client.head_bucket(Bucket=self._options.bucket)
        except ClientError as exc:
            if _error_code(exc) not in _NOT_FOUND_CODES:
                raise
            self._client.create_bucket(Bucket=self._options.bucket)

    def _save_sync(self, checkout: RepositoryCheckout, clone: RepositoryClone) -> int:
        target, prefix = clone.target, folder_prefix(clone.target)
        previous = self._manifest_or_empty(target)
        total = upload_files(self._bucket, checkout.path, checkout.files, prefix)
        # The marker goes last, so a save that fails part-way never lists as stored.
        self._put_marker(clone, checkout.files, total)
        stale = ({*previous, legacy_zip_name(target)}) - set(checkout.files)
        delete_keys(self._bucket, [prefix + name for name in sorted(stale)])
        return total

    def _put_marker(self, clone: RepositoryClone, files: tuple[str, ...], total: int) -> None:
        self._client.put_object(
            Bucket=self._options.bucket,
            Key=marker_key(clone.target),
            Body=json.dumps({"files": list(files)}).encode(),
            ContentType="application/json",
            Metadata=_metadata(clone, total),
        )

    def _manifest(self, target: SaveTarget) -> list[str]:
        response = self._client.get_object(Bucket=self._options.bucket, Key=marker_key(target))
        with response["Body"] as body:
            return _manifest_files(body.read())

    def _manifest_or_empty(self, target: SaveTarget) -> list[str]:
        try:
            return self._manifest(target)
        except ClientError as exc:
            if _error_code(exc) not in _NOT_FOUND_CODES:
                raise
            return []

    def _list_sync(self, provider: SourceProvider) -> list[RepositoryClone]:
        paginator = self._client.get_paginator("list_objects_v2")
        pages = paginator.paginate(
            Bucket=self._options.bucket,
            Prefix=marker_prefix(provider),
            PaginationConfig={"MaxItems": _MAX_LISTED_OBJECTS},
        )
        saved = [
            (target, obj)
            for page in pages
            for obj in page.get("Contents", [])
            if (target := target_from_marker_key(provider, obj["Key"])) is not None
        ]
        saved.sort(key=lambda pair: pair[1]["LastModified"], reverse=True)
        return [self._describe(target, obj) for target, obj in saved[: self._options.list_limit]]

    def _describe(self, target: SaveTarget, obj: dict[str, Any]) -> RepositoryClone:
        head = self._client.head_object(Bucket=self._options.bucket, Key=obj["Key"])
        return _stored_clone(target, head.get("Metadata", {}), obj)

    def _zip_sync(self, target: SaveTarget) -> tuple[Path, Path]:
        files = self._manifest(target)
        folder = Path(tempfile.mkdtemp(prefix="download-", dir=self._options.workdir))
        zip_path = folder / legacy_zip_name(target)
        try:
            write_zip(self._bucket, folder_prefix(target), files, zip_path)
        except BaseException:
            shutil.rmtree(folder, ignore_errors=True)
            raise
        return folder, zip_path

    def _fetch_sync(self, target: SaveTarget, destination: Path) -> None:
        pairs = []
        for name in self._manifest(target):
            path = local_path(destination, name)
            if path is None:
                _logger.warning("Skipped unsafe saved path %r in %s", name, target.clone_id)
                continue
            pairs.append((name, path))
        download_files(self._bucket, folder_prefix(target), pairs)

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

    async def save(self, checkout: RepositoryCheckout, clone: RepositoryClone) -> int:
        raise CloneStorageUnavailableError(self._MESSAGE)

    async def list_clones(self, provider: SourceProvider) -> list[RepositoryClone]:
        raise CloneStorageUnavailableError(self._MESSAGE)

    async def open_download(self, target: SaveTarget) -> ArchiveDownload:
        raise CloneStorageUnavailableError(self._MESSAGE)

    async def fetch_folder(self, target: SaveTarget, destination: Path) -> None:
        raise CloneStorageUnavailableError(self._MESSAGE)


def build_clone_store(settings: IngestionSettings) -> S3CloneFolderStore | UnconfiguredCloneStore:
    if not settings.s3_endpoint_url:
        return UnconfiguredCloneStore()
    options = CloneStoreOptions(
        bucket=settings.s3_clone_bucket,
        list_limit=settings.clone_list_limit,
        workdir=settings.clone_workdir,
        transfer_concurrency=settings.clone_transfer_concurrency,
    )
    return S3CloneFolderStore(create_s3_client(settings), options)
