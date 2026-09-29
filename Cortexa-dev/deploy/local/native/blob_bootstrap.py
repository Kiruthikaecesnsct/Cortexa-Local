"""Create the Cortexa blob containers on a local Azurite instance.

Mirrors the `containers` list passed to the blob-storage module in
deploy/environments/dev/main.tf. Safe to re-run - existing containers are left as-is.

Reads BLOB_CONNECTION_STRING from the environment. Run from any Python service folder:
    uv run python ../../deploy/local/native/blob_bootstrap.py
"""

import os
import sys

from azure.core.exceptions import ResourceExistsError
from azure.storage.blob import BlobServiceClient

CONTAINERS = ("raw-files", "corpus", "viewable-docs")


def _connection_string() -> str:
    value = os.environ.get("BLOB_CONNECTION_STRING", "").strip()
    if not value:
        raise SystemExit("BLOB_CONNECTION_STRING is not set.")
    return value


def main() -> int:
    service = BlobServiceClient.from_connection_string(_connection_string())
    for name in CONTAINERS:
        try:
            service.create_container(name)
        except ResourceExistsError:
            pass
        print(f"blob container {name}")
    print("Blob containers are in place.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
