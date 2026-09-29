# Cortexa Service Bus Session-Key Design (US108)

Author: Alpha

## Purpose

Cortexa's job-orchestrator stamps every outbound Service Bus message with a session
id. Azure Service Bus processes a single session strictly serially on one consumer.
Until US108 the session id was always `BatchId`, so an entire batch — every
candidate's evidence and scoring work — collapsed onto one session and ran one
candidate at a time on one replica. An 8-candidate batch took 10–15 minutes; hundreds
of documents were infeasible.

This document assigns a session granularity to every pipeline event type, with the
justification tied to ordering requirements and the saga state machine. It is the
single source of truth behind `SessionKeyResolver` (job-orchestrator) and the
consumer concurrency settings.

## Granularity choices

Session scope determines what runs serially. The rule:

- **Candidate** — independent per-candidate fan-out work. Many candidates may run in
  parallel; ordering across candidates is irrelevant. Session id = `{batch_id}:{candidate_id}`.
- **Batch** — saga-control and ordering-sensitive events where the batch must be
  processed as one ordered stream. Session id = `{batch_id}`.
- **Document / None** — not needed for the MVP topic set (documented for completeness).

`batch_id` is stamped as an application property (`batch_id`) on **every** message,
candidate-scoped or not, so delete-cascade, the stuck-scanner, and observability can
always recover the batch without parsing the session id.

## Per-topic table

| # | Topic / event type | Scope | Justification |
|---|--------------------|-------|---------------|
| 1 | `batch.created` | Batch | Saga bootstrap. Fans out to per-document ingestion; must be handled once, in order, on the batch saga doc. |
| 2 | `ingestion.requested` | Batch | Drives document ingestion for the batch; ordering-sensitive against the saga's document set. Low fan-out (documents, not candidates) — parallelism win is small, ordering safety matters more. |
| 3 | `ingestion.completed` | Batch | Saga fan-in on the batch doc. Must serialize with other batch-state transitions. |
| 4 | `extraction.requested` | Batch | Per-document extraction; ordering-sensitive against the saga's expected-document tracking. |
| 5 | `extraction.completed` | Batch | Saga fan-in; sets expected-candidate counts on the saga doc. Must serialize. |
| 6 | `extraction.failed` | Batch | Saga error transition on the batch doc. |
| 7 | **`evidence.requested`** | **Candidate** | **Independent per-candidate work.** Each candidate's three-source evidence gather is self-contained; no cross-candidate ordering. Primary parallelism target. |
| 8 | `evidence.completed` | Batch | Saga fan-in: increments per-candidate completion on the batch saga doc under optimistic concurrency. Kept batch-scoped so completions serialize on ONE session — combined with the ETag-retry path this is the ordering guarantee behind `AllCandidatesResolved` firing exactly once. |
| 9 | `evidence.failed` | Batch | Saga fan-in (records failed candidate). Same reasoning as `evidence.completed`. |
| 10 | **`scoring.requested`** | **Candidate** | **Independent per-candidate work.** Each candidate's scoring (LLM + vector + patent APIs) is self-contained. Primary parallelism target. |
| 11 | `scoring.completed` | Batch | Saga fan-in on the batch doc; drives the harvesting/seeding trigger. Serialized on the batch session. |
| 12 | `scoring.failed` | Batch | Saga fan-in. Same as `scoring.completed`. |
| 13 | `harvesting.requested` | Batch | Batch-level engine trigger fired once after all candidates resolve. Single message per batch — no fan-out. |
| 14 | `harvesting.failed` | Batch | Saga error transition. |
| 15 | `seeding.requested` | Batch | Batch-level engine trigger. Single message per batch. |
| 16 | `seeding.failed` | Batch | Saga error transition. |
| 17 | `engine.completed` | Batch | Terminal saga transition on the batch doc. |

Only `evidence.requested` and `scoring.requested` are candidate-scoped — they are the
high-fan-out, order-independent legs. Everything that touches the batch saga document
stays batch-scoped so its optimistic-concurrency invariants hold.

### Why the `*.completed` fan-in events stay batch-scoped

