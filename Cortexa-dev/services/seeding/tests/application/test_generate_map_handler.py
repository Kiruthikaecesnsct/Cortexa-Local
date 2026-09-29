import pytest

from seeding.application.dtos.generate_map_request import GenerateMapRequest
from seeding.application.handlers.generate_map_handler import GenerateMapDeps, handle
from seeding.domain.enums.opportunity_category import OpportunityCategory
from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.models.axis_score import AxisScore
from seeding.domain.models.evidence_bundle import EvidenceHit
from seeding.domain.ports.model_router_port import ModelResult


def _make_axes() -> dict[ScoringAxis, AxisScore]:
    return {axis: AxisScore(axis=axis, score=80, refs=["[src1:0-10]"]) for axis in ScoringAxis}


def _make_request() -> GenerateMapRequest:
    return GenerateMapRequest(
        candidate_id="cand-1",
        batch_id="batch-1",
        job_id="job-1",
        document_id="doc-1",
        axes=_make_axes(),
        bundle_id="bundle-1",
        hits=[EvidenceHit(source_id="src1", text="text", start=0, end=100)],
        source_flags=[],
    )


def _make_model_result(ref: str) -> ModelResult:
    opp = {
        "description": "desc",
        "justification": "just",
        "citations": [ref],
    }
    content = (
        '{"Whitespace": [' + str(opp).replace("'", '"') + "], "
        '"Defensive": [' + str(opp).replace("'", '"') + "], "
        '"Adjacent": [' + str(opp).replace("'", '"') + "], "
        '"Continuation": [' + str(opp).replace("'", '"') + "]}"
    )
    return ModelResult(content=content, citations=[])


@pytest.mark.asyncio
async def test_handle_wires_request_to_client(mocker):
    ref = "[src1:0-100]"
    client = mocker.AsyncMock()
    client.complete.return_value = _make_model_result(ref)

    deps = GenerateMapDeps(client=client)
    request = _make_request()

    response = await handle(deps, request)

    client.complete.assert_called_once()
    assert response.candidate_id == "cand-1"
    assert response.batch_id == "batch-1"
    assert response.document_id == "doc-1"
    assert set(response.opportunities.keys()) == set(OpportunityCategory)


@pytest.mark.asyncio
async def test_handle_returns_opportunities_with_citations(mocker):
    ref = "[src1:0-100]"
    client = mocker.AsyncMock()
    client.complete.return_value = _make_model_result(ref)

    deps = GenerateMapDeps(client=client)
    response = await handle(deps, _make_request())

    for opps in response.opportunities.values():
        assert len(opps) >= 1
