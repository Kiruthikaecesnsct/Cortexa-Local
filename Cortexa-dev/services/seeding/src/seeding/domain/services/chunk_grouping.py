from dataclasses import dataclass
from uuid import UUID, uuid5

DIGEST_NS = UUID("f1d2c3b4-a596-5a87-9b0c-1d2e3f4a5b6c")


def section_label(chunk: dict) -> str:
    hint = chunk.get("section_hint")
    if hint:
        return str(hint)
    metadata = chunk.get("metadata") or {}
    file_path = metadata.get("file_path") or chunk.get("file_path")
    if file_path:
        return str(file_path).replace("\\", "/").rstrip("/").rsplit("/", 1)[-1]
    return ""


def page_number(chunk: dict) -> int:
    raw = chunk.get("page_number")
    return -1 if raw is None else int(raw)


def section_key(batch_id: str, document_id: str, partition_key: str) -> str:
    return str(uuid5(DIGEST_NS, f"{batch_id}:{document_id}:{partition_key}"))


@dataclass(frozen=True)
class ChunkGroup:
    partition_key: str
    group_key: str
    section_label: str
    chunks: list[dict]

    @property
    def chunk_ids(self) -> list[str]:
        return [str(chunk["id"]) for chunk in self.chunks]


def _order_index(chunk: dict) -> int:
    return int(chunk.get("order_index") or 0)


def _token_count(chunk: dict) -> int:
    return int(chunk.get("token_count") or 0)


def _bucket_by_group(chunks: list[dict]) -> dict[str, list[dict]]:
    buckets: dict[str, list[dict]] = {}
    for chunk in chunks:
        buckets.setdefault(section_label(chunk), []).append(chunk)
    for members in buckets.values():
        members.sort(key=_order_index)
    return buckets


def _ordered_group_keys(buckets: dict[str, list[dict]]) -> list[str]:
    return sorted(buckets, key=lambda key: _order_index(buckets[key][0]))


def _split_partitions(chunks: list[dict], max_group_tokens: int) -> list[list[dict]]:
    partitions: list[list[dict]] = []
    current: list[dict] = []
    total = 0
    for chunk in chunks:
        tokens = _token_count(chunk)
        if current and total + tokens > max_group_tokens:
            partitions.append(current)
            current, total = [], 0
        current.append(chunk)
        total += tokens
    if current:
        partitions.append(current)
    return partitions


def group_chunks(chunks: list[dict], max_group_tokens: int) -> list[ChunkGroup]:
    buckets = _bucket_by_group(chunks)
    groups: list[ChunkGroup] = []
    for key in _ordered_group_keys(buckets):
        for index, partition in enumerate(_split_partitions(buckets[key], max_group_tokens)):
            groups.append(
                ChunkGroup(
                    partition_key=f"{key}#part{index}",
                    group_key=key,
                    section_label=key,
                    chunks=partition,
                )
            )
    return groups
