"""Gated, opt-in live eval for US114 extraction source discrimination.

This test sends each curated fixture in ``tests/fixtures/discrimination_chunks.py``
through the real extraction prompt builder, calls the live model-router
``/complete`` endpoint, and parses the response with the real ``CandidateParser``.
It asserts the resulting candidate count falls within each fixture's expected
band and reports the aggregate candidate count so it can be compared by hand
against the pre-US114 inclusive-prompt baseline (acceptance criterion: >= 30%
reduction in total candidates across the fixture set).

It is SKIPPED unless the operator explicitly opts in by setting
EXTRACTION_LLM_EVAL=1, so CI and the default test run never touch the network.

Run it locally against a reachable model-router:

    EXTRACTION_LLM_EVAL=1 \
    MODEL_ROUTER_URL=http://localhost:8080 \
    uv run pytest -m llm tests/test_extraction_discrimination_eval.py
"""

import os

import httpx
import pytest

from extraction.application.parsers.candidate_parser import CandidateParser
from extraction.application.prompts.extraction_prompt_builder import ExtractionPromptBuilder
from extraction.domain.enums.model_mode import ModelMode
from extraction.domain.models.chunk_input import ChunkInput
from extraction.domain.value_objects.provenance_span import ProvenanceSpan
from extraction.infrastructure.model_router.model_router_client import ModelRouterClient
from tests.fixtures.discrimination_chunks import DISCRIMINATION_CHUNKS, DiscriminationChunk

_OPT_IN_FLAG = "EXTRACTION_LLM_EVAL"
_MODEL_ROUTER_URL_ENV = "MODEL_ROUTER_URL"
_DEFAULT_MODEL_ROUTER_URL = "http://localhost:8080"
_LIVE_CALL_TIMEOUT_SECONDS = 90.0
_MAX_OUTPUT_TOKENS = 8192
_DOCUMENT_ID = "us114-discrimination-eval"
_BATCH_ID = "us114-discrimination-eval-batch"
_CODE_CHUNK_MARKER = "-code-"

pytestmark = [
    pytest.mark.llm,
    pytest.mark.skipif(
        os.getenv(_OPT_IN_FLAG) != "1",
        reason=f"live model-router eval is opt-in: set {_OPT_IN_FLAG}=1 to run it",
    ),
]


def _source_kind_for(fixture: DiscriminationChunk) -> str:
    if _CODE_CHUNK_MARKER in fixture.chunk_id:
        return "code"
    return "paper"


def _chunk_for(fixture: DiscriminationChunk) -> ChunkInput:
    span = ProvenanceSpan(source_kind=_source_kind_for(fixture), locator=fixture.chunk_id)
    return ChunkInput(
        text=fixture.text,
        order_index=0,
        source_span=span,
        document_id=_DOCUMENT_ID,
    )


async def _run_fixture(
    fixture: DiscriminationChunk,
    builder: ExtractionPromptBuilder,
    client: ModelRouterClient,
    parser: CandidateParser,
) -> int:
    chunk = _chunk_for(fixture)
    prompt = builder.build(chunk, None)
    result = await client.complete(prompt, ModelMode.single_primary)
    candidates = parser.parse(result.content, chunk, _DOCUMENT_ID, _BATCH_ID)
    return len(candidates)


@pytest.fixture
def model_router_base_url() -> str:
    return os.getenv(_MODEL_ROUTER_URL_ENV, _DEFAULT_MODEL_ROUTER_URL)


@pytest.fixture
async def model_router_client(model_router_base_url: str):
    async with httpx.AsyncClient() as http_client:
        yield ModelRouterClient(
            http_client=http_client,
            base_url=model_router_base_url,
            timeout=_LIVE_CALL_TIMEOUT_SECONDS,
        )


@pytest.fixture
def prompt_builder() -> ExtractionPromptBuilder:
    return ExtractionPromptBuilder(max_output_tokens=_MAX_OUTPUT_TOKENS)


@pytest.fixture
def parser() -> CandidateParser:
    return CandidateParser()


class TestExtractionDiscriminationLiveEval:
    @pytest.mark.parametrize(
        "fixture", DISCRIMINATION_CHUNKS, ids=[c.chunk_id for c in DISCRIMINATION_CHUNKS]
    )
    async def test_fixture_candidate_count_within_expected_band(
        self,
        fixture: DiscriminationChunk,
        prompt_builder: ExtractionPromptBuilder,
        model_router_client: ModelRouterClient,
        parser: CandidateParser,
    ):
        count = await _run_fixture(fixture, prompt_builder, model_router_client, parser)
        assert fixture.expected_min <= count <= fixture.expected_max, (
            f"{fixture.chunk_id} ({fixture.chunk_type}): got {count} candidates, "
            f"expected [{fixture.expected_min}, {fixture.expected_max}]. {fixture.notes}"
        )

    async def test_aggregate_candidate_count_reported(
        self,
        prompt_builder: ExtractionPromptBuilder,
        model_router_client: ModelRouterClient,
        parser: CandidateParser,
    ):
        total = 0
        for fixture in DISCRIMINATION_CHUNKS:
            total += await _run_fixture(fixture, prompt_builder, model_router_client, parser)
        print(
            f"\nUS114 discrimination eval: aggregate candidate count across "
            f"{len(DISCRIMINATION_CHUNKS)} fixtures = {total}. Compare by hand against the "
            "pre-US114 inclusive-prompt baseline for the >= 30% reduction acceptance criterion."
        )
