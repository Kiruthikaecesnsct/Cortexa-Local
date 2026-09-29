import pytest

from extraction.application.parsers.candidate_parser import CandidateParser
from extraction.domain.models.chunk_input import ChunkInput
from extraction.domain.value_objects.provenance_span import ProvenanceSpan


@pytest.fixture
def parser() -> CandidateParser:
    return CandidateParser()


@pytest.fixture
def chunk_with_full_span() -> ChunkInput:
    span = ProvenanceSpan(
        source_kind="paper",
        locator="chars:100-500",
        section_hint="Introduction",
        span_start=100,
        span_end=500,
        page_number=3,
        excerpt="This is the excerpt text from the chunk",
    )
    return ChunkInput(text="Full chunk text", order_index=2, source_span=span, document_id="doc-1")


@pytest.fixture
def chunk_minimal() -> ChunkInput:
    span = ProvenanceSpan(source_kind="code", locator="chars:50-150")
    return ChunkInput(text="Minimal chunk", order_index=0, source_span=span, document_id="doc-2")


class TestResolveSpanLocatorAuthority:
    def test_deterministic_locator_always_from_chunk(
        self, parser: CandidateParser, chunk_with_full_span: ChunkInput
    ):
        raw = {"source_span": {"locator": "section 5.1"}}
        result = parser._resolve_span(raw, chunk_with_full_span)
        assert result.locator == "chars:100-500"

    def test_locator_never_from_llm_when_missing(
        self, parser: CandidateParser, chunk_minimal: ChunkInput
    ):
        raw = {"source_span": {}}
        result = parser._resolve_span(raw, chunk_minimal)
        assert result.locator == "chars:50-150"

    def test_locator_never_from_llm_empty_source_span(
        self, parser: CandidateParser, chunk_with_full_span: ChunkInput
    ):
        raw = {}
        result = parser._resolve_span(raw, chunk_with_full_span)
        assert result.locator == "chars:100-500"


class TestSectionHintPrecedence:
    def test_chunk_section_hint_takes_precedence(
        self, parser: CandidateParser, chunk_with_full_span: ChunkInput
    ):
        raw = {"source_span": {"section_hint": "Methods"}}
        result = parser._resolve_span(raw, chunk_with_full_span)
        assert result.section_hint == "Introduction"

    def test_llm_section_hint_used_when_chunk_has_none(
        self, parser: CandidateParser, chunk_minimal: ChunkInput
    ):
        raw = {"source_span": {"section_hint": "Results"}}
        result = parser._resolve_span(raw, chunk_minimal)
        assert result.section_hint == "Results"

    def test_section_hint_none_when_both_absent(
        self, parser: CandidateParser, chunk_minimal: ChunkInput
    ):
        raw = {"source_span": {}}
        result = parser._resolve_span(raw, chunk_minimal)
        assert result.section_hint is None


class TestProvenanceFieldCarryover:
    def test_span_start_carried_from_chunk(
        self, parser: CandidateParser, chunk_with_full_span: ChunkInput
    ):
        result = parser._resolve_span({}, chunk_with_full_span)
        assert result.span_start == 100

    def test_span_end_carried_from_chunk(
        self, parser: CandidateParser, chunk_with_full_span: ChunkInput
    ):
        result = parser._resolve_span({}, chunk_with_full_span)
        assert result.span_end == 500

    def test_page_number_carried_from_chunk(
        self, parser: CandidateParser, chunk_with_full_span: ChunkInput
    ):
        result = parser._resolve_span({}, chunk_with_full_span)
        assert result.page_number == 3

    def test_excerpt_carried_from_chunk(
        self, parser: CandidateParser, chunk_with_full_span: ChunkInput
    ):
        result = parser._resolve_span({}, chunk_with_full_span)
        assert result.excerpt == "This is the excerpt text from the chunk"

    def test_page_number_none_when_chunk_has_none(
        self, parser: CandidateParser, chunk_minimal: ChunkInput
    ):
        result = parser._resolve_span({}, chunk_minimal)
        assert result.page_number is None

    def test_all_fields_none_when_chunk_minimal(
        self, parser: CandidateParser, chunk_minimal: ChunkInput
    ):
        result = parser._resolve_span({}, chunk_minimal)
        assert result.section_hint is None
        assert result.span_start is None
        assert result.span_end is None
        assert result.page_number is None
        assert result.excerpt is None


class TestSourceKindHandling:
    def test_source_kind_from_llm_when_provided(
        self, parser: CandidateParser, chunk_with_full_span: ChunkInput
    ):
        raw = {"source_span": {"source_kind": "code"}}
        result = parser._resolve_span(raw, chunk_with_full_span)
        assert result.source_kind == "code"

    def test_source_kind_falls_back_to_chunk(
        self, parser: CandidateParser, chunk_with_full_span: ChunkInput
    ):
        raw = {}
        result = parser._resolve_span(raw, chunk_with_full_span)
        assert result.source_kind == "paper"
