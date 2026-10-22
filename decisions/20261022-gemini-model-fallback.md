# Gemini model fallback is config-driven

- Date: 2026-10-22
- Made by: US155 (hand-authored, approved by Alpha)
- Status: active

## Decision

The desktop collector tries Gemini models in the order set by `Ai:Gemini:FallbackModels`. When every key for a model returns QuotaExceeded, that model is marked exhausted for the rest of the run and skipped by later calls. A short per-minute 429 waits once and retries the same model before any fallback. The primary model stays `Ai:Gemini:Model`.

## Why

A quota failure on one model should not fail a whole document while another configured model still has quota. Config keeps the order and the list under user control. The alternative rejected was a hard-coded model chain, which would need a code change for each new model. A PerDay quota or a long retry delay is not worth waiting for, so those fall back at once.

## Affects

- `desktop/collector` only. `services/`, `contracts/` and `frontend/` are not changed by this decision.
- `Ai:Gemini:FallbackModels` defaults to an empty list in code. `appsettings.json` ships `["gemini-3.6-flash"]`. Each entry must be a Gemini model in `Ai:ModelCatalog`, and duplicates are rejected.
- `Ai:Gemini:MaxQuotaRetryDelaySeconds` (default 60) sets the longest wait the provider accepts.
- The exhausted-model state is per run and is not persisted. A cooldown timer is not implemented.
- Future work that changes the fallback order or adds a timed cooldown needs a new decision.
