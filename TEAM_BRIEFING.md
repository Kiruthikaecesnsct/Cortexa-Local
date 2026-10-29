# Team briefing

Rewritten whole from the live files under `context/`, `contracts/`, and `decisions/`. Hard cap about 1500 tokens. Do not append by hand.

Narrative only. Who is working on what right now is not here. That lives in `ACTIVE.md`, generated from the claim cards on every sync.

_Last regenerated: 2026-10-29_

## Current focus

The desktop collector is the active surface. It pulls source files from four remote sources (GitHub, Azure DevOps, SSH/SFTP, and the saved Cortexa repository), splits them, and uploads them for knowledge extraction. All collector code stays inside `Cortexa-dev/desktop/collector`.

US157 (show real stage-by-stage progress on Data Connectors while a file is processed) merged into dev via PR #34. It adds config-driven per-stage progress reporting to candidate identification runs. The collector now reports progress for upload/read, extraction, LLM call, and save stages separately. Each stage has a configurable weight, validated at startup. The progress bar shows a stage caption and respects final-state rules so cancel and fail never report 100%. US157 also fixed BUG003 - a race condition in `IdentifyCandidatesHandler` where out-of-order progress reports from parallel document processing could cause the UI to show regressing counts. The fix serializes the completed-count increment and progress report atomically.

US153 (invention candidates from whole documents with Gemini) and US154 (candidate pass chained into Process, shown in Review) are merged. Candidates stay local. Nothing new is uploaded.

US155 (Gemini model fallback on quota) is merged. The model list is set by `Ai:Gemini:FallbackModels`. A short per-minute 429 waits once before falling back.

## Open follow-ups

- US142 and US143 are manual end-to-end checks; not code gaps.
- The Process screen is shared with US150 (Extract renamed to Process). Confirm the current Process view before editing it further.
- Process runs knowledge and candidate passes back-to-back, so Gemini concurrency is not doubled. Maintain this.
- A 400 or 413 on a whole-document request can mark every Gemini key as Rejected in Settings. No bug is filed yet.
- Confirm Gemini keys are on a paid tier before long runs.
- Gemini fallback has no cooldown. An exhausted model stays skipped for the run.
- US151 and US152 (sign-in) depend on OAuth token persistence decision below. Accepted but not yet implemented.

## In-flight contracts

None. Candidate types are in-process only. Stage definitions and weights are config-driven via `Ai:CandidateStages`.

## Recent decisions

- **Stage-by-stage progress on candidate identification** (2026-10-29, merged in US157). Desktop collector reports progress per stage (upload/read, extraction, LLM, save). Stage weights configured in `Ai:CandidateStages`. Final-state rules prevent cancel/fail from reaching 100%.
- **Gemini model fallback is config-driven** (2026-10-22, active). Models tried in `Ai:Gemini:FallbackModels` order. Exhausted models skipped for the run. Short per-minute 429 waits once before fallback.
- **Candidates identified on the desktop** (2026-10-21, active). Results stay local. Desktop shows results, uploads nothing new.
- **Collector OAuth token persistence** (2026-10-13, accepted, not implemented). Approved exception for OAuth tokens only. PATs stay out.
- **Collector remote token seam** (2026-10-13, merged). Remote sources read tokens through one seam. US152 depends on it.
- **Remote HTTP timeouts and retry limits** (2026-10-10, merged). Unified timeout and retry policy for GitHub and Azure DevOps path.

Older decisions (batch results, remote files table, PAT handling, upload rollback, saga fingerprint) are merged history. Read on demand if a task references them.
