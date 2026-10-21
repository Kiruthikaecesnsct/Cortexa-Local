# Team briefing

Rewritten whole from the live files under `context/`, `contracts/`, and `decisions/`. Hard cap about 1500 tokens. Do not append by hand.

Narrative only. Who is working on what right now is not here. That lives in `ACTIVE.md`, generated from the claim cards on every sync.

_Last regenerated: 2026-10-21_

## Current focus

The desktop collector is the active surface. It pulls source files from four remote sources (GitHub, Azure DevOps, SSH/SFTP, and the saved Cortexa repository), splits them, and uploads them for knowledge extraction. All collector code stays inside `Cortexa-dev/desktop/collector`.

Sprint 5 is in progress. US153 (invention candidates from whole documents with Gemini) merged in PR #29 on 2026-10-21. Its claim is released and its capsule is archived. US154 is next. It wires the candidate pass into Process and shows candidates in Review. Candidates stay local and nothing is uploaded.

## Ordering and open follow-ups for US154

- US154 depends on US153 and touches the Process screen. Keep its panel separate so US150 edits do not collide.
- Run the knowledge pass and the candidate pass sequentially on a selection, so Gemini concurrency is not doubled.
- A whole-document 400 or 413 can mark every Gemini key as Rejected in Settings. A follow-up bug is not filed yet.
- Confirm the Gemini keys are on a paid tier before a long run.

## In-flight contracts

None in `contracts/`. Candidate types are in-process only and recorded under the decision below.

## Recent decisions

- **Collector candidates identified on the desktop (2026-10-21)**: Each selected document is read whole and sent to Gemini in its own request. Candidates are shown locally. Uploading candidates is out of scope. Unit knowledge extraction stays.
- **Remote token seam (2026-10-13)**: Remote requests get their token from `IRemoteTokenResolver`. Do not add provider checks to the HTTP pipeline.
- **OAuth token persistence (2026-10-13)**: US151 and US152 OAuth tokens may persist encrypted in Credential Manager. This is an approved exception to US138. PATs and the SSH passphrase stay session-only.
- **Remote HTTP timeouts (2026-10-10)**: Remote clients use an infinite `HttpClient.Timeout`. Do not set a total timeout on these clients.
- **Batch results contract (2026-10-08)**: Batch list adds `stage`, `harvesting_count`, `seeding_count`. Results return `{batch_id, candidates[]}`. Breaking for clients on the old shapes.
- **Upload retry and rollback (2026-10-08)**: Publish failures do not roll back. Recovery is a replay with the same Idempotency-Key.

## Standing blockers and open follow-ups

- **US151 org approval**: the Imaginext-Innovations-Private-Limited org returns 403 to OAuth sign-in until an org owner approves the OAuth App.
- **Entra live sign-in (US152)**: confirm the Entra app registration, tenant, and admin consent before a live Entra sign-in is claimed as verified.
- **Backlog audit legacy errors**: 8 errors on US142, US143, US149 to US152 (dangling DependsOn, missing layer tag, orphan frontend). They block `backlog-graph.py build`. Fix before the next generated sprint.
- **Live checks not run**: the 5,000-file run and the full GitHub and Azure DevOps Files to Proceed flows (US142, US143).
- **Web retry stalls**: retry stalls until the watchdog fails the batch. Re-upload with the original Idempotency-Key is the only recovery.
- **Duplicate batch gap (US133)**: a second batch can hold the same documents after a server commit and a selection change. The client does not detect it.
- **DOCX heading parity gap**: the desktop collector does not treat DOCX heading styles as section breaks. The backend does.
