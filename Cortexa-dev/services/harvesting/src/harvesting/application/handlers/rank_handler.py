from dataclasses import dataclass

from harvesting.application.dtos.rank_request import RankRequestDto
from harvesting.application.dtos.rank_response import RankResponseDto
from harvesting.domain.models.rank_weights import RankWeights
from harvesting.domain.models.scored_candidate import ScoredCandidate
from harvesting.domain.services import harvesting_ranker


@dataclass
class RankDeps:
    weights: RankWeights


class RankHandler:
    def __init__(self, deps: RankDeps) -> None:
        self._deps = deps

    def handle(self, request: RankRequestDto) -> RankResponseDto:
        candidates = [
            ScoredCandidate(
                candidate_id=c.candidate_id,
                batch_id=request.batch_id,
                job_id=request.job_id,
                document_id=c.document_id,
                axes=c.axes,
            )
            for c in request.candidates
        ]
        ranked = harvesting_ranker.rank(candidates, self._deps.weights)
        return RankResponseDto(batch_id=request.batch_id, ranked=ranked)
