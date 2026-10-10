# Team briefing

Rewritten whole from the live files under `context/`, `contracts/`, and `decisions/`. Hard cap about 1500 tokens. Do not append by hand.

Narrative only. Who is working on what right now is **not** here. That lives in `ACTIVE.md`, generated from the claim cards on every sync.

_Last regenerated: 2026-10-10_

## Current focus

The desktop collector is the active surface. It pulls source files from four remote sources (GitHub, Azure DevOps, SSH/SFTP, and the saved Cortexa repository), splits them, and uploads them to the collector server for knowledge extraction. All collector code stays inside `Cortexa-dev/desktop/collector`.

Remote intake has five steps: Connect, Repository, Branch, Files, Proceed. Choosing a branch opens the Files step for GitHub and Azure DevOps. Cortexa and SSH skip Files and fetch the whole source. Extract shows "Analyzed" in place of "Extracted". The stored status and the History stage keep the old name.

Sprint 5 is in progress. US141 (Azure DevOps file picking) is merged to dev. Next are US142 (end-to-end test, GitHub full flow) and US143 (end-to-end test, Azure DevOps full flow, after US141 and US142).

Recent merges: US141 (Azure DevOps file pick, project filter, "size unknown" summary, 2026-10-10), US140 (GitHub file pick before splitting, PR #22), US139 (Settings two panes, PR #20), US138 (desktop source collectors match the web Scan flow, PR #19), US132 to US136 (rollback and resume, upload failure handling, GitHub and Azure DevOps, SSH, and saved repository collectors; PRs #12 to #16), and US137 (desktop UI revamp, PR #17, presentation only).

US126 is still open. Knowledge extraction runs through a prompt that yields generalizable ideation, concepts, and key points, not literal code identifiers. Gemini and Claude both sit behind `Ai:Provider`.

## In-flight contracts

None live in `contracts/`. The collector batch results contract (GET /collector/batches and GET /collector/batches/{id}/results) is settled and merged. Its shape is under Recent decisions.

## Recent decisions

- **Remote HTTP timeouts and retry limits (US141, 2026-10-10)**: Remote clients use an infinite `HttpClient.Timeout`. `RateLimitHandler` applies a per-attempt `TimeoutSeconds` to the send and body buffering only, so rate-limit waits follow the caller's cancel token. Blob reads use an idle timeout in `GuardedReadStream`. The retry limit is per provider through `MaxRateLimitRetries` (Azure DevOps 10). Do not set a total timeout on the clients.
- **Remote intake picks files before splitting (US140, 2026-10-10)**: The Files step runs for GitHub and Azure DevOps. A selection skips the 2,000-file fetch cap and is capped at 10,000. Splitting runs four at a time by default. The file tree, intake progress card, document batcher, and documents grid are shared. In XAML, DataGrid column widths need a `DataGridLength`; `sys:Double` crashes at load.
- **Settings layout and provider choice (US139, 2026-10-09)**: Two panes. The provider and model choice lives in `AiModelChoice` in `usersettings.json`. Extract does not pick a provider.
- **Remote credentials are session memory only (US138, 2026-10-09)**: Remote PATs and the SSH passphrase are never written to Credential Manager or disk. Old saved secrets are purged at startup. Tokens must be entered again after each restart.
- **SSH host-key fingerprint pin removed (US138, 2026-10-09)**: Alpha accepted the risk. Revisit if SSH sources get a security review.
- **Collector batch results contract (US128, 2026-10-08)**: The batch list adds `stage`, `harvesting_count`, and `seeding_count`. The results endpoint returns `{batch_id, candidates[]}`, and each candidate carries `knowledge_links`. These are breaking changes for clients on the old shapes.
- **Source-agnostic remote files table (US134, 2026-10-08)**: One `remote_files` table covers all four remote sources. A fetched file skips download when its blob SHA matches.
- **Upload retry and rollback (US132 and US133, 2026-10-08)**: Publish failures do not roll back. Recovery is a replay with the same Idempotency-Key, and the server resumes from the saga row. A retry resends the exact stored body. A 409 `upload_in_progress` is retryable. A 409 `idempotency_key_reused` is final.

## Standing blockers and open follow-ups

- **US140 and US141 live checks still open.** The 5,000-file performance run and the full GitHub and Azure DevOps Files to Proceed flows were not exercised live. US142 and US143 should cover them.
- **Organization URL on the connect form (not yet built).** Ask for the organization URL next to the token for both GitHub and Azure DevOps. The Azure DevOps organization still sits on the Extract screen.
- **Not verified live.** The Azure DevOps UI, the rate-limit banner, and Esc cancel (US134). The SSH and saved repository collectors have not been run end to end. The US138 session-only tokens, the GitHub /users fallback, and the US139 Settings flow are not recorded as run live.
- **Manual US133 E2E check not yet run.** Stop the stack mid-upload, restart it, then retry from History. The collector server on localhost:5091 is not started by run-local.ps1, so launch it by hand.
- **Web retry stalls.** Retry on the collector stalls until the watchdog fails the batch. Re-upload with the original Idempotency-Key is the only recovery.
- **Duplicate batch gap (US133).** If the server committed a failed upload and the user then changed the selection (new key), the server can hold a second batch for the same documents. The client does not detect this yet.
- **Live sign-in needed.** The full upload-to-candidates path cannot be verified until a local Cortexa account is set up.
- **Cosmos queries untested locally.** The batch and results queries have only run live in Azure, not in the emulator.
- **DOCX heading parity gap.** The desktop collector does not parse DOCX heading styles as section breaks. The backend does.
