# Team Briefing

**As of**: 2026-10-30  
**Sprint**: 5 — Desktop knowledge collector (Candidate identification, AI providers, OAuth sign-in)  
**Live claims**: None (post-merge quiescence)

---

## Sprint Focus

The collector moves **local candidate identification** (US153–155, merged) and **multi-AI provider support** (US157–159, merged). US157 added stage-by-stage progress reporting but exposed a critical concurrency race. US158 landed Vertex AI (Google) as a third provider alongside Gemini and Claude. US159 added user-editable Vertex connection settings (project id, location) with a persistence pattern folded from a prerequisite story at Alpha's direction. Next wave: OAuth sign-in workflows (US151–152) and remote file selection polish (US141–142).

---

## Recent Merges & Implications

**US157: Stage-by-stage progress** (2026-10-29)
- Candidate run reports real-time progress per stage (Upload → Extract → LLM Call → Save) with per-document fraction aggregation.
- Discovered BUG003 (race condition in progress reporting) during acceptance testing. Out-of-order reports violate the UI's monotonically-increasing invariant.
- UI state machine expects monotonic progress; concurrent reporting must guarantee atomic increment + report.

**US158: Vertex AI provider** (2026-10-30)
- Third provider added: Google Vertex AI (behind `IAiProvider`) alongside Gemini direct and Claude.
- Service-account JSON credential store, bearer-token refresh (1-hour expiry), HTTP error mapping to `AiFailureKind`, transient-failure retry.

**US159: Vertex settings UI & persistence** (2026-10-30)
- User-editable Vertex project id and location in Settings dialog. Persisted to local database.
- Backend persistence gap (VertexDirectProvider had no path to user values) folded from a prerequisite story. New pattern: in-memory `IVertexSettingsState` holder seeded from persisted store at startup, updated synchronously on save. Rejected `IOptionsMonitor` file-watch due to atomic-rename write reliability concerns.
- Decision 20261030-collector-oauth-token-persistence recorded for future providers needing user-editable settings.

---

## In-Flight / Upcoming

**BUG003 blocker**: Race condition in `IdentifyCandidatesHandler.RunState` (non-atomic increment + report). Reproducible under load. Blocks US157 acceptance test `IdentifyAsync_ConcurrentProcessing_ProgressAdvancesWithoutLostUpdates`.  
**Fix options**: (a) Acquire lock before increment and hold through report; (b) Snapshot state atomically, report outside lock; (c) UI max-seen tracking (last resort).

**OAuth & credential persistence** (US151–152): Desktop sign-in via GitHub device flow, GitHub App, and Entra ID (Azure DevOps). Tokens persist encrypted in Windows Credential Manager. No contract or capsule yet; decision approved (20261013), waiting implementation.

**Gemini model fallback** (US155, decision 20261022): Config-driven model chain. When one model hits QuotaExceeded, skip it for the run and try the next. Per-minute 429 waits once then falls back. Lives in code.

**Remote token routing** (US151 merged, decision 20261013): GitHub remotes now use `IRemoteTokenResolver` and provider-agnostic `RemoteTokenAuthHandler`. Azure DevOps pending (US152).

**File selection & splitting** (US140 merged, decision 20261010): Remote intake picks files before splitting. Unselected fetch capped at 2K files; selected at 10K. Splitting bounded parallel (4 concurrent default).

---

## Known External Blockers

- Imaginext-Innovations-Private-Limited org blocks OAuth sign-in (403). Org owner must approve the GitHub OAuth App. Blocks live sign-in; code path unaffected.

---

## Decisions Shaping Next Work

1. **Candidates local, not server** (20261021): Desktop identifies candidates; server handles seeding/harvesting.
2. **OAuth persistence exception** (20261013): OAuth tokens encrypted in Credential Manager; remotes and SSH stay session-only.
3. **Vertex settings override pattern** (20261030): In-memory holder for user-editable provider settings, seeded at startup, updated synchronously on save. Applies to all future providers with user-editable connection settings.
4. **Gemini model fallback** (20261022): Config-driven quota-aware model chain.