Making `evidence.completed` / `scoring.completed` candidate-scoped would let many
completion events for the same batch hit the saga doc concurrently across replicas,
maximizing ETag conflicts. Keeping them batch-scoped serializes completions per batch
onto one session, so the parallelism happens where the *work* is (the `*.requested`
legs run across many candidate sessions), while the *bookkeeping* stays ordered. The
`SagaMessageProcessor` ETag-retry path (below) is the belt-and-suspenders backstop for
the residual conflicts that a single batch session cannot rule out (redelivery,
replica handoff).

## Recoverability of `batch_id`

Every message carries the `batch_id` application property. Candidate-scoped messages
additionally carry `candidate_id`. The session id for a candidate message is
`{batch_id}:{candidate_id}`; consumers that only have the session id can split on the
first `:` to recover the batch id, but the authoritative source is the application
property. `ServiceBusEventPublisher.PublishScheduledAsync` (the retry path) also
stamps `batch_id` derived from the session-id prefix so retried messages remain
delete/scan-visible.

## Components that read the session key — and their US108 change

| Component | Old behaviour | New behaviour |
|-----------|---------------|---------------|
| `ServiceBusEventPublisher.PublishAsync` | `SessionId = BatchId` always | `SessionId = SessionKeyResolver.Resolve(envelope).SessionId`; stamps `batch_id` (+ `candidate_id` for candidate scope) application properties. |
| `ServiceBusEventPublisher.PublishScheduledAsync` | passed-through `sessionId` only | also stamps `batch_id` derived from the session-id prefix. |
| `SessionKeyResolver` (new) | — | Central rule: candidate-scoped set = {`evidence.requested`, `scoring.requested`}; builds session id + application properties. No scattered literals. |
| `SagaMessageProcessor` | abandon on `ConcurrencyConflictException` | retry-on-ETag-conflict with reload and bounded backoff (`ConcurrencyConflictMaxAttempts`, `ConcurrencyConflictBackoffMilliseconds`); abandon only after attempts exhausted. Marks are idempotent so retries never double-count. |
| `ServiceBusBatchDeleter` | matched `message.SessionId == BatchId` | for candidate-scoped topics, walks all sessions via `AcceptNextSessionAsync` (cycle-detected by visited session ids); matches by `batch_id` application property. Batch-scoped topics keep `AcceptSessionAsync(batchId)`. |
| `ServiceBusStuckScanner` | added `message.SessionId` | resolves `batch_id` from the application property (falls back to session-id prefix). |
| Evidence / scoring Python consumers | one `NEXT_AVAILABLE_SESSION` at a time | bounded pool of `max_concurrent_sessions` worker loops, each accepting its own session with its own `AutoLockRenewer`; no shared mutable state across workers. |

## Concurrency caps (coordination with model-router)

Consumer concurrency is config-driven and bounded so the LLM burst is absorbed by the
model-router's admission control (`FoundryConcurrencyLimiter` → clean 429/503 with
`Retry-After`) rather than a `TaskCanceled` storm (the BUG106/108 lesson).

| Service | Setting | Default | Rationale |
|---------|---------|---------|-----------|
| Evidence | `consumer_max_concurrent_sessions` | 4 | Evidence fans to three sources incl. an LLM deep-research pass; kept modest to stay under model-router capacity at a single replica. |
| Scoring | `consumer_max_concurrent_sessions` | 6 | Scoring's LLM leg is lighter per call; slightly higher cap. |

Actual replica scale-out (KEDA) and the full 100-document load test are **US109**,
explicitly out of scope here. US108 delivers correct, bounded parallelism validated at
a fixed concurrency.

## Ordering guarantees preserved

- All batch-state transitions (`*.completed`, `*.failed`, `engine.completed`,
  `batch.created`, ingestion/extraction requests) remain batch-scoped → serialized per
  batch.
- `AllCandidatesResolved` still fires exactly once: completions serialize on the batch
  session, and the idempotent ETag-retry re-applies a candidate mark without
  double-counting if a redelivery races.
- Idempotency under at-least-once: candidate marks are set-based (adding an already
  present candidate id is a no-op), so duplicate/redelivered candidate messages neither
  double-count in the saga nor duplicate downstream rows.
