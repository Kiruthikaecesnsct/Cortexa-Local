import asyncio
import logging
import time
from pathlib import Path

import httpx

from auth import fetch_access_token
from dataset import generate_synthetic_dataset
from gateway_client import BatchFile, CreateBatchOptions, GatewayClient
from metrics import LoadTestMetrics, collect_metrics, print_summary, save_report
from poller import PollConfig, wait_for_batch_complete
from settings import LoadTestSettings

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
)
logger = logging.getLogger(__name__)


def _chunk_dict[K, V](d: dict[K, V], chunk_size: int) -> list[dict[K, V]]:
    items = list(d.items())
    return [dict(items[i : i + chunk_size]) for i in range(0, len(items), chunk_size)]


async def _submit_batch(
    client: GatewayClient,
    batch_name: str,
    files: dict[str, bytes],
    settings: LoadTestSettings,
) -> str:
    batch_files = [BatchFile(name=name, content=content) for name, content in files.items()]
    options = CreateBatchOptions(
        batch_name=batch_name,
        engine="dual",
        ai_model=settings.default_ai_model,
        seed_corpus_domain=settings.seed_corpus_domain,
        files=batch_files,
    )
    logger.info(f"Submitting batch {batch_name} with {len(batch_files)} documents")
    batch_id = await client.create_and_start_batch(options)
    logger.info(f"Batch {batch_name} submitted with ID {batch_id}")
    return batch_id


async def _poll_batch_to_completion(
    client: GatewayClient,
    batch_id: str,
    poll_config: PollConfig,
) -> dict:
    logger.info(f"Polling batch {batch_id} for completion")
    try:
        final_status = await wait_for_batch_complete(
            fetch_status=lambda: client.get_status(batch_id),
            config=poll_config,
        )
        state = final_status.get("status", "Unknown")
        logger.info(f"Batch {batch_id} reached terminal state: {state}")
        return final_status
    except TimeoutError:
        logger.error(f"Batch {batch_id} timed out after {poll_config.timeout}s")
        return {"status": "Timeout", "batch_id": batch_id}


async def _submit_all_batches(
    client: GatewayClient,
    dataset: dict[str, bytes],
    settings: LoadTestSettings,
) -> list[str]:
    batches = _chunk_dict(dataset, settings.documents_per_batch)
    logger.info(
        f"Split {settings.document_count} documents into {len(batches)} batches "
        f"of {settings.documents_per_batch} each"
    )

    batch_ids = []
    for i, batch_chunk in enumerate(batches, start=1):
        batch_id = await _submit_batch(client, f"loadtest-batch-{i}", batch_chunk, settings)
        batch_ids.append(batch_id)

    logger.info(f"All {len(batch_ids)} batches submitted, polling for completion")
    return batch_ids


async def _poll_all_batches(
    client: GatewayClient,
    batch_ids: list[str],
    poll_config: PollConfig,
) -> dict[str, str]:
    poll_tasks = [_poll_batch_to_completion(client, bid, poll_config) for bid in batch_ids]
    final_statuses = await asyncio.gather(*poll_tasks, return_exceptions=True)

    batch_states: dict[str, str] = {}
    for batch_id, status in zip(batch_ids, final_statuses, strict=True):
        if isinstance(status, Exception):
            logger.error(f"Batch {batch_id} raised exception: {status}")
            batch_states[batch_id] = "Exception"
        else:
            batch_states[batch_id] = status.get("status", "Unknown")
    return batch_states


async def _run_live(
    settings: LoadTestSettings, dataset: dict[str, bytes]
) -> tuple[dict[str, str], float]:
    logger.info("Fetching access token")
    access_token = await fetch_access_token(settings)

    base_url = str(settings.gateway_base_url).rstrip("/")
    async with httpx.AsyncClient(
        base_url=base_url,
        headers={"Authorization": f"Bearer {access_token}"},
        timeout=httpx.Timeout(120.0),
    ) as http_client:
        client = GatewayClient(http_client)
        poll_config = PollConfig(
            timeout=float(settings.poll_timeout_s),
            interval=float(settings.poll_interval_s),
        )

        start_time = time.perf_counter()
        batch_ids = await _submit_all_batches(client, dataset, settings)
        batch_states = await _poll_all_batches(client, batch_ids, poll_config)
        wall_clock_s = time.perf_counter() - start_time

    return batch_states, wall_clock_s


async def run_load_test(settings: LoadTestSettings, dry_run: bool = False) -> LoadTestMetrics:
    logger.info(f"Generating {settings.document_count} synthetic documents")
    dataset = generate_synthetic_dataset(settings.document_count)
    logger.info(f"Dataset generated: {len(dataset)} documents")

    if dry_run:
        logger.info("Dry-run mode: skipping submission and polling")
        batch_states: dict[str, str] = {}
        wall_clock_s = 0.0
    else:
        batch_states, wall_clock_s = await _run_live(settings, dataset)

    return collect_metrics(settings, wall_clock_s, batch_states)


def run_harness(dry_run: bool = False) -> None:
    settings = LoadTestSettings()
    logger.info("Starting load test harness")
    logger.info(f"Configuration: {settings.document_count} documents, dry_run={dry_run}")

    if not dry_run:
        logger.warning("COST WARNING: This load test will incur Azure charges (~$2-3 in dev).")
        logger.warning("DO NOT RUN AGAINST PRODUCTION.")

    metrics = asyncio.run(run_load_test(settings, dry_run=dry_run))

    timestamp = time.strftime("%Y%m%d_%H%M%S")
    report_path = Path(f"loadtest_report_{timestamp}.json")
    save_report(metrics, report_path)
    logger.info(f"Report saved to {report_path}")

    print_summary(metrics)
