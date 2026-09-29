# Cortexa E2E Test Suite

Playwright E2E tests for the Cortexa patent analysis pipeline. Tests the full user journey from upload to export against the deployed Azure dev environment.

## CD Integration

The suite runs ONLY as a **CD gate** (PRs → `qa`), NOT in CI. It validates the deployment after infrastructure apply, backend deploy, and frontend publish.

The browser targets the **stable dev SWA** (`vars.CORTEXA_E2E_BASE_URL`), which has the linked backend and serves the freshly-deployed Container Apps revisions. A separate `frontend-validate` job verifies the preview slot serves the built frontend bundle (static check only, no `/api` calls).

**Why stable SWA, not preview slot**: Azure SWA linked backends are bound to `builds/default` (production slot) ONLY. Preview slots (`pr-{number}`) do NOT inherit the linked backend. The SPA's CSP (`connect-src 'self'`) blocks cross-origin calls, so a browser loaded from a preview slot gets 404 on every `/api/*` call → login fails → the entire suite fails.

## Setup

Install dependencies and browsers:

```bash
cd tests/e2e
pnpm install
pnpm run install-browsers
```

Create `.env` from `.env.example` and configure:

```bash
cp .env.example .env
```

Fill in the deployed dev environment URLs and Key Vault secrets. `CORTEXA_E2E_BATCH_ID` is optional — if unset, the suite resolves the newest completed batch.

## Run Commands

Default run (excludes `@live` tag — fast, runs against a completed batch):

```bash
pnpm test
```

Live journey test (tagged `@live` — uploads a batch, polls for completion, validates end-to-end):

```bash
pnpm run test:live
```

Headed mode (for debugging):

```bash
pnpm run test:headed
```

## Batch Resolution

The suite requires one completed batch to test against. Resolution priority:

1. `CORTEXA_E2E_BATCH_ID` env var (explicit)
2. Newest `Completed` batch from `GET /batches` (automatic)
3. Seed-and-poll once (last resort, only in `@live` test)

Default specs (`upload.spec.ts`, `dashboard.spec.ts`, `drilldown.spec.ts`, `export.spec.ts`) assume a completed batch exists. Set `CORTEXA_E2E_BATCH_ID` to a known-good batch ID to skip resolution.

The `@live` test (`full-journey.spec.ts`) uploads a fresh batch and polls until terminal state (default 15 min timeout). This test incurs Azure GPT costs and is excluded by default.

## Cost and Runtime Warning

Full batch processing takes 5-15 minutes and consumes Azure AI Foundry credits. Run `@live` tests sparingly. Default specs run in under 2 minutes and cost nothing beyond the one-time batch seed.

## Validation Rules

### PDF Export

- Filename ends with `.pdf`
- Content starts with `%PDF-`
- Non-empty file

### JSON Export

- Filename ends with `.json`
- Valid JSON parse
- `schemaVersion: '1.0'`
- `batchId` matches the batch under test
- Dashboard export: `harvesting` or `seeding` key present
- Detail export: `opportunity` or `report` key present

## Sample Fixture

`fixtures/sample_paper.pdf` is a minimal valid PDF (612 bytes) with realistic research-paper structure. Replace it with a real PDF if the ingestion pipeline rejects minimal fixtures.

## Lint and Typecheck

```bash
pnpm run lint
pnpm exec tsc --noEmit
```

Both must pass before committing.
