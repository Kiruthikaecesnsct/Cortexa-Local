from scoring.domain.enums.agreement_level import AgreementLevel
from scoring.domain.enums.scoring_axis import ScoringAxis
from scoring.domain.events.scoring_completed import make_scoring_completed_event
from scoring.domain.models.axis_score import AxisScore
from scoring.domain.models.build_verdict_request import BuildVerdictRequest
from scoring.domain.models.stored_verdict import StoredVerdict

_AXES = {
    ScoringAxis.Novelty: AxisScore(axis=ScoringAxis.Novelty, score=80, refs=[]),
    ScoringAxis.Inventiveness: AxisScore(axis=ScoringAxis.Inventiveness, score=60, refs=[]),
    ScoringAxis.Commercial: AxisScore(axis=ScoringAxis.Commercial, score=70, refs=[]),
    ScoringAxis.Strategic: AxisScore(axis=ScoringAxis.Strategic, score=50, refs=[]),
    ScoringAxis.Patentability: AxisScore(axis=ScoringAxis.Patentability, score=90, refs=[]),
}


def _make_verdict() -> StoredVerdict:
    return StoredVerdict.build(
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


def test_payload_contains_required_fields():
    verdict = _make_verdict()
    envelope = make_scoring_completed_event(verdict)
    payload = envelope.payload
    assert payload["document_id"] == verdict.document_id
    assert payload["candidate_id"] == verdict.candidate_id
    assert payload["verdict_id"] == verdict.id
    assert payload["composite_score"] == verdict.composite_score
    assert payload["agreement_level"] == verdict.agreement_level
    assert payload["single_reason"] == verdict.single_reason
    assert envelope.event_type == "scoring.completed"


def test_payload_contains_agreement_level_and_single_reason_for_single_configured():
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
    envelope = make_scoring_completed_event(verdict)
    payload = envelope.payload
    assert payload["agreement_level"] == AgreementLevel.SingleConfigured
    assert payload["single_reason"] == "dual_mode_disabled"


def test_payload_single_reason_is_none_when_dual_agreement_reached():
    verdict = _make_verdict()
    envelope = make_scoring_completed_event(verdict)
    assert envelope.payload["single_reason"] is None


def test_correlation_id_passed_through():
    verdict = _make_verdict()
    envelope = make_scoring_completed_event(verdict, correlation_id="corr-abc")
    assert envelope.correlation_id == "corr-abc"


def test_no_correlation_id_defaults_to_none():
    verdict = _make_verdict()
    envelope = make_scoring_completed_event(verdict)
    assert envelope.correlation_id is None


def test_payload_contains_grounding_fields():
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
    envelope = make_scoring_completed_event(verdict)
    payload = envelope.payload
    assert payload["grounding_meets_minimum"] is False
    assert payload["grounding_source_count"] == 1
