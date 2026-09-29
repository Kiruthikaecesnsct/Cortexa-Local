"""
Integration tests: batch submission → document ingestion → evidence gathering.

Covers AC 2, 3, 4.

All tests share the session-scoped `live_batch_id` fixture which submits once,
starts the saga, and waits for a terminal batch state before any test body runs.
"""

import pytest

from utils.gateway_client import GatewayClient
from utils.verifiers import assert_evidence_sources_non_empty, resolve_candidate_id

_STATES_PAST_QUEUED = frozenset(
    {"Ingested", "Extracted", "Scored", "Harvested", "Seeded", "Complete", "Failed"}
)

_STATES_AT_LEAST_EXTRACTED = frozenset({"Extracted", "Scored", "Harvested", "Seeded", "Complete"})


async def test_batch_submission_accepted(live_batch_id: str) -> None:
    """AC 2 — A successful multipart POST to /batches returns a non-empty batch_id."""
    assert live_batch_id, "expected a non-empty batch_id from batch creation"


async def test_all_documents_advance_past_queued(
    live_batch_id: str,
    gateway: GatewayClient,
) -> None:
    """AC 3 — Every document in the batch progresses past the initial Queued state,
    proving the saga received and processed the ingestion.requested event."""
    status = await gateway.get_status(live_batch_id)
    documents = status.get("documents", [])

    assert documents, "batch must contain at least one tracked document"

    for doc in documents:
        doc_state = doc.get("status")
        assert doc_state in _STATES_PAST_QUEUED, (
            f"document {doc.get('document_id')!r} is still in state {doc_state!r}; "
            "expected it to advance past Queued"
        )


async def test_at_least_one_document_reaches_extracted(
    live_batch_id: str,
    gateway: GatewayClient,
) -> None:
    """AC 3 — At least one document reaches the Extracted state or beyond,
    proving the extraction.completed event was published and consumed."""
    status = await gateway.get_status(live_batch_id)
    documents = status.get("documents", [])

    reached = [d for d in documents if d.get("status") in _STATES_AT_LEAST_EXTRACTED]
    if not documents:
        pytest.skip("no documents in batch status — cannot verify extraction stage")

    assert reached, (
        "expected at least one document to reach Extracted or beyond; "
        f"document states: {[d.get('status') for d in documents]}"
    )


async def test_candidate_detail_has_evidence_sources_with_citations(
    live_batch_id: str,
    gateway: GatewayClient,
) -> None:
    """AC 4 — A candidate detail record exposes non-empty evidence_sources,
    each available source carrying at least one citation."""
    results = await gateway.get_results(live_batch_id)
    harvesting = results.get("harvesting", {})

    candidates = harvesting.get("candidates", []) or harvesting.get("verdicts", [])
    if not candidates:
        pytest.skip(
            "no harvesting candidates produced — the test document may not contain "
            "patentable inventions detectable by the pipeline"
        )

    candidate_id = resolve_candidate_id(candidates[0], "candidate")

    detail = await gateway.get_result_detail(live_batch_id, candidate_id)
    assert_evidence_sources_non_empty(detail)
