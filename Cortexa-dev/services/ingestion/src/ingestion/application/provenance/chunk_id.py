import hashlib

# Characters Azure Cosmos DB forbids in an item id.
_COSMOS_ILLEGAL_ID_CHARS = ("/", "\\", "?", "#")


def build_chunk_id(source_id: str, order_index: int) -> str:
    return f"{source_id}|{order_index}"


def build_code_chunk_id(doc_id: str, file_path: str, order_index: int) -> str:
    # Code chunk ids must be Cosmos-legal: file paths contain '/', which Cosmos
    # forbids in an item id. Hash the path (stable, collision-resistant) and key
    # the id on the document GUID so ids are unique per (document, file, chunk).
    # The raw file_path is preserved on the ProvenanceEntry, so nothing is lost.
    path_hash = hashlib.blake2b(file_path.encode("utf-8"), digest_size=8).hexdigest()
    return f"{doc_id}|{path_hash}|{order_index}"


def is_cosmos_legal_id(item_id: str) -> bool:
    return not any(char in item_id for char in _COSMOS_ILLEGAL_ID_CHARS)
