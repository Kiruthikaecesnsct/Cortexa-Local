from scoring.domain.enums.agreement_level import AgreementLevel
from scoring.domain.enums.scoring_axis import ScoringAxis
from scoring.domain.models.axis_score import AxisScore
from scoring.domain.models.build_verdict_request import BuildVerdictRequest
from scoring.domain.models.stored_verdict import StoredVerdict

_AXES = {
    ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=80, refs=["ref1"]),
    ScoringAxis.Inventiveness: AxisScore(axis=ScoringAxis.Inventiveness, score=60, refs=[]),
    ScoringAxis.Commercial: AxisScore(axis=ScoringAxis.Commercial, score=70, refs=["ref2"]),
    ScoringAxis.Strategic: AxisScore(axis=ScoringAxis.Strategic, score=50, refs=[]),
    ScoringAxis.Patentability: AxisScore(axis=ScoringAxis.Patentability, score=90, refs=["ref3"]),
}


def _build_verdict(batch_id: str = "batch-1", candidate_id: str = "cand-1") -> StoredVerdict:
    return StoredVerdict.build(
        BuildVerdictRequest(
            batch_id=batch_id,
            job_id="job-1",
            candidate_id=candidate_id,
            document_id="doc-1",
            axes=_AXES,
            agreement_level=AgreementLevel.Full,
            agreeing_axis_count=5,
            grounding_meets_minimum=True,
            grounding_source_count=3,
        )
    )


def test_composite_score_is_mean_of_five_axes():
    verdict = _build_verdict()
    expected = (80 + 60 + 70 + 50 + 90) / 5
    assert verdict.composite_score == expected


def test_id_is_batch_colon_candidate():
    verdict = _build_verdict(batch_id="batch-abc", candidate_id="cand-xyz")
    assert verdict.id == "batch-abc:cand-xyz"


def test_grounding_fields_threaded_from_request():
    verdict = StoredVerdict.build(
        BuildVerdictRequest(
            batch_id="batch-1",
            job_id="job-1",
            candidate_id="cand-1",
            document_id="doc-1",
            axes=_AXES,
            agreement_level=AgreementLevel.Full,
            agreeing_axis_count=5,
            grounding_meets_minimum=False,
            grounding_source_count=1,
        )
    )
    assert verdict.grounding_meets_minimum is False
    assert verdict.grounding_source_count == 1


def test_grounding_fields_happy_path():
    verdict = StoredVerdict.build(
        BuildVerdictRequest(
            batch_id="batch-1",
            job_id="job-1",
            candidate_id="cand-1",
            document_id="doc-1",
            axes=_AXES,
            agreement_level=AgreementLevel.Full,
            agreeing_axis_count=5,
            grounding_meets_minimum=True,
            grounding_source_count=3,
        )
    )
    assert verdict.grounding_meets_minimum is True
    assert verdict.grounding_source_count == 3


def test_single_reason_defaults_to_none_when_not_provided():
    verdict = _build_verdict()
    assert verdict.single_reason is None


def test_single_reason_threaded_from_request():
    verdict = StoredVerdict.build(
        BuildVerdictRequest(
            batch_id="batch-1",
            job_id="job-1",
            candidate_id="cand-1",
            document_id="doc-1",
            axes=_AXES,
            agreement_level=AgreementLevel.SingleConfigured,
            agreeing_axis_count=0,
            grounding_meets_minimum=True,
            grounding_source_count=3,
            single_reason="dual_mode_disabled",
        )
    )
    assert verdict.agreement_level == AgreementLevel.SingleConfigured
    assert verdict.single_reason == "dual_mode_disabled"


def test_stored_verdict_deserializes_old_cosmos_doc_without_single_reason_field():
    legacy_payload = {
        "id": "batch-1:cand-1",
        "batch_id": "batch-1",
        "job_id": "job-1",
        "candidate_id": "cand-1",
        "document_id": "doc-1",
        "axes": {axis.value: score.model_dump() for axis, score in _AXES.items()},
        "composite_score": 70.0,
        "agreement_level": AgreementLevel.FallbackSingle.value,
        "agreeing_axis_count": 0,
        "grounding_meets_minimum": True,
        "grounding_source_count": 3,
    }
    verdict = StoredVerdict.model_validate(legacy_payload)
    assert verdict.single_reason is None
    assert verdict.agreement_level == AgreementLevel.FallbackSingle
