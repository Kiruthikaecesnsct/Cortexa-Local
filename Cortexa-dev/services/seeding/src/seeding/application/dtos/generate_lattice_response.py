from __future__ import annotations

from pydantic import BaseModel

from seeding.domain.models.invention_lattice import InventionLattice, LatticeEntry


class GenerateLatticeResponse(BaseModel):
    candidate_id: str
    batch_id: str
    document_id: str
    core: LatticeEntry
    continuations: list[LatticeEntry]
    platform: list[LatticeEntry]
    system: list[LatticeEntry]
    diagram_text: str
    report_id: str

    @classmethod
    def from_lattice(
        cls, lattice: InventionLattice, diagram_text: str, report_id: str
    ) -> GenerateLatticeResponse:
        return cls(
            candidate_id=lattice.candidate_id,
            batch_id=lattice.batch_id,
            document_id=lattice.document_id,
            core=lattice.core,
            continuations=lattice.continuations,
            platform=lattice.platform,
            system=lattice.system,
            diagram_text=diagram_text,
            report_id=report_id,
        )
