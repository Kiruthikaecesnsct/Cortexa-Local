import json

import pytest

from seeding.application.dtos.generate_claim_seeds_request import GenerateClaimSeedsRequest
from seeding.application.handlers.generate_claim_seeds_handler import (
    GenerateClaimSeedsDeps,
    handle,
)
from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.errors.seeding_errors import (
    InsufficientClaimSeedsError,
    UngroundedSeedingError,
)
from seeding.domain.models.axis_score import AxisScore
from seeding.domain.models.evidence_bundle import EvidenceHit
from seeding.domain.models.idf_section import IdfSection
from seeding.domain.ports.model_router_port import ModelResult

_SOURCE_ID = "src1"
_HIT_START = 0
_HIT_END = 100
_EVIDENCE_REF = f"[{_SOURCE_ID}:{_HIT_START}-{_HIT_END}]"


def _make_axes() -> dict[ScoringAxis, AxisScore]:
    return {axis: AxisScore(axis=axis, score=80, refs=[_EVIDENCE_REF]) for axis in ScoringAxis}


def _make_section(text: str = "section text") -> IdfSection:
    return IdfSection(text=text, citations=[_EVIDENCE_REF])


def _make_hits() -> list[EvidenceHit]:
    return [EvidenceHit(source_id=_SOURCE_ID, text="evidence text", start=_HIT_START, end=_HIT_END)]


def _make_request(hits: list[EvidenceHit] | None = None) -> GenerateClaimSeedsRequest:
    return GenerateClaimSeedsRequest(
        candidate_id="cand-1",
        batch_id="batch-1",
        job_id="job-1",
        document_id="doc-1",
        axes=_make_axes(),
        idf_abstract=_make_section("abstract text"),
        idf_background=_make_section("background text"),
        idf_summary=_make_section("summary text"),
        idf_core_differentiating_feature=_make_section("core feature text"),
        bundle_id="bundle-1",
        hits=hits if hits is not None else _make_hits(),
        source_flags=[],
    )


def _limitation_dict(refs: list[str] | None = None) -> dict:
    return {"text": "a limitation", "evidence_refs": refs if refs is not None else [_EVIDENCE_REF]}


def _independent_claim_dict() -> dict:
    return {
        "claim_type": "method",
        "preamble": "A method comprising",
        "recitations": ["recitation one"],
        "limitations": [_limitation_dict()],
    }


def _dependent_claim_dict(parent_index: int = 0) -> dict:
    return {
        "parent_index": parent_index,
        "added_limitations": [_limitation_dict()],
    }


def _make_model_result(dependent_count: int = 2) -> ModelResult:
    content = json.dumps(
        {
            "independent_claims": [_independent_claim_dict()],
            "dependent_claims": [_dependent_claim_dict() for _ in range(dependent_count)],
        }
    )
    return ModelResult(content=content, citations=[])


@pytest.mark.asyncio
async def test_handle_returns_independent_and_dependent_claims(mocker):
    client = mocker.AsyncMock()
    client.complete.return_value = _make_model_result()

    deps = GenerateClaimSeedsDeps(client=client)
    response = await handle(deps, _make_request())

    assert len(response.independent_claims) >= 1
    assert len(response.dependent_claims) >= 2


@pytest.mark.asyncio
async def test_handle_wires_request_fields_into_response(mocker):
    client = mocker.AsyncMock()
    client.complete.return_value = _make_model_result()

    deps = GenerateClaimSeedsDeps(client=client)
    response = await handle(deps, _make_request())

    assert response.candidate_id == "cand-1"
    assert response.batch_id == "batch-1"
    assert response.document_id == "doc-1"


@pytest.mark.asyncio
async def test_handle_returns_limitations_grounded_in_evidence(mocker):
    client = mocker.AsyncMock()
    client.complete.return_value = _make_model_result()

    deps = GenerateClaimSeedsDeps(client=client)
    response = await handle(deps, _make_request())

    independent_refs = response.independent_claims[0].limitations[0].evidence_refs
    dependent_refs = response.dependent_claims[0].added_limitations[0].evidence_refs
    assert independent_refs == [_EVIDENCE_REF]
    assert dependent_refs == [_EVIDENCE_REF]


@pytest.mark.asyncio
async def test_handle_raises_ungrounded_when_bundle_has_no_hits(mocker):
    client = mocker.AsyncMock()
    client.complete.return_value = _make_model_result()

    deps = GenerateClaimSeedsDeps(client=client)

    with pytest.raises(UngroundedSeedingError):
        await handle(deps, _make_request(hits=[]))
    client.complete.assert_not_called()


@pytest.mark.asyncio
async def test_handle_raises_insufficient_when_too_few_dependent_claims(mocker):
    client = mocker.AsyncMock()
    client.complete.return_value = _make_model_result(dependent_count=1)

    deps = GenerateClaimSeedsDeps(client=client)

    with pytest.raises(InsufficientClaimSeedsError):
        await handle(deps, _make_request())
