from pytest_mock import MockerFixture

from harvesting.application.dtos.classify_request import ClassifyRequestDto
from harvesting.application.handlers.classify_handler import ClassifyDeps, ClassifyHandler
from harvesting.domain.enums.maturity import Maturity
from harvesting.domain.enums.scoring_axis import ScoringAxis
from harvesting.domain.models.axis_score import AxisScore
from harvesting.infrastructure.config.settings import HarvestingSettings

NOVELTY_THRESHOLD = 7
FEASIBILITY_THRESHOLD = 6

CANDIDATE_ID = "cand-handler-001"
BATCH_ID = "batch-handler-abc"
JOB_ID = "job-handler-xyz"
DOCUMENT_ID = "doc-handler-001"


def _make_settings(
    novelty_threshold: int = NOVELTY_THRESHOLD,
    feasibility_threshold: int = FEASIBILITY_THRESHOLD,
) -> HarvestingSettings:
    return HarvestingSettings(
        cosmos_uri="https://test.documents.azure.com:443/",
        maturity_novelty_threshold=novelty_threshold,
        maturity_feasibility_threshold=feasibility_threshold,
        servicebus_namespace_fqdn="test.servicebus.windows.net",
    )


def _make_request(novelty_score: int = 80, feasibility_score: int = 70) -> ClassifyRequestDto:
    axes: dict[ScoringAxis, AxisScore] = {
        ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=novelty_score, refs=[]),
        ScoringAxis.Patentability: AxisScore(
            axis=ScoringAxis.Patentability, score=feasibility_score, refs=[]
        ),
        ScoringAxis.Inventiveness: AxisScore(axis=ScoringAxis.Inventiveness, score=50, refs=[]),
        ScoringAxis.Commercial: AxisScore(axis=ScoringAxis.Commercial, score=50, refs=[]),
        ScoringAxis.Strategic: AxisScore(axis=ScoringAxis.Strategic, score=50, refs=[]),
    }
    return ClassifyRequestDto(
        candidate_id=CANDIDATE_ID,
        batch_id=BATCH_ID,
        job_id=JOB_ID,
        document_id=DOCUMENT_ID,
        axes=axes,
    )


class TestClassifyHandler:
    async def test_handle_classifies_and_persists(self, mocker: MockerFixture) -> None:
        repository = mocker.AsyncMock()
        handler = ClassifyHandler(ClassifyDeps(repository=repository, settings=_make_settings()))
        request = _make_request(novelty_score=80, feasibility_score=70)

        response = await handler.handle(request)

        repository.save.assert_awaited_once()
        assert response.candidate_id == CANDIDATE_ID
        assert response.maturity == Maturity.Mature

    async def test_handle_uses_thresholds_from_settings(self, mocker: MockerFixture) -> None:
        repository = mocker.AsyncMock()
        settings = _make_settings(
            novelty_threshold=NOVELTY_THRESHOLD, feasibility_threshold=FEASIBILITY_THRESHOLD
        )
        handler = ClassifyHandler(ClassifyDeps(repository=repository, settings=settings))
        request = _make_request(
            novelty_score=NOVELTY_THRESHOLD, feasibility_score=FEASIBILITY_THRESHOLD
        )

        response = await handler.handle(request)

        assert response.maturity == Maturity.Mature
        assert f">={NOVELTY_THRESHOLD}" in response.reasoning
        assert f">={FEASIBILITY_THRESHOLD}" in response.reasoning

    async def test_handle_returns_dto_with_reasoning(self, mocker: MockerFixture) -> None:
        repository = mocker.AsyncMock()
        handler = ClassifyHandler(ClassifyDeps(repository=repository, settings=_make_settings()))
        request = _make_request(novelty_score=80, feasibility_score=70)

        response = await handler.handle(request)

        assert response.reasoning != ""
        assert len(response.reasoning) > 0
