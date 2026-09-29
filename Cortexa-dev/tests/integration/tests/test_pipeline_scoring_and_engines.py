"""
Integration tests: scoring verdicts → dual-engine results → optional Cosmos cross-check.

Covers AC 5, 6.

All tests share the session-scoped `live_batch_id` fixture which submits once,
starts the saga, and waits for a terminal batch state before any test body runs.
Cosmos cross-checks skip cleanly when CORTEXA_COSMOS_CONNECTION_STRING is unset.
"""

import pytest

from settings import IntegrationSettings
from utils.cosmos_check import read_cosmos_container
from utils.gateway_client import GatewayClient
from utils.verifiers import (
    assert_both_engines_present,
    assert_harvesting_shape,
    assert_seeding_shape,
    assert_verdict_has_citations,
    resolve_candidate_id,
)


async def test_results_contain_both_harvesting_and_seeding(
    live_batch_id: str,
    gateway: GatewayClient,
) -> None:
    """AC 6 — /results returns top-level harvesting and seeding objects,
    each with the correct structural shape for a dual-engine run."""
    results = await gateway.get_results(live_batch_id)

    assert_both_engines_present(results)
    assert_harvesting_shape(results["harvesting"])
    assert_seeding_shape(results["seeding"])


async def test_harvesting_verdicts_present_in_results(
    live_batch_id: str,
    gateway: GatewayClient,
) -> None:
    """AC 6 — The harvesting section contains a verdicts list (may be empty if
    the test document yields no inventions, which is acceptable but flagged)."""
    results = await gateway.get_results(live_batch_id)
    harvesting = results.get("harvesting", {})

    assert isinstance(harvesting.get("verdicts"), list), "harvesting.verdicts must be a list"


async def test_seeding_opportunities_present_in_results(
    live_batch_id: str,
    gateway: GatewayClient,
) -> None:
    """AC 6 — The seeding section contains an opportunities list."""
    results = await gateway.get_results(live_batch_id)
    seeding = results.get("seeding", {})

    assert isinstance(seeding.get("opportunities"), list), "seeding.opportunities must be a list"


async def test_scoring_verdict_has_axis_scores_with_citations(
    live_batch_id: str,
    gateway: GatewayClient,
) -> None:
    """AC 5 — A scored verdict's detail record contains axis_scores, each with citations."""
    results = await gateway.get_results(live_batch_id)
    verdicts = results.get("harvesting", {}).get("verdicts", [])

    if not verdicts:
        pytest.skip(
            "no harvesting verdicts produced — the test document may not contain "
            "patentable inventions detectable by the pipeline"
        )

    candidate_id = resolve_candidate_id(verdicts[0], "verdict")

    detail = await gateway.get_result_detail(live_batch_id, candidate_id)
    assert_verdict_has_citations(detail)


async def test_scoring_verdict_has_overall_score(
    live_batch_id: str,
    gateway: GatewayClient,
) -> None:
    """AC 5 — A scored verdict's detail record includes a non-None overall_score."""
    results = await gateway.get_results(live_batch_id)
    verdicts = results.get("harvesting", {}).get("verdicts", [])

    if not verdicts:
        pytest.skip(
            "no harvesting verdicts produced — the test document may not contain "
            "patentable inventions detectable by the pipeline"
        )

    candidate_id = resolve_candidate_id(verdicts[0], "verdict")

    detail = await gateway.get_result_detail(live_batch_id, candidate_id)
    assert detail.get("overall_score") is not None, (
        "candidate detail must include a non-None overall_score"
    )


async def test_cosmos_evidence_bundles_cross_check(
    live_batch_id: str,
    settings: IntegrationSettings,
) -> None:
    """Optional — Cosmos direct read confirms evidence_bundles were written for this batch.
    Skips when CORTEXA_COSMOS_CONNECTION_STRING is unset or azure-cosmos is not installed."""
    if not settings.cosmos_connection_string:
        pytest.skip("CORTEXA_COSMOS_CONNECTION_STRING not configured")

    items = await read_cosmos_container(
        batch_id=live_batch_id,
        container_name="evidence_bundles",
        connection_string=settings.cosmos_connection_string,
    )
    if items is None:
        pytest.skip("Cosmos client unavailable — azure-cosmos package not installed")

    assert len(items) > 0, (
        f"expected at least one evidence_bundle document for batch_id={live_batch_id!r}"
    )


async def test_cosmos_verdicts_cross_check(
    live_batch_id: str,
    settings: IntegrationSettings,
) -> None:
    """Optional — Cosmos direct read confirms verdicts were written for this batch.
    Skips when CORTEXA_COSMOS_CONNECTION_STRING is unset or azure-cosmos is not installed."""
    if not settings.cosmos_connection_string:
        pytest.skip("CORTEXA_COSMOS_CONNECTION_STRING not configured")

    items = await read_cosmos_container(
        batch_id=live_batch_id,
        container_name="verdicts",
        connection_string=settings.cosmos_connection_string,
    )
    if items is None:
        pytest.skip("Cosmos client unavailable — azure-cosmos package not installed")

    assert len(items) > 0, f"expected at least one verdict document for batch_id={live_batch_id!r}"
