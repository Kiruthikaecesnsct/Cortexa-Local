# E2E CD Gate Setup

This document describes how the Playwright E2E test suite (US061) is wired into the CD workflow as a deployment gate (GitHub Actions only).

## Overview

The E2E suite lives at `tests/e2e/` and tests the full user journey (upload → dashboard → drilldown → export) against the deployed Azure dev environment. Two test modes:

- **Default** (`pnpm test`): Runs against an existing completed batch (fast, <2 min, zero Azure AI cost). Excludes `@live` tag.
- **Live** (`pnpm run test:live`): Uploads a fresh batch, polls for completion, validates end-to-end (5-15 min, incurs Azure AI Foundry cost). Only `@live` tag.

## CD-Only Integration

### GitHub Actions

The E2E suite runs in **exactly one place**: the CD workflow. It is deliberately **not** part of CI — a browser-driven journey that hits live Azure only means something once the code is deployed, so it belongs to deployment, not pre-merge.

- **CD workflow** (`.github/workflows/cd.yml`) — deployment gate on **pull requests targeting `qa`** (the workflow triggers on `pull_request` to `qa` only, never on push). The `e2e` job runs AFTER the infrastructure is applied, backend services are deployed, and the frontend is published to a preview slot. It validates exactly what that CD run deployed.

Structure:

- **Reusable workflow**: `.github/workflows/_e2e.yml` encapsulates the E2E job. Accepts an optional `base_url` input (falls back to `vars.CORTEXA_E2E_BASE_URL` when empty).
- **CD parent**: `cd.yml` calls `_e2e.yml` after `deploy`, `frontend-deploy`, and `smoke` succeed. It passes no `base_url`, so the browser targets the stable dev SWA (`vars.CORTEXA_E2E_BASE_URL`) — the only slot with the linked backend, and it already fronts the freshly-deployed Container Apps revisions. A separate `frontend-validate` job statically probes the PR preview slot for the built bundle (no `/api` calls). See "Why" below.
- **Not in CI**: `ci.yml` (PRs → `dev`/`main`) does not run E2E, and `_changes.yml` carries no `e2e` path filter.

CI/CD runs on GitHub Actions only — there is no Azure DevOps pipeline in this repo.

## Gating

The E2E job is **opt-in** and will not break the CD workflow when dev is unreachable or secrets are missing.

Runs only when `vars.CORTEXA_E2E_ENABLED == 'true'`. If the variable is unset or `false`, the job is skipped. Set this repository variable to enable:

```bash
gh variable set CORTEXA_E2E_ENABLED --body "true"
```

The CD E2E job runs after the deployment steps (`deploy`, `frontend-deploy`, `smoke`) succeed or skip. It will not block a CD PR if the gate variable is unset.

## Azure Authentication

The E2E workflow authenticates to Azure using **OIDC** (federated credential, no long-lived secret).

Workflow permissions include `id-token: write` and the `dev-plan` environment. The `azure/login` action authenticates via:

- `secrets.AZURE_CLIENT_ID`
- `secrets.AZURE_TENANT_ID`
- `secrets.AZURE_SUBSCRIPTION_ID`

These secrets are configured on the `dev-plan` GitHub environment (Settings → Environments → dev-plan → Add secret).

## Required Secrets and Variables

The E2E workflow reads the following environment variables at runtime:

| Variable | Type | Source | Description |
|----------|------|--------|-------------|
| `CORTEXA_E2E_BASE_URL` | Variable | GitHub vars | Stable dev SWA URL (production slot with linked backend) |
| `CORTEXA_GATEWAY_BASE_URL` | Variable | GitHub vars | API gateway URL (Container App) |
| `CORTEXA_KEY_VAULT_NAME` | Variable | GitHub vars | Azure Key Vault name (e.g. `cortexa-dev-kv`) |
| `CORTEXA_ADMIN_EMAIL_SECRET` | Variable | GitHub vars | Key Vault secret name for admin email (default: `identity-admin-email`) |
| `CORTEXA_ADMIN_PASSWORD_SECRET` | Variable | GitHub vars | Key Vault secret name for admin password (default: `identity-admin-initial-password`) |
| `CORTEXA_E2E_BATCH_ID` | Variable | GitHub vars | Optional: Explicit batch ID to test against (if unset, resolves newest completed batch) |
| `CORTEXA_E2E_LIVE_TIMEOUT_MS` | Variable | GitHub vars | Optional: Timeout for live test (default: 900000 = 15 min) |
| `CORTEXA_E2E_POLL_INTERVAL_MS` | Variable | GitHub vars | Optional: Poll interval for live test (default: 5000 = 5 sec) |

### Base URL Resolution (CD workflow)

The CD E2E job ALWAYS targets the **stable dev SWA** (`vars.CORTEXA_E2E_BASE_URL`), never the preview slot.

