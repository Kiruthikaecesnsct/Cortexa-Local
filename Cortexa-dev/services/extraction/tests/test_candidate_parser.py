import pytest

from extraction.application.parsers.candidate_parser import CandidateParser
from extraction.domain.errors.extraction_errors import CandidateParseError
from extraction.domain.models.chunk_input import ChunkInput
from extraction.domain.value_objects.provenance_span import ProvenanceSpan
from tests.fixtures.discrimination_chunks import DISCRIMINATION_CHUNKS, DiscriminationChunk

DOCUMENT_ID = "doc-001"
BATCH_ID = "batch-abc"

VALID_CANDIDATE = {
    "claim_text": "A method of sparse tensor encoding.",
    "problem": "Existing encoders waste memory.",
    "mechanism": "Prune zero-valued elements before serialisation.",
    "tech_field": "Data compression",
    "ipc_cpc_guess": "G06F 17/16",
}

VALID_PAYLOAD = {"candidates": [VALID_CANDIDATE]}


@pytest.fixture
def parser() -> CandidateParser:
    return CandidateParser()


@pytest.fixture
def chunk() -> ChunkInput:
    span = ProvenanceSpan(source_kind="paper", locator="section 2.1")
    return ChunkInput(text="Some text.", order_index=3, source_span=span, document_id=DOCUMENT_ID)


def _json(obj: object) -> str:
    import json

    return json.dumps(obj)


class TestParseValidPayload:
    def test_returns_one_candidate(self, parser: CandidateParser, chunk: ChunkInput):
        result = parser.parse(_json(VALID_PAYLOAD), chunk, DOCUMENT_ID, BATCH_ID)
        assert len(result) == 1

    def test_candidate_fields_mapped(self, parser: CandidateParser, chunk: ChunkInput):
        result = parser.parse(_json(VALID_PAYLOAD), chunk, DOCUMENT_ID, BATCH_ID)
        c = result[0]
        assert c.claim_text == VALID_CANDIDATE["claim_text"]
        assert c.problem == VALID_CANDIDATE["problem"]
        assert c.mechanism == VALID_CANDIDATE["mechanism"]
        assert c.tech_field == VALID_CANDIDATE["tech_field"]
        assert c.ipc_cpc_guess == VALID_CANDIDATE["ipc_cpc_guess"]

    def test_source_chunk_index_from_chunk(self, parser: CandidateParser, chunk: ChunkInput):
        result = parser.parse(_json(VALID_PAYLOAD), chunk, DOCUMENT_ID, BATCH_ID)
        assert result[0].source_chunk_index == chunk.order_index

    def test_document_id_and_batch_id_propagated(self, parser: CandidateParser, chunk: ChunkInput):
        result = parser.parse(_json(VALID_PAYLOAD), chunk, DOCUMENT_ID, BATCH_ID)
        assert result[0].document_id == DOCUMENT_ID
        assert result[0].batch_id == BATCH_ID

    def test_id_is_unique_per_candidate(self, parser: CandidateParser, chunk: ChunkInput):
        payload = {"candidates": [VALID_CANDIDATE, VALID_CANDIDATE]}
        result = parser.parse(_json(payload), chunk, DOCUMENT_ID, BATCH_ID)
        assert result[0].id != result[1].id

    def test_created_at_is_timezone_aware(self, parser: CandidateParser, chunk: ChunkInput):
        result = parser.parse(_json(VALID_PAYLOAD), chunk, DOCUMENT_ID, BATCH_ID)
        assert result[0].created_at.tzinfo is not None

    def test_span_falls_back_to_chunk_span_when_missing(
        self, parser: CandidateParser, chunk: ChunkInput
    ):
        result = parser.parse(_json(VALID_PAYLOAD), chunk, DOCUMENT_ID, BATCH_ID)
        assert result[0].source_span == chunk.source_span

    def test_source_kind_from_llm_when_provided(self, parser: CandidateParser, chunk: ChunkInput):
        candidate = {
            **VALID_CANDIDATE,
            "source_span": {"source_kind": "code"},
        }
        result = parser.parse(_json({"candidates": [candidate]}), chunk, DOCUMENT_ID, BATCH_ID)
        assert result[0].source_span.source_kind == "code"
        assert result[0].source_span.locator == chunk.source_span.locator

    def test_ipc_cpc_guess_is_none_when_absent(self, parser: CandidateParser, chunk: ChunkInput):
        candidate = {k: v for k, v in VALID_CANDIDATE.items() if k != "ipc_cpc_guess"}
        result = parser.parse(_json({"candidates": [candidate]}), chunk, DOCUMENT_ID, BATCH_ID)
        assert result[0].ipc_cpc_guess is None

    def test_empty_candidates_array_returns_empty_list(
        self, parser: CandidateParser, chunk: ChunkInput
    ):
        result = parser.parse(_json({"candidates": []}), chunk, DOCUMENT_ID, BATCH_ID)
        assert result == []


