from scoring.domain.events.scoring_failed import make_scoring_failed_event


def _make_event(candidate_id: str | None = "cand-1", correlation_id: str | None = None):
    return make_scoring_failed_event(
        batch_id="batch-1",
        document_id="doc-1",
        reason="missing_candidate_id",
        job_id="job-1",
        candidate_id=candidate_id,
        trigger_type="pipeline",
        correlation_id=correlation_id,
    )


def test_event_type_is_scoring_failed():
    envelope = _make_event()
    assert envelope.event_type == "scoring.failed"


def test_payload_contains_required_fields():
    envelope = _make_event()
    payload = envelope.payload
    assert payload["reason"] == "missing_candidate_id"
    assert payload["document_id"] == "doc-1"
    assert payload["job_id"] == "job-1"
    assert payload["candidate_id"] == "cand-1"
    assert payload["trigger_type"] == "pipeline"


def test_candidate_id_may_be_none():
    envelope = _make_event(candidate_id=None)
    assert envelope.payload["candidate_id"] is None


def test_correlation_id_passed_through():
    envelope = _make_event(correlation_id="corr-abc")
    assert envelope.correlation_id == "corr-abc"


def test_no_correlation_id_defaults_to_none():
    envelope = _make_event()
    assert envelope.correlation_id is None


def test_batch_id_and_document_id_set_on_envelope():
    envelope = _make_event()
    assert envelope.batch_id == "batch-1"
    assert envelope.document_id == "doc-1"
