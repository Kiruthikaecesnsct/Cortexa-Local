# Team Briefing

**As of**: 2026-10-30  
**Sprint**: 5 — Desktop knowledge collector (Candidate identification, AI providers, OAuth sign-in)  
**Live claims**: None (post-merge quiescence)

---

## Sprint Focus

The collector moves **local candidate identification** (US153–155, merged) and **multi-AI provider support** (US157–158, merged). US157 added stage-by-stage progress reporting with a critical concurrency race discovered during review. US158 landed Vertex AI (Google) as a third provider alongside Gemini and Claude, with credential management and service-account token refresh. Both merges unblock the next wave: OAuth sign-in workflows (US151–152) and remote file selection polish (US141–142).

---

## Recent Merges & Implications

**US157: Stage-by-stage progress** (2026-10-29)
- The candidate run now reports real-time progress: Upload → Extract → LLM Call → Save, with per-document fraction aggregation.
- Discovered BUG003 (race condition in progress reporting) during acceptance testing. Intermittent out-of-order progress updates violate the UI's monotonically-increasing invariant. A lock-based or snapshot-based fix required.
- UI state machine expects monotonic progress; concurrent reporting must guarantee atomic increment + report.

**US158: Vertex AI provider** (2026-10-30)
- Third provider added: Google Vertex AI (behind `IAiProvider`) alongside Gemini direct and Claude.
- Service-account JSON credential store, bearer-token refresh with 1-hour expiry, HTTP error mapping to `AiFailureKind`, transient-failure retry.
- Fixed startup crash from empty default ProjectId during review. Code review sign-off complete.

---

## In-Flight / Upcoming

**BUG003 blocker**: Race condition race condition in `IdentifyCandidatesHandler.RunState` (non-atomic increment + report). Reproducible under load. Blocks US157 acceptance test `IdentifyAsync_ConcurrentProcessing_ProgressAdvancesWithoutLostUpdates`.  
**Root**: `Interlocked.Increment` followed by separate `progress?.Report(...)` call; under parallelism > 1, reports arrive out-of-order.  
**Options**: (a) Acquire lock before increment and hold through report; (b) Snapshot state atomically, report outside lock; (c) UI max-seen tracking (last resort).

**OAuth & credential persistence** (US151–152): Desktop sign-in via GitHub device flow, GitHub App, and Entra ID (Azure DevOps). Tokens persist encrypted in Windows Credential Manager (exception to the session-only PAT rule). Remote PATs and SSH passphrases remain session-only. No contract or capsule yet; decision (20261013-collector-oauth-token-persistence) approved, waiting implementation.

**Gemini fallback** (US155, decision 20261022): Config-driven model chain. When one Gemini model hits QuotaExceeded, skip it for the rest of the run and try the next in `Ai:Gemini:FallbackModels`. A per-minute 429 waits once then fallback. Lives in code, no spec change.

**Remote token seam** (US151 merged, decision 20261013): GitHub remotes now route tokens through `IRemoteTokenResolver` and provider-agnostic `RemoteTokenAuthHandler`. Azure DevOps still uses `PatAuthHandler` until US152. A new provider adds a token source strategy and wires it in the resolver.

**File selection before split** (US140 merged, decision 20261010): Remote intake (GitHub and Azure DevOps) now picks files before splitting. Unselected fetch capped at 2,000 files; selected at 10,000. Splitting runs bounded parallel (default 4 concurrent). Extract label on screen changed to "Analyzed" (not the SQLite enum or History stage name). US141 (Azure DevOps polish) shares these components.

---

## Known External Blockers

- Imaginext-Innovations-Private-Limited org blocks OAuth sign-in (403). Org owner must approve the GitHub OAuth App under third-party access policy and enforce secure 2FA. Blocks live sign-in to that org; code path unaffected.

---

## Contracts & Data Shapes

None in-flight. US128's batch results API and History wire shapes (decision 20261008-collector-batch-results) are locked and merged.

---

## Decisions Shaping Next Work

1. **Candidates local, not server** (20261021): Desktop identifies candidates, does not upload them. Seeding/harvesting remain server-driven.
2. **OAuth persistence exception** (20261013): OAuth tokens encrypted and persisted; remote PATs and SSH stay session-only.
3. **Atomic progress reporting**: BUG003 fix must synchronize increment and report (decision pending).
