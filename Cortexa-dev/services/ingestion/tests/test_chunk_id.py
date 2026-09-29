from ingestion.application.provenance.chunk_id import (
    build_chunk_id,
    build_code_chunk_id,
    is_cosmos_legal_id,
)


def test_build_chunk_id_paper_format_unchanged():
    assert build_chunk_id("doc-guid", 3) == "doc-guid|3"


def test_build_code_chunk_id_is_cosmos_legal():
    chunk_id = build_code_chunk_id("repo-guid", "src/train/model.py", 0)
    assert is_cosmos_legal_id(chunk_id)


def test_build_code_chunk_id_keyed_on_document():
    assert build_code_chunk_id("repo-guid", "a/b.py", 2).startswith("repo-guid|")


def test_build_code_chunk_id_deterministic():
    a = build_code_chunk_id("repo-guid", "src/a.py", 1)
    b = build_code_chunk_id("repo-guid", "src/a.py", 1)
    assert a == b


def test_build_code_chunk_id_distinct_per_file_and_order():
    ids = {
        build_code_chunk_id("repo-guid", "src/a.py", 0),
        build_code_chunk_id("repo-guid", "src/b.py", 0),
        build_code_chunk_id("repo-guid", "src/a.py", 1),
    }
    assert len(ids) == 3


def test_is_cosmos_legal_id_detects_illegal_chars():
    assert not is_cosmos_legal_id("src/a.py|0")
    assert not is_cosmos_legal_id("a\\b|0")
    assert not is_cosmos_legal_id("a?b|0")
    assert not is_cosmos_legal_id("a#b|0")
    assert is_cosmos_legal_id("repo-guid|abc123|0")
