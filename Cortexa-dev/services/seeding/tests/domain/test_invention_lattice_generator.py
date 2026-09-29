import pytest

from seeding.domain.enums.scoring_axis import ScoringAxis
from seeding.domain.errors.seeding_errors import (
    InsufficientLatticeError,
    UngroundedSeedingError,
)
from seeding.domain.models.axis_score import AxisScore
from seeding.domain.models.evidence_bundle import EvidenceBundle, EvidenceHit
from seeding.domain.models.idf_draft import IdfDraft
from seeding.domain.models.idf_section import IdfSection
from seeding.domain.models.invention_lattice import InventionLattice, LatticeEntry
from seeding.domain.models.scored_candidate import ScoredCandidate
from seeding.domain.services.invention_lattice_generator import (
    validate_inputs,
    validate_lattice,
)

_REF = "[src1:0-10]"


def _make_axes() -> dict[ScoringAxis, AxisScore]:
    return {axis: AxisScore(axis=axis, score=80, refs=[_REF]) for axis in ScoringAxis}


def _make_candidate(axes: dict[ScoringAxis, AxisScore] | None = None) -> ScoredCandidate:
    return ScoredCandidate(
        candidate_id="cand-1",
        batch_id="batch-1",
        job_id="job-1",
        document_id="doc-1",
        axes=axes if axes is not None else _make_axes(),
    )


def _make_bundle(hits: list[EvidenceHit] | None = None) -> EvidenceBundle:
    default_hits = [EvidenceHit(source_id="src1", text="evidence text", start=0, end=100)]
    return EvidenceBundle(
        id="bundle-1",
        batch_id="batch-1",
        job_id="job-1",
        candidate_id="cand-1",
        document_id="doc-1",
        hits=hits if hits is not None else default_hits,
        source_flags=[],
    )


def _make_section(text: str = "section text") -> IdfSection:
    return IdfSection(text=text, citations=[_REF])


def _make_idf_draft(
    abstract: str = "abstract text",
    summary: str = "summary text",
    core_feature: str = "core feature text",
) -> IdfDraft:
    return IdfDraft(
        candidate_id="cand-1",
        batch_id="batch-1",
        job_id="job-1",
        document_id="doc-1",
        abstract=_make_section(abstract),
        background=_make_section("background text"),
        summary=_make_section(summary),
        core_differentiating_feature=_make_section(core_feature),
    )


def _make_entry(title: str = "entry") -> LatticeEntry:
    return LatticeEntry(
        title=title,
        description="description",
        scope="scope",
        filing_strategy_note="note",
        evidence_refs=[_REF],
    )


def _make_lattice(
    continuations: list[LatticeEntry] | None = None,
    platform: list[LatticeEntry] | None = None,
    system: list[LatticeEntry] | None = None,
) -> InventionLattice:
    return InventionLattice(
        document_id="doc-1",
        candidate_id="cand-1",
        batch_id="batch-1",
        core=_make_entry("core"),
        continuations=continuations
        if continuations is not None
        else [_make_entry(), _make_entry()],
        platform=platform if platform is not None else [_make_entry(), _make_entry()],
        system=system if system is not None else [_make_entry(), _make_entry()],
    )


def test_empty_bundle_raises_ungrounded():
    with pytest.raises(UngroundedSeedingError):
        validate_inputs(_make_candidate(), _make_idf_draft(), _make_bundle(hits=[]))


def test_missing_axis_raises_ungrounded():
    axes = {ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=80, refs=[])}
    with pytest.raises(UngroundedSeedingError):
        validate_inputs(_make_candidate(axes=axes), _make_idf_draft(), _make_bundle())


def test_empty_abstract_raises_ungrounded():
    with pytest.raises(UngroundedSeedingError):
        validate_inputs(_make_candidate(), _make_idf_draft(abstract=""), _make_bundle())


def test_empty_summary_raises_ungrounded():
    with pytest.raises(UngroundedSeedingError):
        validate_inputs(_make_candidate(), _make_idf_draft(summary=""), _make_bundle())


def test_empty_core_differentiating_feature_raises_ungrounded():
    with pytest.raises(UngroundedSeedingError):
        validate_inputs(_make_candidate(), _make_idf_draft(core_feature=""), _make_bundle())


def test_valid_inputs_does_not_raise():
    validate_inputs(_make_candidate(), _make_idf_draft(), _make_bundle())


def test_all_four_levels_present_does_not_raise():
    validate_lattice(_make_lattice())


def test_continuations_under_count_raises_insufficient():
    with pytest.raises(InsufficientLatticeError):
        validate_lattice(_make_lattice(continuations=[_make_entry()]))


def test_platform_under_count_raises_insufficient():
    with pytest.raises(InsufficientLatticeError):
        validate_lattice(_make_lattice(platform=[_make_entry()]))


def test_system_under_count_raises_insufficient():
    with pytest.raises(InsufficientLatticeError):
        validate_lattice(_make_lattice(system=[_make_entry()]))


def test_empty_continuations_raises_insufficient():
    with pytest.raises(InsufficientLatticeError):
        validate_lattice(_make_lattice(continuations=[]))
