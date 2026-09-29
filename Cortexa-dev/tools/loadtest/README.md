# Cortexa Load Test Harness

100-document load test for the Cortexa pipeline. Submits synthetic documents, waits for all batches to complete, and reports throughput against capacity plan thresholds.

## Prerequisites

- Azure CLI authenticated (`az login`)
- GitHub CLI authenticated (`gh auth login`)
- Azure Key Vault access to `cortexa-dev-kv` for admin credentials
- Python 3.14 via pyenv virtualenv `Cortexa_env_314`
- Gateway URL defaults to dev: `https://cortexa-dev-api-gateway.proudsmoke-86efe866.eastus.azurecontainerapps.io`

## Configuration

Defaults are set in `src/settings.py`. Override via environment variables with `CORTEXA_` prefix:

- `CORTEXA_GATEWAY_BASE_URL` (default: dev gateway)
- `CORTEXA_KEY_VAULT_NAME` (default: `cortexa-dev-kv`)
- `CORTEXA_DOCUMENT_COUNT` (default: 100)
- `CORTEXA_DOCUMENTS_PER_BATCH` (default: 10)
- `CORTEXA_POLL_TIMEOUT_S` (default: 6000, ~100 minutes)
- `CORTEXA_POLL_INTERVAL_S` (default: 20)

## Installation

From the repo root:

```bash
pyenv activate Cortexa_env_314
cd tools/loadtest
uv sync
```

## Dry Run (No Azure Submission)

Validates the harness, generates the dataset, but does not submit batches.

```bash
pyenv activate Cortexa_env_314
cd tools/loadtest
uv run python -m src --dry-run
```

Expected output: dataset generation logs, report with zero wall-clock time, no Azure calls.

## Live Run

Submits 100 documents across 10 batches (10 docs each) to the dev gateway and polls to completion.

```bash
pyenv activate Cortexa_env_314
cd tools/loadtest
uv run python -m src
```

Run duration: up to 100 minutes (90-minute pass threshold + buffer).

## Pass/Fail Thresholds

From `.notes/informations/Architectures/Cortexa_CapacityPlan.md`:

- Wall-clock time: ≤90 minutes for 100 documents (threshold: 5400s)
- Azure cost: ≤$3.00 in dev (estimated ~$2-3)
- Zero failed batches
- Zero partially failed batches
- All batches reach terminal state (Completed, Failed, PartiallyFailed, or Timeout)

The harness checks wall-clock and batch terminal states. Cost must be verified manually via Azure Cost Analysis after the run.

## Report

On completion, the harness writes a timestamped JSON report (`loadtest_report_YYYYMMDD_HHMMSS.json`) and prints a human-readable summary. Report includes:

- Total wall-clock time
- Throughput (docs/min)
- Batch terminal states (completed, failed, partial)
- Pass/fail verdict with reasons

## Cost Warning

Each 100-document run incurs ~$2-3 in Azure dev charges (Container Apps compute + Foundry GPT inference + Cosmos + Service Bus + Blob Storage). Do not run repeatedly without monitoring cost.

## DO NOT RUN AGAINST PRODUCTION

This harness defaults to the dev environment. Do not point it at production. The gateway URL is explicitly configured to dev, and the Key Vault name defaults to `cortexa-dev-kv`.

## Troubleshooting

- Authentication failure: check `az login` and Key Vault RBAC (requires Secrets User role on `cortexa-dev-kv`).
- Timeout: 100-minute poll timeout is generous. If batches time out, check Azure Container Apps logs for evidence/scoring/extraction stalls.
- Gateway 401/403: verify admin credentials exist in Key Vault (`identity-admin-email`, `identity-admin-initial-password`).
- Python import errors: ensure `pyenv activate Cortexa_env_314` and `uv sync` completed successfully.

## Expected Throughput

From the capacity plan:

- Foundry-bound ceiling: ~1.9 docs/min sustained (Foundry gpt-5.5 MaxInFlight=24)
- Wall-clock for 100 docs: 60-75 minutes ideal, 90 minutes with cold-start/scale-up overhead

If throughput is significantly lower, investigate:

- Model-router 429/503 counts (retry exhaustion)
- KEDA scale-up delay (replicas stuck at zero)
- Evidence deep-research timeout rate
- Dead-letter queue growth (Service Bus subscriptions)
