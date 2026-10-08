# Upload recovery

This note explains what the collector server does when an upload fails, and how a client recovers.

## Rollback scope

A rollback covers the write stages only. The server writes in this order:

1. documents
2. chunks
3. provenance_maps
4. saga (batches)

If a write stage fails, the server deletes what it wrote, in reverse order: saga, provenance_maps, chunks, documents. It deletes every planned row id of each stage that was started, because writes run in parallel and a failed stage may still have saved some rows.

Rollback is best effort. It runs with its own 30 second timeout, not the request token. Each row that could not be deleted is logged by container, row id and batch id. No document content is logged.

If the saga delete fails, rollback stops. The remaining rows stay so that a replay can finish the batch.

Rollback never runs when the saga already exists. Batch ids are deterministic, so the existing saga belongs to another request for the same key. Deleting rows would destroy that batch.

If a write fails before the saga step and a saga for the batch exists by then, another request owns the batch and rollback is skipped.

## Publish failures

A publish failure never rolls back. The rows and the saga are saved. Only the notification to the pipeline failed, and some events may already be sent.

The pipeline does not retry collector documents. The recovery is to upload again with the same Idempotency-Key. The server finds the saga and publishes again the events for every document still in the Queued state. A document may receive its event twice. Consumers must accept that.

Known limit: the Retry button in the web app does not republish. A batch stuck after a publish failure stalls until the watchdog fails it. Upload again with the same key instead.

## Error codes

| Status | Error | Meaning |
| --- | --- | --- |
| 409 | `upload_in_progress` | Another request holds this key and no batch exists yet. Retry with the same key. |
| 409 | `idempotency_key_reused` | The key was used for a different upload. Use a new key. |
| 502 | `publish_failed` | Rows are saved but the pipeline was not notified. Retry with the same key. |
| 503 | `storage_unavailable` | Storage failed and the writes were rolled back. Retry with the same key. |

## Same key, different body

The server stores a fingerprint of the client request on the saga as `request_fingerprint`. A repeat request with the same key and the same fingerprint is a replay. A different fingerprint returns 409 `idempotency_key_reused`.

The check only works while the saga holds the fingerprint. A saga created before this change has none, and the orchestrator can replace the saga. In both cases the server treats the request as a replay and does not return 409.
