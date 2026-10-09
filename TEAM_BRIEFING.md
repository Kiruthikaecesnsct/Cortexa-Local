# Team briefing

Rewritten whole from the live files under `context/`, `contracts/`, and `decisions/`. Hard cap about 1500 tokens. Do not append by hand.

Narrative only. Who is working on what right now is **not** here. That lives in `ACTIVE.md`, generated from the claim cards on every sync.

_Last regenerated: 2026-10-09_

## Current focus

The desktop collector is the active surface. It pulls source files from four remote sources (GitHub, Azure DevOps, SSH/SFTP, and the saved Cortexa repository) and uploads them to the collector server for knowledge extraction. Sprint 5 is tightening how remote sources behave and how the collector is configured. All collector code stays inside `Cortexa-dev/desktop/collector`.

Settings (US139, PR #20, merged to dev 2026-10-09) is now two panes: AI model, AI provider keys, and Connections. The About section was dropped at Alpha's request. The UI font is Cambria. The chosen AI provider and model are saved in `usersettings.json` under the `AiModelChoice` key. Extract reads that choice and stays blocked until a provider key or a Bedrock sign-in exists.

Next up in Sprint 5: US140 (GitHub file pick before splitting), US141, then US142 and US143 (end-to-end tests).

Earlier merges: US138 (desktop source collectors match the web Scan flow, PR #19), US132 (collector server rollback and resume, PR #12), US133 (upload failure handling and resume, PR #13), US134 (GitHub and Azure DevOps collectors, PR #14), US135 (SSH collector, PR #15), US136 (saved repository collector, PR #16), and US137 (desktop UI revamp, PR #17, presentation only).

US126 is still open. Knowledge extraction runs through a prompt that yields generalizable ideation, concepts, and key points rather than literal code identifiers. Both AI providers sit behind the same `Ai:Provider` config.

## In-flight contracts

None live. The collector batch results contract (GET /collector/batches and GET /collector/batches/{id}/results) was settled in US128 and merged, so it is out of `contracts/`. Its shape is recorded under Recent decisions.

## Recent decisions

- **Settings layout and provider choice (US139, 2026-10-09)**: Settings has two panes. The provider and model choice lives in `AiModelChoice` in `usersettings.json`. Extract does not pick a provider itself any more. Any story or test step that selects the provider on Extract is stale.
- **Remote credentials are session memory only (US138, 2026-10-09)**: remote PATs and the SSH passphrase live in session memory. They never go to Windows Credential Manager or to disk. Old saved remote secrets are purged at startup. This supersedes the Credential Manager rule in `decisions/20261008-collector-remote-pat-handling.md`. Remote tokens must be entered again after each app restart.
- **SSH host-key fingerprint pin removed (US138, 2026-10-09)**: Alpha accepted the risk. Revisit if SSH sources get a security review.
- **GitHub repo listing (US138, 2026-10-09)**: GitHub lists organization repos first. On a 404 it falls back to the /users listing.
- **Source-agnostic remote files table (US134, 2026-10-08; reused by US135 and US136)**: one `remote_files` table (V003) covers all four remote sources. A fetched file skips the download when its cached blob SHA matches the remote one. See `decisions/20261008-collector-remote-files-table.md`.
- **Remote API transport rules (US134, 2026-10-08)**: a token goes only to its configured API origin, and redirects are off. A rate-limit gate retries on 429. Pinned API versions are GitHub 2026-03-10 and Azure DevOps 7.1. See `decisions/20261008-collector-remote-pat-handling.md` for the transport rules only.
- **Collector batch results shape (US128, 2026-10-08)**: the batch list carries `stage` plus `harvesting_count` and `seeding_count`. Results return grouped candidates keyed by DeterministicIds. Old flat-array clients fail. See `decisions/20261008-collector-batch-results.md`.
- **Publish failures do not roll back (US132, 2026-10-08)**: a publish failure leaves the saga row and written rows in place. Recovery is a replay with the same Idempotency-Key. See `decisions/20261008-collector-upload-no-rollback.md`.
- **Saga request fingerprint (US132, 2026-10-08)**: the same key with a different fingerprint returns 409 `idempotency_key_reused`. Rows with no fingerprint replay instead. See `decisions/20261008-saga-request-fingerprint.md`.
- **Upload retry resends stored bytes (US133, 2026-10-08)**: bodies are kept in `batch_payloads` (V002) and never rebuilt. 409 `upload_in_progress` is retryable. 409 `idempotency_key_reused` is final. See `decisions/20261008-collector-upload-retry-payload.md`.
- **Extraction prompt scope (US126)**: the prompt must yield generalizable concepts and ideation, not literal identifiers. See `context/US126.md`.

## Standing blockers and open follow-ups

- **US142 and US143 test steps need a recheck.** US142 says to select the provider on Extract. After US139 that choice is made in Settings, and Extract is blocked until a key or Bedrock sign-in exists. Fix both stories' steps before either one starts.
- **Organization URL on the connect form (from US134, not yet built as recorded).** Ask for the organization URL next to the token for both GitHub and Azure DevOps. The Azure DevOps organization still sits on the Extract screen apart from the token.
- **Not verified live.** The Azure DevOps UI, the rate-limit banner, and Esc cancel have not been checked in the running app (carried from US134). The SSH and saved repository collectors have not been run end to end. The US138 behaviour (session-only tokens, GitHub /users fallback) and the US139 Settings flow are not recorded as run live.
- **Manual US133 E2E check not yet run.** Stop the stack mid-upload, restart it, then retry from History. The collector server on localhost:5091 is not started by run-local.ps1, so launch it by hand.
- **Web retry stalls.** Retry on the collector still stalls until the watchdog fails the batch. Re-upload with the original Idempotency-Key is the only recovery path.
- **Duplicate batch gap (from US133).** If the server committed a failed upload and the user then changes the selection (new key), the server can hold a second batch for the same documents. The client does not detect this yet.
- **Live sign-in needed.** The full upload-to-candidates path cannot be verified until a local Cortexa account is set up.
- **Cosmos queries untested locally.** The batch and results queries have only run live in Azure, not in the emulator.
- **DOCX heading parity gap.** The desktop collector does not parse DOCX heading styles as section breaks. The backend does.
- **US140 live checks still open (from US140, 2026-10-10).** The 5,000-file performance run and the full GitHub Files to Proceed flow were not exercised live. US142's end-to-end test should cover both.
