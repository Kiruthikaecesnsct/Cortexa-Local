from seeding.application.handlers.generate_lattice_handler import (
    _lattice_to_opportunities,
)
from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.models.axis_score import AxisScore
from seeding.domain.models.invention_lattice import InventionLattice, LatticeEntry
from seeding.domain.models.scored_candidate import ScoredCandidate

EVIDENCE_REF = "[src1:0-100]"


def _make_candidate(novelty_score: int = 80, patentability_score: int = 90) -> ScoredCandidate:
    axes = {
        ScoringAxis.Novelty: AxisScore(
            axis=ScoringAxis.Novelty, score=novelty_score, refs=[EVIDENCE_REF]
        ),
        ScoringAxis.Patentability: AxisScore(
            axis=ScoringAxis.Patentability, score=patentability_score, refs=[EVIDENCE_REF]
        ),
    }
    return ScoredCandidate(
        candidate_id="cand-1",
        batch_id="batch-1",
        job_id="job-1",
        document_id="doc-1",
        axes=axes,
    )


def _make_lattice_entry(title: str, description: str) -> LatticeEntry:
    return LatticeEntry(
        title=title,
        description=description,
        scope="scope",
        filing_strategy_note="note",
        evidence_refs=[EVIDENCE_REF],
    )


def test_lattice_to_opportunities_all_levels_present():
    candidate = _make_candidate(novelty_score=80, patentability_score=90)
    lattice = InventionLattice(
        document_id="doc-1",
        candidate_id="cand-1",
        batch_id="batch-1",
        core=_make_lattice_entry("Core Invention", "Core description"),
        continuations=[
            _make_lattice_entry("Continuation 1", "Continuation 1 description"),
            _make_lattice_entry("Continuation 2", "Continuation 2 description"),
        ],
        platform=[_make_lattice_entry("Platform Extension", "Platform description")],
        system=[_make_lattice_entry("System Integration", "System description")],
    )

    opportunities = _lattice_to_opportunities(lattice, candidate)

    assert len(opportunities) == 5
    assert opportunities[0].id == "cand-1-core"
    assert opportunities[0].title == "Core Invention"
    assert opportunities[0].description == "Core description"
    assert opportunities[0].confidence_score == 85.0
    assert opportunities[0].roadmap_alignment == "Core"

    assert opportunities[1].id == "cand-1-continuation-0"
    assert opportunities[1].title == "Continuation 1"
    assert opportunities[1].roadmap_alignment == "Continuation"
    assert opportunities[1].confidence_score == 85.0

    assert opportunities[2].id == "cand-1-continuation-1"
    assert opportunities[2].title == "Continuation 2"
    assert opportunities[2].roadmap_alignment == "Continuation"
    assert opportunities[2].confidence_score == 85.0

    assert opportunities[3].id == "cand-1-platform-0"
    assert opportunities[3].title == "Platform Extension"
    assert opportunities[3].roadmap_alignment == "Platform"
    assert opportunities[3].confidence_score == 85.0

    assert opportunities[4].id == "cand-1-system-0"
    assert opportunities[4].title == "System Integration"
    assert opportunities[4].roadmap_alignment == "System"
    assert opportunities[4].confidence_score == 85.0


def test_lattice_to_opportunities_only_core_present():
    candidate = _make_candidate(novelty_score=75, patentability_score=80)
    lattice = InventionLattice(
        document_id="doc-1",
        candidate_id="cand-1",
        batch_id="batch-1",
        core=_make_lattice_entry("Core Only", "Core only description"),
        continuations=[],
        platform=[],
        system=[],
    )

    opportunities = _lattice_to_opportunities(lattice, candidate)

    assert len(opportunities) == 1
    assert opportunities[0].id == "cand-1-core"
    assert opportunities[0].title == "Core Only"
    assert opportunities[0].confidence_score == 77.5
    assert opportunities[0].roadmap_alignment == "Core"


def test_lattice_to_opportunities_confidence_calculated_from_axes():
    candidate = _make_candidate(novelty_score=60, patentability_score=70)
    lattice = InventionLattice(
        document_id="doc-1",
        candidate_id="cand-1",
        batch_id="batch-1",
        core=_make_lattice_entry("Test Core", "Test description"),
        continuations=[_make_lattice_entry("Test Continuation", "Test continuation description")],
        platform=[],
        system=[],
    )

    opportunities = _lattice_to_opportunities(lattice, candidate)

    assert all(opp.confidence_score == 65.0 for opp in opportunities)


def test_lattice_to_opportunities_multiple_entries_at_each_level():
    candidate = _make_candidate()
    lattice = InventionLattice(
        document_id="doc-1",
        candidate_id="cand-1",
        batch_id="batch-1",
        core=_make_lattice_entry("Core", "Core description"),
        continuations=[
            _make_lattice_entry(f"Continuation {i}", f"Continuation {i} description")
            for i in range(3)
        ],
        platform=[
            _make_lattice_entry(f"Platform {i}", f"Platform {i} description") for i in range(2)
        ],
        system=[_make_lattice_entry(f"System {i}", f"System {i} description") for i in range(4)],
    )

    opportunities = _lattice_to_opportunities(lattice, candidate)

    assert len(opportunities) == 10
    core_opps = [opp for opp in opportunities if opp.roadmap_alignment == "Core"]
    continuation_opps = [opp for opp in opportunities if opp.roadmap_alignment == "Continuation"]
    platform_opps = [opp for opp in opportunities if opp.roadmap_alignment == "Platform"]
    system_opps = [opp for opp in opportunities if opp.roadmap_alignment == "System"]

    assert len(core_opps) == 1
    assert len(continuation_opps) == 3
    assert len(platform_opps) == 2
    assert len(system_opps) == 4
