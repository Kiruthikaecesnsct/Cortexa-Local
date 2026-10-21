# Team briefing

Rewritten whole from the live files under `context/`, `contracts/`, and `decisions/`. Hard cap about 1500 tokens. Do not append by hand.

Narrative only. Who is working on what right now is not here. That lives in `ACTIVE.md`, generated from the claim cards on every sync.

_Last regenerated: 2026-10-21_

## Current focus

The desktop collector is the active surface. It pulls source files from four remote sources (GitHub, Azure DevOps, SSH/SFTP, and the saved Cortexa repository), splits them, and uploads them to the collector server for knowledge extraction. All collector code stays inside `Cortexa-dev/desktop/collector`.

Remote intake has five steps: Connect, Repository, Branch, Files, Proceed. Cortexa and SSH skip Files and fetch the whole source.

Sign-in is now available for both forge sources. US151 (GitHub device flow and GitHub App sign-in, PR #27, 2026-10-13) and US152 (Entra ID sign-in for Azure DevOps, PR #28, 2026-10-21) are merged. PAT stays as the fallback for both and works unchanged. Both paths are desktop-only, with no Cortexa endpoint.

Next open work: US142 (end-to-end GitHub flow) and US143 (end-to-end Azure DevOps flow). They cover the live Files to Proceed checks listed below.

Sprint 5 context: US150 (PR #26) added per-row remove and multi-select on the Documents grid and renamed Extract to Process. US149 (PR #25) renamed Extract to Data Connectors.

US126 (extract knowledge and upload) remains open. The extraction prompt must yield generalizable ideation, concepts, and key points, not literal code identifiers. Providers sit behind `Ai:Provider`: Gemini, Claude, and Groq.

## In-flight contracts

None in `contracts/`. US151 and US152 both recorded Contract None. The token seam is internal to the collector and is recorded under Recent decisions. The batch results contract (GET /collector/batches and GET /collector/batches/{id}/results) is settled and merged.

## Recent decisions

- **Remote token seam (2026-10-13)**: Remote requests get their token from `IRemoteTokenResolver`, which picks an `IRemoteTokenSource` per provider from `IRemoteAuthState`. `RemoteTokenAuthHandler` sends it. GitHub and Azure DevOps both use it now (Azure DevOps since US152). Do not add provider checks to the HTTP pipeline. OAuth client IDs are public and live in `appsettings.json`. A method with no client ID shows disabled with a note.
- **OAuth token persistence (2026-10-13)**: US151 and US152 OAuth tokens may persist encrypted in Credential Manager. This is an approved exception to US138. PATs and the SSH passphrase stay session-only. The US138 startup purge skips the OAuth slots. Sign-out clears them. MSAL cache bytes are chunked across numbered Credential Manager targets; the 2560-byte store limit is unchanged.
- **Remote HTTP timeouts (2026-10-10)**: Remote clients use an infinite `HttpClient.Timeout`. Rate-limit waits follow only the caller's cancel token. Blob reads use an idle timeout. `MaxRateLimitRetries` is per provider (Azure DevOps 10). Do not set a total timeout on these clients.
- **Remote intake picks files before splitting (2026-10-10)**: The Files step runs for GitHub and Azure DevOps. A selection skips the 2,000-file fetch cap and is capped at 10,000.
- **Remote credentials are session memory only (2026-10-08)**: PATs and the SSH passphrase are never written to Credential Manager or disk. Old saved secrets are purged at startup, except the OAuth slots noted above. The SSH host-key pin was removed as an accepted risk.
- **Collector batch results contract (2026-10-08)**: Batch list adds `stage`, `harvesting_count`, `seeding_count`. Results return `{batch_id, candidates[]}` with `knowledge_links`. Breaking for clients on the old shapes.
- **Source-agnostic remote files table (2026-10-08)**: One `remote_files` table covers all four sources. Do not drop the `COALESCE` in the unique key. SSH reuses it.
- **Upload retry and rollback (2026-10-08)**: Publish failures do not roll back. Recovery is a replay with the same Idempotency-Key. A 409 `upload_in_progress` is retryable. A 409 `idempotency_key_reused` is final.

## Standing blockers and open follow-ups

- **US151 org approval**: the Imaginext-Innovations-Private-Limited org returns 403 to the OAuth sign-in until an org owner approves the OAuth App and users meet the org's 2FA rule. Live sign-in to that org is blocked until then.
- **Entra live sign-in (US152)**: the Entra app registration, tenant choice, and admin consent for the Azure DevOps delegated permission were external to the story. Confirm them before a live Azure DevOps Entra sign-in is claimed as verified. If the target org is not Entra-backed, PAT stays the only Azure DevOps path.
- **Azure DevOps organization URL**: the connect form does not collect the organization URL for GitHub and Azure DevOps yet. It stays required. The Azure DevOps organization still sits on the Extract screen.
- **Live checks not run**: the 5,000-file run and the full GitHub and Azure DevOps Files to Proceed flows (US142 and US143 cover them). Also unverified live: Azure DevOps UI, rate-limit banner, Esc cancel (US134), SSH and saved repository collectors, US138 session-only tokens, US139 Settings flow.
- **Manual US133 check not run**: stop the stack mid-upload, restart, retry from History. The collector server on localhost:5091 is not started by run-local.ps1, so launch it by hand.
- **Web retry stalls**: retry stalls until the watchdog fails the batch. Re-upload with the original Idempotency-Key is the only recovery.
- **Duplicate batch gap (US133)**: if the server committed a failed upload and the selection then changed, a second batch can hold the same documents. The client does not detect it.
- **Cosmos queries untested locally**: batch and results queries have only run live in Azure, not the emulator.
- **DOCX heading parity gap**: the desktop collector does not treat DOCX heading styles as section breaks. The backend does.
