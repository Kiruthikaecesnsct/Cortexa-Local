# Team briefing

Rewritten whole from the live files under `context/`, `contracts/`, and `decisions/`. Hard cap about 1500 tokens. Do not append by hand.

Narrative only. Who is working on what right now is not here. That lives in `ACTIVE.md`, generated from the claim cards on every sync.

_Last regenerated: 2026-10-22_

## Current focus

The desktop collector is the active surface. It pulls source files from four remote sources (GitHub, Azure DevOps, SSH/SFTP, and the saved Cortexa repository), splits them, and uploads them for knowledge extraction. All collector code stays inside `Cortexa-dev/desktop/collector`.

Sprint 5 is in progress. US153 (invention candidates from whole documents with Gemini) and US154 (candidate pass chained into Process, candidates shown in Review) are merged. Candidates stay local. Nothing new is uploaded.

US155 (Gemini model fallback on quota) is merged through PR #31. The model list is set by `Ai:Gemini:FallbackModels`, and a short per-minute 429 waits once before falling back. Its capsule is archived.

## Open follow-ups

- US142 and US143 are still open as manual end-to-end checks. They are not code gaps.
- The Process screen is shared with US150 (Extract renamed to Process). Check the current Process view before starting any further edit there.
- Process runs the knowledge pass and the candidate pass one after the other on a selection, so Gemini concurrency is not doubled. Keep it that way.
- A whole-document 400 or 413 can mark every Gemini key as Rejected in Settings. A follow-up bug is not filed yet.
- Confirm the Gemini keys are on a paid tier before a long run.
- Gemini fallback has no cooldown timer. An exhausted model stays skipped for the rest of the run.
- US151 and US152 (sign-in work) rely on the OAuth token persistence decision below. It is accepted but not yet implemented.

## In-flight contracts

None in `contracts/`. Candidate types are in-process only and recorded under the decisions below.

## Recent decisions

- **Gemini model fallback is config-driven** (2026-10-22, active). Models are tried in `Ai:Gemini:FallbackModels` order. Exhausted models are skipped for the run. A short per-minute 429 waits once before falling back.
- **Candidates identified on the desktop** (2026-10-21, active). Candidate identification runs in the collector. Results stay local. The desktop shows results and uploads nothing new.
- **Collector OAuth token persistence** (2026-10-13, accepted, not implemented). Approved exception to US138 for OAuth tokens only. PATs stay out of persistence.
- **Collector remote token seam** (2026-10-13, merged). Remote sources read tokens through one seam. US152 depends on it.
- **Remote HTTP timeouts and retry limits** (2026-10-10, merged). One timeout and retry policy for the shared GitHub and Azure DevOps HTTP path.
- **Remote intake picks files before splitting** (2026-10-10, merged). Applies to GitHub and Azure DevOps.

Older decisions (collector batch results, source-agnostic remote files table, PAT handling, upload rollback and retry, saga request fingerprint) are merged history. Their code is the source of truth now. PAT handling is partly superseded by US138. Read the file on demand if a task references it.
