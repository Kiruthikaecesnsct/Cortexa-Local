# Cortexa Integration Tests

Cross-service integration suite that exercises the live Azure dev pipeline through
the public api-gateway. The entire suite is driven by one real pipeline run.

---

## Prerequisites

- Python 3.14 (managed by `pyenv activate Cortexa_env_314`)
- Azure CLI authenticated as an identity that can read from `cortexa-dev-kv`
  (the dev container's `cortexa-terraform` service principal has Key Vault Secrets
  Officer and satisfies this requirement)
- Network access to `cortexa-dev-api-gateway.proudsmoke-86efe866.eastus.azurecontainerapps.io`

---

## Setup

```bash
cd tests/integration

# Copy and optionally edit the env file
cp .env.example .env

# Install dependencies
uv sync
```

To enable optional direct-Cosmos validation, set `CORTEXA_COSMOS_CONNECTION_STRING`
in `.env`. Retrieve the value from Key Vault:

```bash
az keyvault secret show \
  --vault-name cortexa-dev-kv \
  --name cosmos-primary-connection-string \
  --query value -o tsv
```

Then install the optional extra:

```bash
uv sync --extra cosmos
```

---

## Running

```bash
# Full suite (runs the pipeline once, all tests share the result)
uv run pytest

# Verbose output with live log
uv run pytest -v --log-cli-level=INFO

# Single test
uv run pytest tests/test_foo.py::test_bar

# Format check
uv run ruff format --check .

# Lint
uv run ruff check .
```

---

## Targeting a different environment

Override `CORTEXA_GATEWAY_BASE_URL` in `.env` or as an environment variable:

```bash
CORTEXA_GATEWAY_BASE_URL=https://cortexa-prod-api-gateway.example.com uv run pytest
```

Ensure the Key Vault settings also match the target environment.

---

## Runtime and cost warning

Each full test suite run submits one `engine=dual` batch to the live Azure dev
pipeline. This triggers:

- Azure Blob Storage write (document upload)
- Azure AI Foundry (GPT) inference calls for extraction, scoring, harvesting, and seeding
- Azure AI Search vector queries
- Patent API calls (EPO, Lens)
- Cosmos DB reads and writes

**Typical wall-clock time:** 5–15 minutes depending on cold-start state of
container apps. `CORTEXA_PIPELINE_TIMEOUT_S` defaults to 600 s. Raise it if
needed.

**Approximate cost per run (dev):** ~$0.10–$0.50 in GPT token spend depending
on document size and model. The minimal synthetic PDF keeps this near the lower
bound. Supply a real research paper via the fixtures for more meaningful
patentability results.

**Do not run against prod** unless you intend to pay prod-tier inference costs
and have written authorization.
