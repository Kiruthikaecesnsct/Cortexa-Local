# Team briefing

Rewritten whole from the live files under `context/`, `contracts/`, and `decisions/`. Hard cap about 1500 tokens. Do not append by hand.

Narrative only. Who is working on what right now is **not** here. That lives in `ACTIVE.md`, generated from the claim cards on every sync.

_Last regenerated: 2026-10-12_

## Current focus

The desktop collector is the active surface. It pulls source files from four remote sources (GitHub, Azure DevOps, SSH/SFTP, and the saved Cortexa repository), splits them, and uploads them to the collector server for knowledge extraction. All collector code stays inside `Cortexa-dev/desktop/collector`.

Remote intake has five steps: Connect, Repository, Branch, Files, Proceed. Choosing a branch opens the Files step for GitHub and Azure DevOps. Cortexa and SSH skip Files and fetch the whole source.

Sprint 5 is in progress. US150 merged via PR #26. It added per-row remove and multi-select to the Documents grid on Data Connectors, and renamed the Extract knowledge button to Process. The action buttons now sit above the Documents card. US149 (PR #25) renamed the Extract screen to Data Connectors. Next are US142 (end-to-end test, GitHub full flow) and US143 (end-to-end test, Azure DevOps full flow).

US126 (extract knowledge and upload) remains open per its capsule. The extraction prompt must yield generalizable ideation, concepts, and key points, not literal code identifiers. Providers sit behind `Ai:Provider`: Gemini, Claude, and Groq.

## In-flight contracts

None live in `contracts/`. The collector batch results contract (GET /collector/batches and GET /collector/batches/{id}/results) is settled and merged. Its shape is under Recent decisions.

## Recent decisions

- **Remote HTTP timeouts (US141, 2026-10-10)**: Remote clients use an infinite `HttpClient.Timeout`. Rate-limit waits follow only the caller's cancel token. Blob reads use an idle timeout. The retry limit is per provider (`MaxRateLimitRetries`, Azure DevOps 10). Do not set a total timeout on these clients.
- **Remote intake picks files before splitting (US140, 2026-10-10)**: The Files step runs for GitHub and Azure DevOps. A selection skips the 2,000-file fetch cap and is capped at 10,000. Splitting runs four at a time by default. The file tree, intake progress card, and documents grid are shared. In XAML, DataGrid column widths need a `DataGridLength`.
- **Extract shows "Analyzed" (US140)**: The label changed on the Extract screen only. The stored status and the History stage keep the old "Extracted" name.
- **Settings in two panes (US139, 2026-10-09)**: The provider and model choice lives in `AiModelChoice` in `usersettings.json`. Extract does not pick a provider.
- **Remote credentials are session memory only (US138, 2026-10-09)**: PATs and the SSH passphrase are never written to Credential Manager or disk. Old saved secrets are purged at startup. This supersedes the US134 Credential Manager rule. Tokens must be entered again after each restart. The SSH host-key pin was removed by Alpha as an accepted risk. Revisit it if SSH sources get a security review.
- **Collector batch results contract (US128, 2026-10-08)**: The batch list adds `stage`, `harvesting_count`, and `seeding_count`. The results endpoint returns `{batch_id, candidates[]}`, and each candidate carries `knowledge_links`. These are breaking changes for clients on the old shapes.
- **Source-agnostic remote files table (US134, 2026-10-08)**: One `remote_files` table covers all four remote sources. A fetched file skips download when its blob SHA matches. Do not drop the `COALESCE` in the unique key. SSH reuses this table.
- **Upload retry and rollback (US132 and US133, 2026-10-08)**: Publish failures do not roll back. Recovery is a replay with the same Idempotency-Key, and the server resumes from the saga row. A retry resends the exact stored bytes. A 409 `upload_in_progress` is retryable. A 409 `idempotency_key_reused` is final.

## Standing blockers and open follow-ups

- **US140 and US141 live checks not run.** The 5,000-file performance run and the full GitHub and Azure DevOps Files to Proceed flows were not exercised live. US142 and US143 cover them.
- **Organization URL on the connect form (not built).** Ask for it next to the token for GitHub and Azure DevOps. The Azure DevOps organization still sits on the Extract screen.
- **Not verified live.** The Azure DevOps UI, the rate-limit banner, Esc cancel (US134), the SSH and saved repository collectors, the US138 session-only tokens, and the US139 Settings flow.
- **Manual US133 check not run.** Stop the stack mid-upload, restart it, then retry from History. The collector server on localhost:5091 is not started by run-local.ps1, so launch it by hand.
- **Web retry stalls.** Retry on the collector stalls until the watchdog fails the batch. Re-upload with the original Idempotency-Key is the only recovery.
- **Duplicate batch gap (US133).** If the server committed a failed upload and the user then changed the selection, the server can hold a second batch for the same documents. The client does not detect this yet.
- **Live sign-in needed.** The full upload-to-candidates path cannot be verified until a local Cortexa account is set up.
- **Cosmos queries untested locally.** The batch and results queries have only run live in Azure, not in the emulator.
- **DOCX heading parity gap.** The desktop collector does not parse DOCX heading styles as section breaks. The backend does.