**Why**: Azure SWA linked backends are bound to `builds/default` (production slot) ONLY. Preview slots (`pr-{number}`) do NOT inherit the linked backend. The frontend SPA's CSP (`connect-src 'self'`) blocks cross-origin calls, so a browser loaded from a preview slot gets 404 on every `/api/*` call → login fails → the entire E2E suite fails.

**How the CD gate validates both frontend and backend**:

1. **Backend validation**: The E2E browser targets the stable dev SWA (`vars.CORTEXA_E2E_BASE_URL`), which HAS the linked backend. The linked backend forwards `/api/*` to the dev api-gateway Container App. The `deploy` job publishes new Container Apps revisions immediately before the E2E gate runs, so the stable SWA serves against the freshly-deployed backend.

2. **Frontend validation**: A separate lightweight `frontend-validate` job curls the preview slot and asserts it serves the built SPA bundle (`/assets/index-*.js`), not raw source. This is a static artifact check only (no `/api` calls). It confirms the `frontend-deploy` step succeeded and the PR's new frontend bundle is live on the preview slot.

Set these as **repository variables** (not secrets, they're non-sensitive URLs/names):

```bash
gh variable set CORTEXA_E2E_BASE_URL --body "https://nice-beach-0701ce10f.7.azurestaticapps.net"
gh variable set CORTEXA_GATEWAY_BASE_URL --body "https://cortexa-dev-api-gateway.proudsmoke-86efe866.eastus.azurecontainerapps.io"
gh variable set CORTEXA_KEY_VAULT_NAME --body "cortexa-dev-kv"
```

The admin secret names default to `identity-admin-email` and `identity-admin-initial-password` if not set.

## Key Vault Access

The E2E suite reads admin credentials from Azure Key Vault at runtime using `DefaultAzureCredential`. The GitHub OIDC federated identity must have **Key Vault Secrets User** role on the Key Vault:

```bash
SP_OBJECT_ID=$(az ad sp show --id <CLIENT_ID> --query id -o tsv)
az keyvault set-policy --name cortexa-dev-kv \
  --object-id "$SP_OBJECT_ID" \
  --secret-permissions get list
```

Without this RBAC assignment, the E2E job will fail with a 403 Forbidden when attempting to fetch secrets.

## Artifacts

On failure, the Playwright HTML report is uploaded as an artifact: `playwright-report-<run-id>` (7-day retention).

Download and open `index.html` in a browser to inspect failures, screenshots, and traces.

## Cost Control

The default E2E job (`pnpm test`) runs against a completed batch and incurs zero Azure AI cost. The `@live` test (`pnpm run test:live`) uploads a fresh batch and consumes Azure AI Foundry credits (GPT-5.5 + GPT-5.4 inference, ~$0.50-$2.00 per run depending on document size).

The `@live` test is **excluded from the CD gate** (set `run_live: false`). The CD gate validates the deployment against an existing batch, not a fresh upload. To enable live tests, add a manual `workflow_dispatch` trigger to `_e2e.yml` or create a separate workflow that calls `_e2e.yml` with `run_live: true`.

## Troubleshooting

### E2E job skipped

Check that the gate variable is set: `vars.CORTEXA_E2E_ENABLED == 'true'`. If the variable is unset, the job is intentionally skipped.

### 403 Forbidden from Key Vault

The GitHub OIDC service principal lacks Key Vault Secrets User role. See Key Vault Access section above.

### DefaultAzureCredential not resolving

Ensure the Azure login step runs before the Playwright test step. `DefaultAzureCredential` resolves the OIDC token set by `azure/login`.

### Tests fail: batch not found

The suite requires at least one completed batch. If no completed batch exists and `CORTEXA_E2E_BATCH_ID` is unset, the test will fail. Either:

1. Set `CORTEXA_E2E_BATCH_ID` to a known-good batch ID.
2. Run the `@live` test once to seed a batch.

### Tests fail: dev environment unreachable

The E2E job gates on the `CORTEXA_E2E_ENABLED` variable to prevent red-failing every PR when dev is down. If dev is consistently unreachable, disable E2E by unsetting the gate variable.

## Playwright Config Notes

`playwright.config.ts` reads environment variables via `loadConfig()` in `src/support/env.ts`. The config automatically inverts the `@live` grep:

- `CI=true` and `grep` undefined → excludes `@live` (default)
- `CI=true` and `grep=/@live/` → includes only `@live` (live test)

The config retries failing tests 2x (`retries: 2`) and runs 2 workers (`workers: 2`). Cold-start flakes are mitigated by the retry policy.

## Summary

- E2E runs ONLY as a CD gate in GitHub Actions (`.github/workflows/cd.yml`), NOT in CI.
- Triggers on pull requests targeting `qa` (after deploy).
- Gated by `CORTEXA_E2E_ENABLED` — will not block the CD PR when missing.
- Authenticates to Azure via OIDC (federated credential, no long-lived secret).
- Reads admin credentials from Key Vault at runtime via `DefaultAzureCredential`.
- Uploads Playwright HTML report on failure.
- Default run excludes `@live` (fast, zero cost). Live test requires explicit opt-in and incurs Azure AI cost.
