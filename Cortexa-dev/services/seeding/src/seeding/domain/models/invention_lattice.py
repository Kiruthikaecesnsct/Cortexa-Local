from pydantic import BaseModel


class LatticeEntry(BaseModel):
    title: str
    description: str
    scope: str
    filing_strategy_note: str
    evidence_refs: list[str]


class InventionLattice(BaseModel):
    document_id: str
    candidate_id: str
    batch_id: str
    core: LatticeEntry
    continuations: list[LatticeEntry]
    platform: list[LatticeEntry]
    system: list[LatticeEntry]
