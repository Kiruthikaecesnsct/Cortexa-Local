from scoring.domain.models.evidence_bundle import EvidenceBundle


def test_new_fields_deserialize_from_phase1():
    raw = {
        "id": "bundle-1",
        "batch_id": "batch-1",
        "job_id": "job-1",
        "candidate_id": "cand-1",
        "document_id": "doc-1",
        "hits": [],
        "confidence_band": "High",
        "sources_used": ["PatentApi", "SeedCorpus", "LlmResearch"],
        "source_flags": {
            "PatentApi": True,
            "SeedCorpus": True,
            "LlmResearch": True,
        },
        "merged_at": "2026-01-01T00:00:00Z",
        "active_source_count": 3,
        "meets_minimum_sources": True,
        "minimum_active_sources": 3,
    }
    bundle = EvidenceBundle.model_validate(raw)
    assert bundle.active_source_count == 3
    assert bundle.meets_minimum_sources is True
    assert bundle.minimum_active_sources == 3


def test_legacy_bundle_without_grounding_fields_still_deserializes():
    raw = {
        "id": "bundle-legacy",
        "batch_id": "batch-1",
        "job_id": "job-1",
        "candidate_id": "cand-1",
        "document_id": "doc-1",
        "hits": [],
        "confidence_band": "Medium",
        "sources_used": ["PatentApi"],
        "source_flags": {
            "PatentApi": True,
            "SeedCorpus": False,
            "LlmResearch": False,
        },
        "merged_at": "2026-01-01T00:00:00Z",
    }
    bundle = EvidenceBundle.model_validate(raw)
    assert bundle.active_source_count == 0
    assert bundle.meets_minimum_sources is False
    assert bundle.minimum_active_sources == 0


def test_partial_grounding_signal():
    raw = {
        "id": "bundle-partial",
        "batch_id": "batch-1",
        "job_id": "job-1",
        "candidate_id": "cand-1",
        "document_id": "doc-1",
        "hits": [],
        "confidence_band": "Medium",
        "sources_used": ["SeedCorpus"],
        "source_flags": {
            "PatentApi": False,
            "SeedCorpus": True,
            "LlmResearch": False,
        },
        "merged_at": "2026-01-01T00:00:00Z",
        "active_source_count": 1,
        "meets_minimum_sources": False,
        "minimum_active_sources": 3,
    }
    bundle = EvidenceBundle.model_validate(raw)
    assert bundle.active_source_count == 1
    assert bundle.meets_minimum_sources is False
    assert bundle.minimum_active_sources == 3
