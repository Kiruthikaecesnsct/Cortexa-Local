from pydantic import BaseModel

from seeding.domain.models.idf_section import IdfSection


class IdfDraft(BaseModel):
    candidate_id: str
    batch_id: str
    job_id: str
    document_id: str
    abstract: IdfSection
    background: IdfSection
    summary: IdfSection
    core_differentiating_feature: IdfSection
