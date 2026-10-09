# Team briefing

Rewritten whole from the live files under `context/`, `contracts/`, and `decisions/`. Hard cap about 1500 tokens. Do not append by hand.

Narrative only. Who is working on what right now is **not** here. That lives in `ACTIVE.md`, generated from the claim cards on every sync.

_Last regenerated: 2026-10-09_

## Current focus

The desktop collector is hardening its extraction and upload path and pulls source files from four remote sources. GitHub and Azure DevOps (US134, PR #14), SSH/SFTP (US135, PR #15), and the saved Cortexa repository (US136, PR #16) are all in place. All of them share one source-agnostic cache table, so each new source reuses the same fetch, skip-if-unchanged, and document-link logic.

US137 (premium enterprise UI revamp of the desktop collector) merged to dev via PR #17 on 2026-10-09. It is presentation only: XAML styles and display-only view model properties. It changes no collector server endpoint, API shape, or data table.

US126 is still open. Knowledge extraction runs through a prompt that yields generalizable ideation, concepts, and key points rather than literal code identifiers. Both AI providers sit behind the same `Ai:Provider` config.

Merged to dev in recent PRs: US132 (collector server rollback and resume, PR #12), US133 (upload failure handling and resume, PR #13), US134 (GitHub and Azure DevOps collectors, PR #14), US135 (SSH collector, PR #15), US136 (saved repository collector, PR #16), and US137 (desktop UI revamp, PR #17).

## In-flight contracts

None live. The collector batch results contract (GET /collector/batches and GET /collector/batches/{id}/results) was settled in US128 and merged, so it has left `contracts/`. Its shape is recorded under Recent decisions.

## Recent decisions

- **Saved repository source joins the remote files table (US136, 2026-10-09)**: the `cortexa_repo` provider reuses the `remote_files` table like the other remote sources. No new cache table is added.
- **SSH source joins the remote files table (US135, 2026-10-09)**: SSH/SFTP fetches reuse the `remote_files` table. Blob keys encode path, mtime and size. See `decisions/20261008-collector-remote-files-table.md`.
- **Remote files table (US134, 2026-10-08)**: one source-agnostic `remote_files` table (V003) covers GitHub, Azure DevOps, SSH, and cortexa_repo. Unique on provider, repo key, branch, and path. A fetched file skips the download when its cached blob SHA matches the remote one.
- **Remote PAT handling (US134, 2026-10-08)**: PATs live in Windows Credential Manager. A token goes only to its configured API origin, and redirects are off. A rate-limit gate retries 429. Pinned: GitHub 2026-03-10, Azure DevOps 7.1. See `decisions/20261008-collector-remote-pat-handling.md`.
- **Collector batch results shape (US128, 2026-10-08)**: the batch list carries `stage` plus `harvesting_count` and `seeding_count`. Results return grouped candidates keyed by DeterministicIds. Old flat-array clients fail. See `decisions/20261008-collector-batch-results.md`.
- **Publish failures do not roll back (US132, 2026-10-08)**: a publish failure leaves the saga row and written rows in place. Recovery is a replay with the same Idempotency-Key. See `decisions/20261008-collector-upload-no-rollback.md`.
- **Saga request fingerprint (US132, 2026-10-08)**: the same key with a different fingerprint returns 409 `idempotency_key_reused`. Rows with no fingerprint replay instead. See `decisions/20261008-saga-request-fingerprint.md`.
- **Upload retry resends stored bytes (US133, 2026-10-08)**: bodies are kept in `batch_payloads` (V002) and never rebuilt. 409 `upload_in_progress` is retryable. 409 `idempotency_key_reused` is final. See `decisions/20261008-collector-upload-retry-payload.md`.
- **Extraction prompt scope (US126)**: the prompt must yield generalizable concepts and ideation, not literal identifiers. This keeps extraction portable across domains. See `context/US126.md`.

## Standing blockers and open follow-ups

- **Organization URL on the connect form (from US134, not yet built).** Ask for the organization URL next to the token for both GitHub and Azure DevOps. Today GitHub has no organization step, and the Azure DevOps organization sits on the Extract screen apart from the token.
- **Not verified live (US134, carried into US135 and US136).** The Azure DevOps UI, the rate-limit banner, and Esc cancel have not been checked in the running app. The SSH and saved repository collectors have not been run end to end either.
- **Manual US133 E2E check not yet run.** Stop the stack mid-upload, restart it, then retry from History. The collector server on localhost:5091 is not started by run-local.ps1, so launch it by hand.
- **Web retry stalls.** Retry on the collector still stalls until the watchdog fails the batch. Re-upload with the original Idempotency-Key is the only recovery path.
- **Duplicate batch gap (from US133).** If the server committed a failed upload and the user then changes the selection (new key), the server can hold a second batch for the same documents. The client does not detect this yet.
- **Live sign-in needed.** The full upload-to-candidates path cannot be verified until a local Cortexa account is set up.
- **Cosmos queries untested locally.** The batch and results queries have only run live in Azure, not in the emulator.
- **DOCX heading parity gap.** The desktop collector does not parse DOCX heading styles as section breaks. The backend does.
