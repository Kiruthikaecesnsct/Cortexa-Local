from ingestion.application.code_ingestor import assemble_code_ingest_request
from ingestion.application.raw_file_ingestor import IngestorConfig
from ingestion.domain.enums.source_kind import SourceKind

BATCH_ID = "batch-001"
DOCUMENT_ID = "repo-001"
CORRELATION_ID = "corr-001"
CONFIG = IngestorConfig(chunk_size=512, chunk_overlap=50, chunk_encoding="cl100k_base")


def test_assemble_code_ingest_request_single_file():
    files = [("src/main.py", "def main():\n    pass\n")]

    request = assemble_code_ingest_request(BATCH_ID, DOCUMENT_ID, files, CORRELATION_ID, CONFIG)

    assert request.batch_id == BATCH_ID
    assert request.document_id == DOCUMENT_ID
    assert request.source_kind == SourceKind.CODE
    assert request.raw_content == b""
    assert len(request.chunks) >= 1
    assert request.provenance_map.batch_id == BATCH_ID


def test_assemble_code_ingest_request_multiple_files():
    files = [
        ("src/main.py", "def main():\n    pass\n"),
        ("src/utils.py", "def helper():\n    return 42\n"),
        ("README.md", "# Project\nDescription here.\n"),
    ]

    request = assemble_code_ingest_request(BATCH_ID, DOCUMENT_ID, files, CORRELATION_ID, CONFIG)

    assert request.source_kind == SourceKind.CODE
    assert request.raw_content == b""
    assert len(request.chunks) >= 3
    assert request.provenance_map.batch_id == BATCH_ID


def test_assemble_code_ingest_request_empty_files_list():
    files = []

    request = assemble_code_ingest_request(BATCH_ID, DOCUMENT_ID, files, CORRELATION_ID, CONFIG)

    assert request.source_kind == SourceKind.CODE
    assert request.raw_content == b""
    assert len(request.chunks) == 0
    assert len(request.provenance_map.entries) == 0


def test_assemble_code_ingest_request_file_yields_multiple_chunks():
    large_file_content = "def function():\n    pass\n" * 100
    files = [("src/large.py", large_file_content)]

    request = assemble_code_ingest_request(BATCH_ID, DOCUMENT_ID, files, CORRELATION_ID, CONFIG)

    assert len(request.chunks) > 1


def test_assemble_code_ingest_request_chunks_have_sequential_order_across_files():
    files = [
        ("a.py", "x = 1\n" * 10),
        ("b.py", "y = 2\n" * 10),
        ("c.py", "z = 3\n" * 10),
    ]

    request = assemble_code_ingest_request(BATCH_ID, DOCUMENT_ID, files, CORRELATION_ID, CONFIG)

    order_indices = [chunk.order_index for chunk in request.chunks]
    assert order_indices == sorted(order_indices)
    assert order_indices == list(range(len(order_indices)))


def test_assemble_code_ingest_request_provenance_file_path_correct():
    files = [
        ("src/foo.py", "def foo():\n    pass\n"),
        ("lib/bar.py", "def bar():\n    pass\n"),
    ]

    request = assemble_code_ingest_request(BATCH_ID, DOCUMENT_ID, files, CORRELATION_ID, CONFIG)

    entries = request.provenance_map.entries
    file_paths = {entry.file_path for entry in entries}
    assert "src/foo.py" in file_paths
    assert "lib/bar.py" in file_paths


def test_assemble_code_ingest_request_provenance_line_range_correct():
    text = "line1\nline2\nline3\n"
    files = [("test.py", text)]

    request = assemble_code_ingest_request(BATCH_ID, DOCUMENT_ID, files, CORRELATION_ID, CONFIG)

    entries = request.provenance_map.entries
    assert len(entries) >= 1
    first_entry = entries[0]
    assert first_entry.line_range is not None
    assert first_entry.line_range[0] == 1
    assert first_entry.line_range[1] >= 1


def test_assemble_code_ingest_request_filename_empty():
    files = [("src/main.py", "code here")]

    request = assemble_code_ingest_request(BATCH_ID, DOCUMENT_ID, files, CORRELATION_ID, CONFIG)

    assert request.filename == ""


def test_assemble_code_ingest_request_source_kind_always_code():
    files = [("README.md", "# Not Python\nStill code ingestion.\n")]

    request = assemble_code_ingest_request(BATCH_ID, DOCUMENT_ID, files, CORRELATION_ID, CONFIG)

    assert request.source_kind == SourceKind.CODE


def test_assemble_code_ingest_request_skips_geometry_capture_entirely():
    files = [("src/main.py", "def main():\n    pass\n" * 30)]

    request = assemble_code_ingest_request(BATCH_ID, DOCUMENT_ID, files, CORRELATION_ID, CONFIG)

    assert request.viewable_pdf is None
    assert request.geometry_reason is None
    for chunk in request.chunks:
        assert chunk.chunk_rects is None
        assert chunk.page_dimensions is None
        assert chunk.page_number is None
