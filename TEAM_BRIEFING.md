# Team briefing

Rewritten whole from the live files under `context/`, `contracts/`, and `decisions/`. Hard cap about 1500 tokens. Do not append by hand.

Narrative only. Who is working on what right now is not here. That lives in `ACTIVE.md`, generated from the claim cards on every sync.

_Last regenerated: 2026-10-13_

## Current focus

The desktop collector is the active surface. It pulls source files from four remote sources (GitHub, Azure DevOps, SSH/SFTP, and the saved Cortexa repository), splits them, and uploads them to the collector server for knowledge extraction. All collector code stays inside `Cortexa-dev/desktop/collector`.

Remote intake has five steps: Connect, Repository, Branch, Files, Proceed. Cortexa and SSH skip Files and fetch the whole source.

Sprint 5 is in progress. US150 merged via PR #26 (per-row remove and multi-select on the Documents grid; Extract button renamed to Process). US149 (PR #25) renamed Extract to Data Connectors. Next are US142 (end-to-end GitHub flow) and US143 (end-to-end Azure DevOps flow).

Two stories are queued on the same surface: US151 (GitHub device flow and GitHub App sign-in) and US152 (Entra ID sign-in for Azure DevOps). Both belong to feature `collector-remote-oauth`. PAT stays as the fallback and must keep working unchanged. Both are desktop-only with no Cortexa endpoint.

US126 (extract knowledge and upload) remains open. The extraction prompt must yield generalizable ideation, concepts, and key points, not literal code identifiers. Providers sit behind `Ai:Provider`: Gemini, Claude, and Groq.

## In-flight contracts

None in `contracts/`. US151 and US152 both have Contract None. The batch results contract (GET /collector/batches and GET /collector/batches/{id}/results) is settled and merged; see Recent decisions.

## Recent decisions

- **Remote HTTP timeouts (2026-10-10)**: Remote clients use an infinite `HttpClient.Timeout`. Rate-limit waits follow only the caller's cancel token. Blob reads use an idle timeout. `MaxRateLimitRetries` is per provider (Azure DevOps 10). Do not set a total timeout on these clients.
- **Remote intake picks files before splitting (2026-10-10)**: The Files step runs for GitHub and Azure DevOps. A selection skips the 2,000-file fetch cap and is capped at 10,000. Splitting runs four at a time by default. DataGrid column widths in XAML need a `DataGridLength`.
- **Extract shows "Analyzed" (2026-10-10)**: Label only. Stored status and History stage keep the old "Extracted" name.
- **Remote credentials are session memory only (2026-10-09)**: PATs and the SSH passphrase are never written to Credential Manager or disk. Old saved secrets are purged at startup. This supersedes the US134 Credential Manager rule. The SSH host-key pin was removed as an accepted risk.
- **Collector batch results contract (2026-10-08)**: Batch list adds `stage`, `harvesting_count`, `seeding_count`. Results return `{batch_id, candidates[]}` with `knowledge_links`. Breaking for clients on the old shapes.
- **Source-agnostic remote files table (2026-10-08)**: One `remote_files` table covers all four sources. Do not drop the `COALESCE` in the unique key. SSH reuses it.
- **Upload retry and rollback (2026-10-08)**: Publish failures do not roll back. Recovery is a replay with the same Idempotency-Key. A 409 `upload_in_progress` is retryable. A 409 `idempotency_key_reused` is final.

**Resolved 2026-10-13**: OAuth tokens for US151 and US152 may persist encrypted in Credential Manager (approved exception to US138); PATs and the SSH passphrase stay session-only. See `decisions/20261013-collector-oauth-token-persistence.md`.

## Standing blockers and open follow-ups

- **US151 external prerequisites**: owner and org of the GitHub App registration, admin approval of the App installation, "Enable Device Flow" switched on, and the client ID for dev and Alpha's machine. The App path cannot be tested end to end without approval.
- **US152 external prerequisites**: Entra app registration, single or multi-tenant choice, admin consent for the Azure DevOps delegated permission, and confirmation that the target org is Entra-backed. If it is not, PAT stays the only path.
- **US140 and US141 live checks not run**: the 5,000-file run and the full GitHub and Azure DevOps Files to Proceed flows. US142 and US143 cover them.
- **Not verified live**: Azure DevOps UI, rate-limit banner, Esc cancel (US134), SSH and saved repository collectors, US138 session-only tokens, US139 Settings flow.
- **Manual US133 check not run**: stop the stack mid-upload, restart, retry from History. The collector server on localhost:5091 is not started by run-local.ps1, so launch it by hand.
- **Web retry stalls**: retry stalls until the watchdog fails the batch. Re-upload with the original Idempotency-Key is the only recovery.
- **Duplicate batch gap (US133)**: if the server committed a failed upload and the selection then changed, a second batch can hold the same documents. The client does not detect it.
- **Organization URL on the connect form not built**: needed for GitHub and Azure DevOps. US152 keeps it required. The Azure DevOps organization still sits on the Extract screen.
- **Live sign-in needed**: the upload-to-candidates path needs a local Cortexa account.
- **Cosmos queries untested locally**: batch and results queries have only run live in Azure, not the emulator.
- **DOCX heading parity gap**: the desktop collector does not treat DOCX heading styles as section breaks. The backend does.
