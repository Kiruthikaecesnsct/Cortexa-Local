# Collector Upload Publish Failures Do Not Roll Back

**Date**: 2026-10-08
**Item**: US132 (Collector server rollback and resume)
**Status**: Merged to dev (PR #12). Settled by Alpha.

## Decision

When a collector upload fails at the publish step, the server does not roll back the batch, even when zero events were published. Recovery is a replay of the same upload with the same Idempotency-Key.

## Context

US132 added reverse-order rollback for the collector batch handler. Rollback runs when a batch fails before publish. A publish failure is different. Some events may already be on the bus, and the caller may not know which ones landed. Rolling back after a partial publish would delete documents that downstream consumers may already be reading.

Zero events published still counts as a publish failure for this rule. The decision keeps one rule for every publish failure. It does not split on event count.

## Consequences

- A failed publish leaves the saga row and the written rows in place.
- The client recovers by replaying the upload with the original Idempotency-Key. The server resumes from the saga row.
- Web Retry on the collector side still stalls until the watchdog fails the batch. Re-upload is the only recovery path, as US132 already states.
- Rollback code runs only for failures before the publish step.
