# Collector Batch Results API Contract Settlement

**Date**: 2026-10-08  
**Item**: US128 (Show Batch Progress and Candidates in History)  
**Status**: Merged; decision locks the API surface going forward.

## Summary

US128 extended the collector server's batch and results endpoints with new fields and response shapes. The changes stabilize the polling contract for the History UI and enable candidate scoring/patentability rendering.

## GET /collector/batches - New Fields

The batch list endpoint now returns:

- `stage` (PascalCase enum): Ingested, Extracted, Scored, Harvested, or Seeded. Derived from per-document saga states—lowest rank among non-failed/cancelled documents. Complete/NoCandidates count as Seeded if wants_seeding, else Harvested.
- `harvesting_count` (int): Count of documents in Harvested stage.
- `seeding_count` (int): Count of documents in Seeded stage.

Owner-scoping: Returns only the caller's batches (owner + org filter applied by server; 404 is never returned here).

## GET /collector/batches/{id}/results - Response Shape

Changed from a flat array of knowledge links to a single response object:

```
{
  batch_id: string,
  candidates: [
    {
      candidate_id: string,
      engine: string,
      title: string,
      kind: string,
      evidence_count: int,
      score: float | null,
      patentability: float | null,
      knowledge_links: [
        {
          id: string,
          kind: string,
          title: string,
          summary: string,
          source: string,
          document_id: string
        }
      ]
    }
  ]
}
```

Computation rules:

- `evidence_count`: Sum of ARRAY_LENGTH(hits) across all evidence_bundles rows for that candidate_id.
- `score`: verdict.composite_score; fallback to report.weighted_score if no verdict; null if neither.
- `patentability`: verdict.axes.Patentability.score (0-100); fallback to report.axes.Patentability; null if neither.
- `document_id`: Carried from the chunk row. Client computes server IDs via DeterministicIds.DocumentId(serverBatchId, clientDocId).

Owner-scoping: Returns 404 for batches owned by other users.

## DeterministicIds Moved to Collector.Domain

The `DeterministicIds` utility (for computing server document IDs from batch + client doc ID) now lives in `Collector.Domain` (not split across services/). This supports the client's document navigation without a round-trip.

No SQLite migration required.

## Wire Records Shared in Collector.Domain/History

The core wire records (BatchHistoryRow, CandidateRow, KnowledgeLinkRow, etc.) live in `Collector.Domain/History/` and are shared between server storage (Cosmos queries, materialized into these shapes) and client display (History screen, candidate cards).

This centralizes the contract and eliminates duplication.

## Backwards Compatibility

These are breaking changes. Clients expecting the old shapes (flat array, no stage field) will fail. Version negotiation or a new endpoint slug (e.g. `/v2/results`) is required if coexistence is needed. US128 moved both endpoints at once, so no partial transition is possible.
