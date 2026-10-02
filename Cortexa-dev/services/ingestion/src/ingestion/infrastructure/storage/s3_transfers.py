"""Blocking S3 transfers for saved folders; callers run these off the event loop."""

import mimetypes
import shutil
import zipfile
from collections.abc import Callable, Iterable
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from pathlib import Path
from typing import Any

_DELETE_BATCH = 1000
_DEFAULT_CONTENT_TYPE = "application/octet-stream"


@dataclass(frozen=True)
class Bucket:
    client: Any
    name: str
    concurrency: int

    def run_all[T](self, func: Callable[[T], Any], items: Iterable[T]) -> list[Any]:
        """Runs func over items on a bounded pool; the first failure is re-raised."""
        with ThreadPoolExecutor(max_workers=self.concurrency) as pool:
            return list(pool.map(func, items))


def _content_type(name: str) -> str:
    return mimetypes.guess_type(name)[0] or _DEFAULT_CONTENT_TYPE


def upload_files(bucket: Bucket, root: Path, files: Iterable[str], prefix: str) -> int:
    """Uploads each file under prefix with its repository path; returns the total bytes."""

    def upload(name: str) -> int:
        source = root / name
        extra = {"ContentType": _content_type(name)}
        bucket.client.upload_file(str(source), bucket.name, prefix + name, ExtraArgs=extra)
        return source.stat().st_size

    return sum(bucket.run_all(upload, files))


def download_files(bucket: Bucket, prefix: str, files: Iterable[tuple[str, Path]]) -> None:
    """Downloads each (name, destination) pair, creating folders as needed."""

    def download(item: tuple[str, Path]) -> None:
        name, destination = item
        destination.parent.mkdir(parents=True, exist_ok=True)
        bucket.client.download_file(bucket.name, prefix + name, str(destination))

    bucket.run_all(download, files)


def write_zip(bucket: Bucket, prefix: str, files: Iterable[str], zip_path: Path) -> None:
    """Streams every file into one zip, one object at a time to keep memory flat."""
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name in files:
            body = bucket.client.get_object(Bucket=bucket.name, Key=prefix + name)["Body"]
            try:
                with archive.open(name, "w") as entry:
                    shutil.copyfileobj(body, entry)
            finally:
                body.close()


def delete_keys(bucket: Bucket, keys: list[str]) -> None:
    for start in range(0, len(keys), _DELETE_BATCH):
        batch = [{"Key": key} for key in keys[start : start + _DELETE_BATCH]]
        bucket.client.delete_objects(Bucket=bucket.name, Delete={"Objects": batch, "Quiet": True})
