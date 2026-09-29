import json

import pytest

from seeding.application.dtos.generate_lattice_request import GenerateLatticeRequest
from seeding.application.handlers.generate_lattice_handler import (
    GenerateLatticeDeps,
    handle,
)
from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.errors.seeding_errors import (
    InsufficientLatticeError,
    LatticeParseError,
    UngroundedSeedingError,
)
from seeding.domain.models.axis_score import AxisScore
from seeding.domain.models.evidence_bundle import EvidenceHit
from seeding.domain.models.idf_section import IdfSection
from seeding.domain.ports.model_router_port import ModelResult
from seeding.infrastructure.config.settings import SeedingSettings

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


def _make_request(hits: list[EvidenceHit] | None = None) -> GenerateLatticeRequest:
    return GenerateLatticeRequest(
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


def _entry_dict(title: str = "entry") -> dict:
    return {
        "title": title,
        "description": "description",
        "scope": "scope",
        "filing_strategy_note": "note",
        "evidence_refs": [_EVIDENCE_REF],
    }


def _lattice_payload(level_count: int = 2) -> dict:
    return {
        "core": _entry_dict("core"),
        "continuations": [_entry_dict() for _ in range(level_count)],
        "platform": [_entry_dict() for _ in range(level_count)],
        "system": [_entry_dict() for _ in range(level_count)],
    }


def _make_model_result(level_count: int = 2) -> ModelResult:
    return ModelResult(content=json.dumps(_lattice_payload(level_count)), citations=[])


class _FakePublisher:
    def __init__(self) -> None:
        self.calls: list[tuple[str, dict]] = []

    async def publish(
        self,
        topic: str,
        payload: dict,
        session_id: str | None = None,
        correlation_id: str | None = None,
    ) -> None:
        self.calls.append((topic, payload))


class _FakeSeedingRepository:
    def __init__(self) -> None:
        self.saved_results: list = []

    async def save(self, result) -> None:
        self.saved_results.append(result)


def _make_deps(client) -> tuple[GenerateLatticeDeps, _FakePublisher]:
    publisher = _FakePublisher()
    settings = SeedingSettings(model_router_url="http://model-router.internal.test")
    repository = _FakeSeedingRepository()
    deps = GenerateLatticeDeps(
        client=client, publisher=publisher, settings=settings, seeding_repository=repository
    )
    return deps, publisher


@pytest.mark.asyncio
async def test_handle_returns_all_four_levels(mocker):
    client = mocker.AsyncMock()
    client.complete.return_value = _make_model_result()
    deps, _ = _make_deps(client)

    response = await handle(deps, _make_request())

    assert response.core.title == "core"
    assert len(response.continuations) == 2
    assert len(response.platform) == 2
    assert len(response.system) == 2


@pytest.mark.asyncio
async def test_handle_publishes_engine_completed_event(mocker):
    client = mocker.AsyncMock()
    client.complete.return_value = _make_model_result()
    deps, publisher = _make_deps(client)

    response = await handle(deps, _make_request())

    assert len(publisher.calls) == 1
    topic, event = publisher.calls[0]
    assert topic == deps.settings.engine_completed_topic
    assert event["event_type"] == "engine.completed"
    assert event["schema_version"] == "1.0"
    assert event["document_id"] == "doc-1"
    inner = event["payload"]
    assert inner["document_id"] == "doc-1"
    assert inner["engine"] == "seeding"
    assert inner["report_id"] == response.report_id


@pytest.mark.asyncio
async def test_handle_returns_non_empty_diagram_with_level_markers(mocker):
    client = mocker.AsyncMock()
    client.complete.return_value = _make_model_result()
    deps, _ = _make_deps(client)

    response = await handle(deps, _make_request())

    assert response.diagram_text
    assert "CORE" in response.diagram_text
    assert "CONTINUATIONS" in response.diagram_text
    assert "PLATFORM" in response.diagram_text
    assert "SYSTEM" in response.diagram_text


@pytest.mark.asyncio
async def test_handle_returns_report_id(mocker):
    client = mocker.AsyncMock()
    client.complete.return_value = _make_model_result()
    deps, _ = _make_deps(client)

    response = await handle(deps, _make_request())

    assert response.report_id


@pytest.mark.asyncio
async def test_handle_raises_ungrounded_when_bundle_has_no_hits(mocker):
    client = mocker.AsyncMock()
    client.complete.return_value = _make_model_result()
    deps, publisher = _make_deps(client)

    with pytest.raises(UngroundedSeedingError):
        await handle(deps, _make_request(hits=[]))

    client.complete.assert_not_called()
    assert publisher.calls == []


@pytest.mark.asyncio
async def test_handle_raises_insufficient_lattice_when_levels_under_count(mocker):
    client = mocker.AsyncMock()
    client.complete.return_value = _make_model_result(level_count=1)
    deps, publisher = _make_deps(client)

    with pytest.raises(InsufficientLatticeError):
        await handle(deps, _make_request())

    assert publisher.calls == []


@pytest.mark.asyncio
async def test_handle_raises_parse_error_for_unknown_evidence_ref(mocker):
    client = mocker.AsyncMock()
    payload = _lattice_payload()
    payload["core"]["evidence_refs"] = ["[unknown:0-50]"]
    client.complete.return_value = ModelResult(content=json.dumps(payload), citations=[])
    deps, publisher = _make_deps(client)

    with pytest.raises(LatticeParseError):
        await handle(deps, _make_request())

    assert publisher.calls == []
