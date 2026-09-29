from __future__ import annotations

from pydantic import BaseModel

from seeding.domain.models.idf_draft import IdfDraft
from seeding.domain.models.idf_section import IdfSection


class GenerateIdfResponse(BaseModel):
    candidate_id: str
    batch_id: str
    job_id: str
    document_id: str
    abstract: IdfSection
    background: IdfSection
    summary: IdfSection
    core_differentiating_feature: IdfSection

    @classmethod
    def from_draft(cls, draft: IdfDraft) -> GenerateIdfResponse:
        return cls(
            candidate_id=draft.candidate_id,
            batch_id=draft.batch_id,
            job_id=draft.job_id,
            document_id=draft.document_id,
            abstract=draft.abstract,
            background=draft.background,
            summary=draft.summary,
            core_differentiating_feature=draft.core_differentiating_feature,
        )
