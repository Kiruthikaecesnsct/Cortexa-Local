from harvesting.domain.services.evidence_citation_resolver import resolve_provenance_links


def _pdf_candidate() -> dict:
    return {
        "document_id": "doc-pdf",
        "source_chunk_index": 2,
        "source_span": {
            "locator": "chars:0-40",
            "source_kind": "paper",
            "span_start": 0,
            "span_end": 40,
            "excerpt": "A method for doing X in a novel way.",
        },
    }


def _pdf_chunk() -> dict:
    return {
        "id": "doc-pdf|2",
        "document_id": "doc-pdf",
        "order_index": 2,
        "text": "A method for doing X in a novel way.",
        "start_char": 0,
        "end_char": 40,
        "chunk_rects": [
            {"page_number": 1, "x0": 10.0, "x1": 200.0, "top": 50.0, "bottom": 65.0},
            {"page_number": 1, "x0": 10.0, "x1": 180.0, "top": 66.0, "bottom": 81.0},
        ],
        "page_dimensions": [{"page_number": 1, "width": 612.0, "height": 792.0}],
    }


def _pdf_document() -> dict:
    return {"id": "doc-pdf", "viewable_blob_uri": "https://blob.example.com/doc-pdf.pdf"}


def test_pdf_candidate_with_geometry_emits_pdf_preview_kind_and_rects():
    links = resolve_provenance_links(_pdf_candidate(), chunk=_pdf_chunk(), document=_pdf_document())

    assert len(links) == 1
    link = links[0]
    assert link.preview_kind == "pdf"
    assert link.chunk_id == "doc-pdf|2"
    assert len(link.highlight_rects) == 2
    assert link.highlight_rects[0].page_number == 1
    assert link.highlight_rects[0].x0 == 10.0
    assert len(link.page_dimensions) == 1
    assert link.page_dimensions[0].width == 612.0
    assert link.file_path is None
    assert link.line_range is None
    assert link.clean_excerpt == "A method for doing X in a novel way."


def test_pdf_candidate_missing_viewable_blob_uri_degrades_to_none_no_rects():
    document_without_pdf = {"id": "doc-pdf", "viewable_blob_uri": None}

    links = resolve_provenance_links(
        _pdf_candidate(), chunk=_pdf_chunk(), document=document_without_pdf
    )

    assert links[0].preview_kind == "none"
    assert links[0].highlight_rects is None
    assert links[0].page_dimensions is None


def test_pdf_candidate_missing_chunk_rects_degrades_to_none():
    chunk_without_geometry = dict(_pdf_chunk())
    chunk_without_geometry["chunk_rects"] = None
    chunk_without_geometry["page_dimensions"] = None

    links = resolve_provenance_links(
        _pdf_candidate(), chunk=chunk_without_geometry, document=_pdf_document()
    )

    assert links[0].preview_kind == "none"
    assert links[0].highlight_rects is None


def test_code_candidate_emits_code_preview_kind_with_file_path_and_line_range_no_rects():
    candidate = {
        "document_id": "repo-1",
        "source_chunk_index": 4,
        "source_span": {
            "locator": "chars:80-140",
            "source_kind": "code",
            "span_start": 80,
            "span_end": 140,
            "excerpt": "def train_step(model, batch):",
        },
    }
    chunk = {
        "id": "repo-1|abcd1234|4",
        "document_id": "repo-1",
        "order_index": 4,
        "text": "def train_step(model, batch):\n    return model(batch)",
        "start_char": 80,
        "end_char": 140,
    }
    provenance_entry = {
        "chunk_id": "repo-1|abcd1234|4",
        "source_kind": "code",
        "order_index": 4,
        "doc_id": "repo-1",
        "file_path": "src/train.py",
        "line_range": [12, 20],
    }

    links = resolve_provenance_links(
        candidate, chunk=chunk, document=None, provenance_entry=provenance_entry
    )

    assert len(links) == 1
    link = links[0]
    assert link.preview_kind == "code"
    assert link.highlight_rects is None
    assert link.page_dimensions is None
    assert link.file_path == "src/train.py"
    assert link.line_range.start_line == 12
    assert link.line_range.end_line == 20


def test_code_candidate_without_provenance_entry_still_reports_code_kind_with_no_metadata():
    candidate = {
        "document_id": "repo-1",
        "source_chunk_index": 4,
        "source_span": {"locator": "chars:80-140", "source_kind": "code"},
    }

    links = resolve_provenance_links(candidate, chunk=None, document=None, provenance_entry=None)

    assert links[0].preview_kind == "code"
    assert links[0].file_path is None
    assert links[0].line_range is None


def test_legacy_candidate_with_no_chunk_lookup_degrades_to_none():
    candidate = {
        "document_id": "doc-legacy",
        "source_chunk_index": 1,
        "source_span": {"locator": "chunk-001", "source_kind": "paper"},
    }

    links = resolve_provenance_links(candidate)

    assert len(links) == 1
    assert links[0].preview_kind == "none"
    assert links[0].highlight_rects is None
    assert links[0].page_dimensions is None
    assert links[0].clean_excerpt is None
    assert links[0].chunk_id == "doc-legacy|1"


def test_chunk_read_failure_fallback_still_returns_legacy_fields():
    candidate = {
        "document_id": "doc-legacy",
        "source_chunk_index": 1,
        "source_span": {
            "locator": "chunk-001",
            "source_kind": "paper",
            "page_number": 3,
            "excerpt": "raw excerpt",
        },
    }

    links = resolve_provenance_links(candidate, chunk=None, document=None, provenance_entry=None)

    assert len(links) == 1
    link = links[0]
    assert link.document_id == "doc-legacy"
    assert link.page_number == 3
    assert link.excerpt == "raw excerpt"
    assert link.preview_kind == "none"
