import httpx
import pytest
import respx

from extraction.application.dtos.extraction_prompt import ExtractionPrompt, PromptOptions
from extraction.domain.models.chunk_input import ChunkInput
from extraction.domain.value_objects.provenance_span import ProvenanceSpan

SINGLE_RESPONSE = {
    "Provider": "azure-openai",
    "Model": "gpt-4o",
    "Content": '{"candidates": []}',
    "Citations": None,
    "Usage": {"prompt_tokens": 100, "completion_tokens": 50, "total_tokens": 150},
    "Grounding": None,
    "Error": None,
}

DUAL_RESPONSE = {
    "Primary": SINGLE_RESPONSE,
    "Secondary": {**SINGLE_RESPONSE, "Provider": "anthropic", "Model": "claude-3-5-sonnet"},
}


@pytest.fixture
def sample_span() -> ProvenanceSpan:
    return ProvenanceSpan(source_kind="paper", locator="section 3.2")


@pytest.fixture
def sample_chunk(sample_span: ProvenanceSpan) -> ChunkInput:
    return ChunkInput(
        text="A novel method of encoding tensors using sparse quantization.",
        order_index=0,
        source_span=sample_span,
        document_id="doc-abc123",
    )


@pytest.fixture
def sample_prompt() -> ExtractionPrompt:
    return ExtractionPrompt(
        task_kind="extraction",
        prompt="Find patentable inventions.",
        evidence_refs=None,
        options=PromptOptions(),
    )


@pytest.fixture
def mock_complete():
    with respx.mock(base_url="http://model-router-test") as mock:
        mock.post("/complete").mock(return_value=httpx.Response(200, json=SINGLE_RESPONSE))
        yield mock


@pytest.fixture
def mock_complete_dual():
    with respx.mock(base_url="http://model-router-test") as mock:
        mock.post("/complete/dual").mock(return_value=httpx.Response(200, json=DUAL_RESPONSE))
        yield mock


@pytest.fixture
def mock_both():
    with respx.mock(base_url="http://model-router-test") as mock:
        mock.post("/complete").mock(return_value=httpx.Response(200, json=SINGLE_RESPONSE))
        mock.post("/complete/dual").mock(return_value=httpx.Response(200, json=DUAL_RESPONSE))
        yield mock
