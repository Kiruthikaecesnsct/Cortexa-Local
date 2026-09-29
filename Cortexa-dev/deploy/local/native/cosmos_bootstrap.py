"""Create the cortexa-pipeline database and its containers on a local Cosmos DB emulator.

Mirrors deploy/modules/cosmos-db/main.tf (the source of truth for the Azure topology):
every container partitions on /batch_id. Safe to re-run - existing entities are left as-is.

Reads COSMOS_URI and COSMOS_KEY from the environment. Run from any Python service folder:
    uv run python ../../deploy/local/native/cosmos_bootstrap.py
"""

import os
import sys

from azure.cosmos import CosmosClient, PartitionKey

DATABASE = "cortexa-pipeline"
PARTITION_KEY_PATH = "/batch_id"
CONTAINERS = (
    "documents",
    "chunks",
    "provenance_maps",
    "candidates",
    "evidence_bundles",
    "verdicts",
    "reports",
    "harvesting",
    "seeding",
    "batches",
    "config",
)


def _required_env(name: str) -> str:
    value = os.environ.get(name, "").strip()
    if not value:
        raise SystemExit(f"{name} is not set.")
    return value


def main() -> int:
    client = CosmosClient(_required_env("COSMOS_URI"), credential=_required_env("COSMOS_KEY"))
    database = client.create_database_if_not_exists(DATABASE)
    for name in CONTAINERS:
        database.create_container_if_not_exists(
            id=name, partition_key=PartitionKey(path=PARTITION_KEY_PATH)
        )
        print(f"container {DATABASE}/{name}")
    print("Cosmos database is in place.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
