# Extraction Capacity — `max_chunks_per_unit`

Reference for sizing the extraction unit-of-work (BUG175). A code repo is ingested as a
single pipeline document with hundreds of chunks (485 for nanoGPT). Extraction must split a
document's chunks into bounded **units** — each unit a capped chunk range processed as its own
`extraction.requested` message under its own Service Bus session lock. This doc pins the cap so
one message can never exceed the session-lock lifetime again.

## Current settings

Source: `services/extraction/src/extraction/infrastructure/config/settings.py`

| Setting | Value | Line |
|---------|-------|------|
| `extraction_concurrency` | 4 | 14 |
| `session_lock_renewal_seconds` | 1800.0 | 15 |
| `model_call_total_retry_budget_seconds` | 200.0 | 12 |
| `model_max_output_tokens` | 8192 | 13 |

Service Bus `extraction-requested/extraction` subscription: `requiresSession=true`,
`lockDuration=PT1M`, `maxDeliveryCount=10`. The consumer's `AutoLockRenewer` renews only up to
`session_lock_renewal_seconds` (1800s / 30 min). Any message that needs more than that wall-clock
loses its lock, redelivers, and eventually dead-letters — the BUG175 failure mode.

## Per-call latency

| Source | Latency | Note |
|--------|---------|------|
| BUG175 live logs (grok-4.3 via model-router) | ~15s / call | `Received HTTP response headers after 15350ms` |
| `Cortexa_CapacityPlan.md` (observed, bug081) | 18–25s / chunk | at `max_tokens = 8192` |

Capacity math uses the conservative worst case: **25s per call**.

## Worst-case wall-clock formula

```
unit_wall_clock ≈ ceil(chunks_per_unit / concurrency) × per_call_latency × retry_attempts
```

- `concurrency = 4`
- `per_call_latency = 25s` (worst observed)
- `retry_attempts = 2` — the `_extract_chunk_safe` transient/parse retry loop
  (`process_extraction_request_handler.py`, 2 attempts per chunk)

This is the normal-load ceiling. A single call can additionally burn up to
`model_call_total_retry_budget_seconds = 200s` under a sustained 429 storm, but that is a
Foundry-degraded scenario that returns `TRANSIENT` and redelivers regardless; bounding the unit
keeps that redelivery cheap (one small unit re-runs, not 485 chunks).

## Chosen cap

**`max_chunks_per_unit = 25`**

```
ceil(25 / 4) × 25s × 2  =  7 × 25 × 2  =  350s
350s / 1800s ceiling  ≈  19%
```

19% of the session-lock ceiling — inside the 15–25% target band, leaving a **>5x** headroom.
The margin is deliberately large because the multipliers stack multiplicatively: latency
regression, a concurrency drop under CPU pressure, KEDA cold starts, and retry storms can each
inflate wall-clock, and a redelivery loop is a hard failure (dead-letter), not a slow success.
20% keeps all four factors survivable simultaneously.

## Resulting unit counts

| Repo | Chunks | Units at cap 25 |
|------|--------|-----------------|
| nanoGPT | 485 | `ceil(485 / 25)` = **20** |
| Paper (typical) | 10–40 | 1–2 (path unchanged in practice) |

Each unit is paper-sized, so the existing per-document fan-out and KEDA scaling behave the same
as they always have for papers.

## Unit-atomic save (why not incremental)

A unit is saved as one all-or-nothing batch: extraction fans all chunks in the unit out
concurrently, collects the candidates, then calls `save_batch` **once**, then publishes
`extraction.completed`. Saving per-chunk while the unit is still running would leave a truncated
but committed set if the pod is killed mid-unit; because candidate ids are random and the saga
dedups `extraction.completed` by `unit_index`, that truncated set would be treated as the final
result and the remaining chunks silently lost. Bounding the unit (≤25 chunks, ~350s) makes the
whole unit cheap to redo, so atomicity costs nothing: a transient chunk failure persists nothing
and the redelivered message reprocesses just that one small unit. The per-unit idempotency check
(`exists_for_unit`) is therefore safe — candidates exist for a unit only after the entire unit
committed, so a post-save redelivery re-publishes the complete set.

## Independence check (safe to split)

Confirmed each chunk is processed with no cross-chunk state, so ranging is safe:

- `_extract_all` fans chunks out under a semaphore; every chunk calls
  `ExtractCandidatesHandler.handle` independently.
- `ExtractionPromptBuilder.build(chunk, document_context)` builds one self-contained prompt per
  chunk. `document_context` is only the sanitized document **filename** (a constant title string
  per document), never derived from sibling chunks.
- `ChunkReader.get_chunks` reads and sorts chunks by `order_index`; nothing shares state across
  chunks.

A unit therefore only needs to carry its chunk range (e.g. `order_index` bounds) plus the
document id — the same `document_context` filename resolves per unit with no loss.

## Guardrail

`max_chunks_per_unit` becomes a named config value (default **25**), not a magic number. Changing
`extraction_concurrency`, the model, or `max_tokens` shifts `per_call_latency`; re-run the formula
and keep `unit_wall_clock` under ~20% of `session_lock_renewal_seconds` before shipping.
