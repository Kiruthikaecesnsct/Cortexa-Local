from harvesting.application.dtos.harvesting_result_response import (
    HighlightRectView,
    LineRangeView,
    PageDimensionView,
    ProvenanceLinkView,
)

_FROZEN_PROVENANCE_LINK_FIELDS = {
    "document_id",
    "locator",
    "source_kind",
    "hit_url",
    "chunk_id",
    "source_chunk_index",
    "page_number",
    "section_hint",
    "span_start",
    "span_end",
    "excerpt",
    "preview_kind",
    "page_dimensions",
    "highlight_rects",
    "clean_excerpt",
    "file_path",
    "line_range",
}

_FROZEN_PAGE_DIMENSION_FIELDS = {"page_number", "width", "height"}
_FROZEN_HIGHLIGHT_RECT_FIELDS = {"page_number", "x0", "x1", "top", "bottom"}
_FROZEN_LINE_RANGE_FIELDS = {"start_line", "end_line"}


def test_provenance_link_view_field_names_match_frozen_wire_contract():
    assert set(ProvenanceLinkView.model_fields.keys()) == _FROZEN_PROVENANCE_LINK_FIELDS


def test_page_dimension_view_field_names_match_frozen_wire_contract():
    assert set(PageDimensionView.model_fields.keys()) == _FROZEN_PAGE_DIMENSION_FIELDS


def test_highlight_rect_view_field_names_match_frozen_wire_contract():
    assert set(HighlightRectView.model_fields.keys()) == _FROZEN_HIGHLIGHT_RECT_FIELDS


def test_line_range_view_field_names_match_frozen_wire_contract():
    assert set(LineRangeView.model_fields.keys()) == _FROZEN_LINE_RANGE_FIELDS


def test_provenance_link_view_preview_kind_literal_values():
    link = ProvenanceLinkView(document_id="doc-1")
    assert link.preview_kind == "none"

    for value in ("pdf", "code", "none"):
        assert ProvenanceLinkView(document_id="doc-1", preview_kind=value).preview_kind == value


def test_provenance_link_view_serializes_nested_field_names_on_wire():
    link = ProvenanceLinkView(
        document_id="doc-1",
        preview_kind="pdf",
        page_dimensions=[PageDimensionView(page_number=1, width=612.0, height=792.0)],
        highlight_rects=[HighlightRectView(page_number=1, x0=1.0, x1=2.0, top=3.0, bottom=4.0)],
    )

    dumped = link.model_dump(mode="json")

    assert set(dumped["page_dimensions"][0].keys()) == _FROZEN_PAGE_DIMENSION_FIELDS
    assert set(dumped["highlight_rects"][0].keys()) == _FROZEN_HIGHLIGHT_RECT_FIELDS


def test_provenance_link_view_serializes_line_range_field_names_on_wire():
    link = ProvenanceLinkView(
        document_id="repo-1",
        preview_kind="code",
        file_path="src/train.py",
        line_range=LineRangeView(start_line=12, end_line=20),
    )

    dumped = link.model_dump(mode="json")

    assert set(dumped["line_range"].keys()) == _FROZEN_LINE_RANGE_FIELDS


def test_provenance_link_view_legacy_payload_still_validates():
    legacy_payload = {
        "document_id": "doc-legacy",
        "locator": "chunk-001",
        "source_kind": "extraction",
        "chunk_id": "doc-legacy|1",
        "source_chunk_index": 1,
        "page_number": 3,
        "section_hint": None,
        "span_start": None,
        "span_end": None,
        "excerpt": "raw excerpt",
    }

    link = ProvenanceLinkView.model_validate(legacy_payload)

    assert link.preview_kind == "none"
    assert link.page_dimensions is None
    assert link.highlight_rects is None
    assert link.clean_excerpt is None
    assert link.file_path is None
    assert link.line_range is None