class TestFenceStripping:
    def test_parse_json_fenced_content(self, parser: CandidateParser, chunk: ChunkInput):
        content = f"```json\n{_json(VALID_PAYLOAD)}\n```"
        result = parser.parse(content, chunk, DOCUMENT_ID, BATCH_ID)
        assert len(result) == 1
        assert result[0].claim_text == VALID_CANDIDATE["claim_text"]

    def test_parse_plain_fenced_content(self, parser: CandidateParser, chunk: ChunkInput):
        content = f"```\n{_json(VALID_PAYLOAD)}\n```"
        result = parser.parse(content, chunk, DOCUMENT_ID, BATCH_ID)
        assert len(result) == 1
        assert result[0].claim_text == VALID_CANDIDATE["claim_text"]

    def test_parse_prose_preamble(self, parser: CandidateParser, chunk: ChunkInput):
        content = f"Here are the candidates:\n{_json(VALID_PAYLOAD)}"
        result = parser.parse(content, chunk, DOCUMENT_ID, BATCH_ID)
        assert len(result) == 1
        assert result[0].claim_text == VALID_CANDIDATE["claim_text"]

    def test_parse_truncated_raises(self, parser: CandidateParser, chunk: ChunkInput):
        with pytest.raises(CandidateParseError, match="not valid JSON"):
            parser.parse('{"candidates": [{"claim_text": "foo"', chunk, DOCUMENT_ID, BATCH_ID)

    def test_parse_empty_candidates(self, parser: CandidateParser, chunk: ChunkInput):
        result = parser.parse('{"candidates": []}', chunk, DOCUMENT_ID, BATCH_ID)
        assert result == []


class TestParseErrors:
    def test_raises_on_invalid_json(self, parser: CandidateParser, chunk: ChunkInput):
        with pytest.raises(CandidateParseError, match="not valid JSON"):
            parser.parse("not json at all", chunk, DOCUMENT_ID, BATCH_ID)

    def test_raises_when_root_is_array(self, parser: CandidateParser, chunk: ChunkInput):
        with pytest.raises(CandidateParseError, match="Expected JSON object"):
            parser.parse(_json([VALID_CANDIDATE]), chunk, DOCUMENT_ID, BATCH_ID)

    def test_raises_when_candidates_key_missing(self, parser: CandidateParser, chunk: ChunkInput):
        with pytest.raises(CandidateParseError, match="missing 'candidates' key"):
            parser.parse(_json({"items": []}), chunk, DOCUMENT_ID, BATCH_ID)

    def test_raises_when_candidates_not_array(self, parser: CandidateParser, chunk: ChunkInput):
        with pytest.raises(CandidateParseError, match="must be a JSON array"):
            parser.parse(_json({"candidates": "wrong"}), chunk, DOCUMENT_ID, BATCH_ID)

    def test_raises_on_missing_required_field(self, parser: CandidateParser, chunk: ChunkInput):
        incomplete = {k: v for k, v in VALID_CANDIDATE.items() if k != "claim_text"}
        with pytest.raises(CandidateParseError, match="Candidate field error"):
            parser.parse(_json({"candidates": [incomplete]}), chunk, DOCUMENT_ID, BATCH_ID)

    def test_raises_when_candidate_is_not_dict(self, parser: CandidateParser, chunk: ChunkInput):
        with pytest.raises(CandidateParseError, match="Candidate field error"):
            parser.parse(_json({"candidates": ["string", None, 42]}), chunk, DOCUMENT_ID, BATCH_ID)


class TestParseDiscriminationFixtures:
    """Validate that CandidateParser correctly parses the expected output shape for
    each curated discrimination fixture (US114 Phase 1).

    These assert parsing of the *expected* output distribution recorded on each
    fixture's ``representative_output`` — they do NOT exercise the LLM's own
    source-discrimination judgment. The gated live eval in
    ``test_extraction_discrimination_eval.py`` is what checks the real model.
    """

    @pytest.mark.parametrize(
        "fixture", DISCRIMINATION_CHUNKS, ids=[c.chunk_id for c in DISCRIMINATION_CHUNKS]
    )
    def test_parsed_count_within_expected_band(
        self, parser: CandidateParser, chunk: ChunkInput, fixture: DiscriminationChunk
    ):
        result = parser.parse(_json(fixture.representative_output), chunk, DOCUMENT_ID, BATCH_ID)
        assert fixture.expected_min <= len(result) <= fixture.expected_max

    @pytest.mark.parametrize(
        "fixture",
        [c for c in DISCRIMINATION_CHUNKS if c.chunk_type == "clear_contribution"],
        ids=[c.chunk_id for c in DISCRIMINATION_CHUNKS if c.chunk_type == "clear_contribution"],
    )
    def test_clear_contribution_parses_at_least_one(
        self, parser: CandidateParser, chunk: ChunkInput, fixture: DiscriminationChunk
    ):
        result = parser.parse(_json(fixture.representative_output), chunk, DOCUMENT_ID, BATCH_ID)
        assert len(result) >= 1

    @pytest.mark.parametrize(
        "fixture",
        [c for c in DISCRIMINATION_CHUNKS if c.chunk_type == "pure_background"],
        ids=[c.chunk_id for c in DISCRIMINATION_CHUNKS if c.chunk_type == "pure_background"],
    )
    def test_pure_background_parses_none(
        self, parser: CandidateParser, chunk: ChunkInput, fixture: DiscriminationChunk
    ):
        result = parser.parse(_json(fixture.representative_output), chunk, DOCUMENT_ID, BATCH_ID)
        assert result == []

    @pytest.mark.parametrize(
        "fixture",
        [c for c in DISCRIMINATION_CHUNKS if c.chunk_type == "applied_off_the_shelf"],
        ids=[c.chunk_id for c in DISCRIMINATION_CHUNKS if c.chunk_type == "applied_off_the_shelf"],
    )
    def test_applied_off_the_shelf_parses_none(
        self, parser: CandidateParser, chunk: ChunkInput, fixture: DiscriminationChunk
    ):
        result = parser.parse(_json(fixture.representative_output), chunk, DOCUMENT_ID, BATCH_ID)
        assert result == []
