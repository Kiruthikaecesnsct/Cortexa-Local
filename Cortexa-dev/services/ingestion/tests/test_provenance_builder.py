import pytest

from ingestion.application.provenance.provenance_builder import (
    CodeFileContext,
    build_code_entries,
    build_paper_entries,
    build_provenance_map,
)
from ingestion.domain.enums.source_kind import SourceKind
from ingestion.domain.errors.provenance_errors import (
    ChunkNotFoundError,
    InvalidProvenanceEntryError,
)
from ingestion.domain.models.chunk import Chunk
from ingestion.domain.models.provenance_entry import ProvenanceEntry


def _make_chunk(order_index: int, start_char: int, end_char: int, text: str) -> Chunk:
    return Chunk(
        text=text[start_char:end_char],
        order_index=order_index,
        start_char=start_char,
        end_char=end_char,
        token_count=1,
    )


def test_paper_entries_byte_range_matches_utf8() -> None:
    text = "café au lait"
    chunk = _make_chunk(0, 0, len(text), text)
    entries = build_paper_entries("doc1", text, [chunk])
    assert len(entries) == 1
    entry = entries[0]
    assert entry.source_kind == SourceKind.PAPER
    assert entry.byte_range == (0, len(text.encode("utf-8")))
    assert entry.byte_range != (0, len(text))


def test_paper_entry_byte_range_non_ascii_offset() -> None:
    text = "café\nau lait"
    start_char = text.index("au")
    end_char = len(text)
    chunk = _make_chunk(0, start_char, end_char, text)
    entries = build_paper_entries("doc1", text, [chunk])
    expected_byte_start = len(text[:start_char].encode("utf-8"))
    expected_byte_end = len(text[:end_char].encode("utf-8"))
    assert entries[0].byte_range == (expected_byte_start, expected_byte_end)


def test_code_entries_line_range_is_one_based() -> None:
    text = "def foo():\n    pass\n"
    chunk = _make_chunk(0, 0, len(text), text)
    ctx = CodeFileContext(repo_id="repo1", file_path="src/foo.py", text=text)
    entries = build_code_entries(ctx, [chunk])
    assert len(entries) == 1
    entry = entries[0]
    assert entry.source_kind == SourceKind.CODE
    assert entry.line_range == (1, 2)
    assert entry.file_path == "src/foo.py"


def test_code_entries_chunk_id_is_cosmos_legal_for_nested_path() -> None:
    text = "x = 1\n"
    chunk = _make_chunk(0, 0, len(text), text)
    ctx = CodeFileContext(repo_id="repo1", file_path="src/train/model.py", text=text)
    entries = build_code_entries(ctx, [chunk])
    chunk_id = entries[0].chunk_id
    for illegal in ("/", "\\", "?", "#"):
        assert illegal not in chunk_id
    # file_path is preserved on the entry even though it is hashed out of the id.
    assert entries[0].file_path == "src/train/model.py"


def test_code_entries_chunk_id_keyed_on_document() -> None:
    text = "x = 1\n"
    chunk = _make_chunk(0, 0, len(text), text)
    ctx = CodeFileContext(repo_id="repo-guid", file_path="a/b.py", text=text)
    entries = build_code_entries(ctx, [chunk])
    assert entries[0].chunk_id.startswith("repo-guid|")


def test_code_entries_chunk_ids_unique_across_files_and_chunks() -> None:
    text = "a = 1\nb = 2\nc = 3\n"
    chunks = [_make_chunk(i, 0, len(text), text) for i in range(2)]
    ids_file_a = {
        e.chunk_id
        for e in build_code_entries(
            CodeFileContext(repo_id="repo1", file_path="dir/a.py", text=text), chunks
        )
    }
    ids_file_b = {
        e.chunk_id
        for e in build_code_entries(
            CodeFileContext(repo_id="repo1", file_path="dir/b.py", text=text), chunks
        )
    }
    # Two chunks per file, two files, all ids distinct.
    assert len(ids_file_a) == 2
    assert len(ids_file_b) == 2
    assert ids_file_a.isdisjoint(ids_file_b)


def test_get_by_chunk_id_round_trip() -> None:
    text = "hello\nworld\n"
    chunk = _make_chunk(0, 0, len(text), text)
    entries = build_paper_entries("doc42", text, [chunk])
    pmap = build_provenance_map("batch99", entries)
    found = pmap.get_by_chunk_id("doc42|0")
    assert found.doc_id == "doc42"
    assert found.order_index == 0


def test_get_by_chunk_id_not_found_raises() -> None:
    pmap = build_provenance_map("batch1", [])
    with pytest.raises(ChunkNotFoundError):
        pmap.get_by_chunk_id("nonexistent|0")


def test_invalid_paper_entry_missing_byte_range() -> None:
    with pytest.raises(InvalidProvenanceEntryError):
        ProvenanceEntry(
            chunk_id="doc1|0",
            source_kind=SourceKind.PAPER,
            order_index=0,
            doc_id="doc1",
        )


def test_invalid_code_entry_missing_file_path() -> None:
    with pytest.raises(InvalidProvenanceEntryError):
        ProvenanceEntry(
            chunk_id="repo1|0",
            source_kind=SourceKind.CODE,
            order_index=0,
            doc_id="repo1",
            line_range=(1, 5),
        )
