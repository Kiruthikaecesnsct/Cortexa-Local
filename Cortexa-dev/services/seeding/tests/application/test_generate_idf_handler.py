import pytest

from seeding.application.dtos.generate_idf_request import GenerateIdfRequest
from seeding.application.handlers.generate_idf_handler import GenerateIdfDeps, handle
from seeding.domain.enums.opportunity_category import OpportunityCategory
from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.models.axis_score import AxisScore
from seeding.domain.models.evidence_bundle import EvidenceHit
from seeding.domain.models.opportunity import Opportunity
from seeding.domain.ports.model_router_port import ModelResult


def _make_axes() -> dict[ScoringAxis, AxisScore]:
    return {axis: AxisScore(axis=axis, score=80, refs=["[src1:0-10]"]) for axis in ScoringAxis}


def _make_opportunities() -> dict[OpportunityCategory, list[Opportunity]]:
    opportunities = {category: [] for category in OpportunityCategory}
    opportunities[OpportunityCategory.Whitespace] = [
        Opportunity(
            category=OpportunityCategory.Whitespace,
            description="d",
            justification="j",
            citations=["[src1:0-10]"],
        )
    ]
    return opportunities


def _make_request() -> GenerateIdfRequest:
    return GenerateIdfRequest(
        candidate_id="cand-1",
        batch_id="batch-1",
        job_id="job-1",
        document_id="doc-1",
        axes=_make_axes(),
        opportunities=_make_opportunities(),
        bundle_id="bundle-1",
        hits=[EvidenceHit(source_id="src1", text="text", start=0, end=100)],
        source_flags=[],
    )


def _make_model_result(ref: str) -> ModelResult:
    section = '{"text": "section text", "citations": ["' + ref + '"]}'
    content = (
        '{"abstract": ' + section + ", "
        '"background": ' + section + ", "
        '"summary": ' + section + ", "
        '"core_differentiating_feature": ' + section + "}"
    )
    return ModelResult(content=content, citations=[])


@pytest.mark.asyncio
async def test_handle_wires_request_to_client(mocker):
    ref = "[src1:0-100]"
    client = mocker.AsyncMock()
    client.complete.return_value = _make_model_result(ref)

    deps = GenerateIdfDeps(client=client)
    response = await handle(deps, _make_request())

    client.complete.assert_called_once()
    assert response.candidate_id == "cand-1"
    assert response.batch_id == "batch-1"
    assert response.document_id == "doc-1"


@pytest.mark.asyncio
async def test_handle_returns_all_sections_with_citations(mocker):
    ref = "[src1:0-100]"
    client = mocker.AsyncMock()
    client.complete.return_value = _make_model_result(ref)

    deps = GenerateIdfDeps(client=client)
    response = await handle(deps, _make_request())

    assert response.abstract.citations == [ref]
    assert response.background.citations == [ref]
    assert response.summary.citations == [ref]
    assert response.core_differentiating_feature.citations == [ref]
