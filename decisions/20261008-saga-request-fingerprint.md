# Saga Row Request Fingerprint for Idempotency Keys

**Date**: 2026-10-08
**Item**: US132 (Collector server rollback and resume)
**Status**: Merged to dev (PR #12). Settled by Alpha.

## Decision

The saga row, which the collector shares with the orchestrator, carries an additive nullable `request_fingerprint` column. It holds the SHA-256 of the client request.

Replay rules for a repeated Idempotency-Key:

- Same key, same fingerprint: replay the stored result.
- Same key, different fingerprint: return 409 with error code `idempotency_key_reused`.
- Same key, row has no fingerprint: replay the stored result instead of returning 409.

## Context

Parallel UpsertAllAsync writes mean a retried request can carry a different body than the original. The server must reject that rather than resume the wrong batch. Rows written before this change have no fingerprint. Treating them as a conflict would break every in-flight batch at deploy time, so they replay.

## Consequences

- The column is additive and nullable. No backfill is needed.
- The orchestrator reads the same container. It ignores the new column. Its behavior does not change.
- Clients that reuse a key with a changed body now get 409. Clients that reuse a key with the same body see no change.
