from dataclasses import dataclass
from datetime import UTC, datetime

from harvesting.application.dtos.harvesting_result_response import (
    CandidateDto,
    HarvestingResultResponseDto,
    VerdictDto,
)
from harvesting.domain.enums.scoring_axis import ScoringAxis
from harvesting.domain.models.harvesting_report import HarvestingReport
from harvesting.domain.repositories.harvesting_protocols import ReportRepository
from harvesting.domain.services.recommendation import PURSUE_THRESHOLD, recommendation_for


@dataclass
class GetResultsDeps:
    report_repo: ReportRepository
    verdicts_repo: object


class GetResultsHandler:
    def __init__(self, deps: GetResultsDeps) -> None:
        self._deps = deps

    async def handle(self, batch_id: str) -> HarvestingResultResponseDto:
        report = await self._deps.report_repo.get_by_batch(batch_id)
        verdict_items = await self._deps.verdicts_repo.get_by_batch(batch_id)

        candidates = self._map_candidates(report, batch_id)
        verdicts = self._map_verdicts(verdict_items, batch_id)
        summary = self._build_summary(report, verdicts)

        return HarvestingResultResponseDto(
            id=report.id,
            batch_id=batch_id,
            candidates=candidates,
            verdicts=verdicts,
            summary=summary,
            created_at=report.generated_at.isoformat(),
        )

    def _map_candidates(self, report: HarvestingReport, batch_id: str) -> list[CandidateDto]:
        return [
            CandidateDto(
                id=c.candidate_id,
                title=c.title,
                abstract=c.description,
                description=c.description,
                claim_draft=self._extract_claim_draft(c),
                novelty_hypothesis=self._extract_novelty_hypothesis(c),
                source_asset_id=report.document_id,
                batch_id=batch_id,
                created_at=report.generated_at.isoformat(),
                candidate_id=c.candidate_id,
                maturity=c.maturity.value,
                rank=c.rank,
                weighted_score=c.weighted_score,
                axes={
                    axis.value: {"axis": axis.value, "score": score.score, "refs": score.refs}
                    for axis, score in c.axes.items()
                },
                agreement_flag=c.agreement_flag.value,
                citations=[citation.model_dump() for citation in c.citations],
                provenance_links=[link.model_dump() for link in c.provenance_links],
                source_availability=c.source_availability,
                source_status=c.source_status,
                evidence_sources=c.evidence_sources,
            )
            for c in report.candidates
        ]

    def _map_verdicts(self, verdict_items: list, batch_id: str) -> list[VerdictDto]:
        return [
            VerdictDto(
                id=v["id"],
                candidate_id=v["candidate_id"],
                patentability_score=v.get("composite_score", 0.0),
                rationale=self._build_rationale(v),
                recommendation=recommendation_for(v.get("composite_score", 0.0)),
                batch_id=batch_id,
                created_at=datetime.now(UTC).isoformat(),
            )
            for v in verdict_items
        ]

    def _extract_claim_draft(self, candidate: object) -> str:
        return candidate.claim_draft.strip()

    def _extract_novelty_hypothesis(self, candidate: object) -> str:
        if ScoringAxis.Novelty in candidate.axes:
            score = candidate.axes[ScoringAxis.Novelty].score
            if score >= PURSUE_THRESHOLD:
                return f"High novelty detected (score: {score}) - novel approach identified"
            return f"Moderate novelty (score: {score}) - incremental improvement"
        return f"Novel approach based on {candidate.maturity.value} maturity assessment"

    def _build_rationale(self, verdict: dict) -> str:
        axes = verdict.get("axes", {})
        if not axes:
            return "Assessed based on composite scoring"

        axis_scores = []
        for axis_key, axis_data in axes.items():
            if isinstance(axis_data, dict):
                score = axis_data.get("score", 0)
                axis_scores.append(f"{axis_key}: {score}")

        if axis_scores:
            return f"Composite score based on {', '.join(axis_scores)}"
        return "Assessed based on composite scoring"

    def _build_summary(self, report: HarvestingReport, verdicts: list[VerdictDto]) -> str:
        total = len(report.candidates)
        if total == 0:
            return "No candidates found"

        pursue = sum(1 for v in verdicts if v.recommendation == "pursue")
        investigate = sum(1 for v in verdicts if v.recommendation == "investigate")
        abandon = total - pursue - investigate

        return (
            f"Found {total} candidate(s): "
            f"{pursue} to pursue, {investigate} to investigate, {abandon} to abandon"
        )
