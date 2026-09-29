from datetime import UTC, datetime

from pydantic import BaseModel, Field, field_validator


class CorpusMatch(BaseModel):
    id: str = ""
    score: float = 0.0
    chunk_id: str = ""
    document_id: str = ""
    section_label: str = ""
    text_excerpt: str = ""


class LiveMatch(BaseModel):
    reference: str = ""
    title: str = ""
    applicant: str = ""
    date: str = ""
    url: str = ""
    relevance_score: float = 0.0
    source: str = ""


class ConceptLandscape(BaseModel):
    concept: str = ""
    chunk_ids: list[str] = Field(default_factory=list)
    density: str = "sparse"
    corpus_axis: str = "sparse"
    live_axis: str = "sparse"
    max_sim: float = 0.0
    relevant_corpus_hits: int = 0
    live_hit_count: int = 0
    corpus_matches: list[CorpusMatch] = Field(default_factory=list)
    live_matches: list[LiveMatch] = Field(default_factory=list)

    @field_validator("chunk_ids", mode="before")
    @classmethod
    def _coerce_chunk_ids(cls, v: object) -> list[str]:
        if v is None:
            return []
        if isinstance(v, (str, bytes)):
            return [str(v)]
        return [str(item) for item in v]


class WhitespaceIntersection(BaseModel):
    kind: str
    text: str = ""
    chunk_ids: list[str] = Field(default_factory=list)
    nearest_similarity: float = 0.0
    nearest_reference: str = ""

    @field_validator("chunk_ids", mode="before")
    @classmethod
    def _coerce_chunk_ids(cls, v: object) -> list[str]:
        if v is None:
            return []
        if isinstance(v, (str, bytes)):
            return [str(v)]
        return [str(item) for item in v]


class LandscapeSourceFlags(BaseModel):
    evidence_reachable: bool = False
    corpus_only: bool = True
    live_sources_ok: list[str] = Field(default_factory=list)
    degraded_sources: list[str] = Field(default_factory=list)


class LandscapeProvenance(BaseModel):
    landscape_id: str
    schema_version: str
    source_flags: LandscapeSourceFlags = Field(default_factory=LandscapeSourceFlags)


class PriorArtLandscape(BaseModel):
    id: str
    batch_id: str
    document_id: str
    type: str = "prior_art_landscape"
    schema_version: str
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    concepts: list[ConceptLandscape] = Field(default_factory=list)
    whitespace: list[WhitespaceIntersection] = Field(default_factory=list)
    source_flags: LandscapeSourceFlags = Field(default_factory=LandscapeSourceFlags)
