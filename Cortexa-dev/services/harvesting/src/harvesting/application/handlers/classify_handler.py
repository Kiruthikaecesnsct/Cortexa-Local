from dataclasses import dataclass

from harvesting.application.dtos.classify_request import ClassifyRequestDto
from harvesting.application.dtos.classify_response import ClassifyResponseDto
from harvesting.domain.models.scored_candidate import ScoredCandidate
from harvesting.domain.repositories.harvesting_protocols import MaturityResultRepository
from harvesting.domain.services import maturity_classifier
from harvesting.infrastructure.config.settings import HarvestingSettings


@dataclass
class ClassifyDeps:
    repository: MaturityResultRepository
    settings: HarvestingSettings


class ClassifyHandler:
    def __init__(self, deps: ClassifyDeps) -> None:
        self._deps = deps

    async def handle(self, request: ClassifyRequestDto) -> ClassifyResponseDto:
        candidate = ScoredCandidate(
            candidate_id=request.candidate_id,
            batch_id=request.batch_id,
            job_id=request.job_id,
            document_id=request.document_id,
            axes=request.axes,
        )
        result = maturity_classifier.classify(
            candidate,
            self._deps.settings.maturity_novelty_threshold,
            self._deps.settings.maturity_feasibility_threshold,
        )
        await self._deps.repository.save(result)
        return ClassifyResponseDto(
            candidate_id=result.candidate_id,
            maturity=result.maturity,
            novelty_score=result.novelty_score,
            feasibility_score=result.feasibility_score,
            reasoning=result.reasoning,
        )
