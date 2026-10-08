# Collector Upload Retry Resends Stored Bytes

**Date**: 2026-10-08
**Item**: US133 (Upload failure handling and resume)
**Status**: Merged to dev (PR #13).

## Decision

The desktop collector stores each upload request as serialized bytes, with its Idempotency-Key, in the `batch_payloads` table (V002). A retry resends those exact bytes under the same key. It never rebuilds the body. The server fingerprints the body (US132), so a rebuilt body would fail the replay check. Retry makes no AI calls.

Status codes on upload:

- 409 `upload_in_progress` is retryable.
- 409 `idempotency_key_reused` is final. Do not retry it.

Changing the selection creates a new body and a new key. The old failed batch is marked replaced (`batches.replaced_at`), and only after the new upload is persisted.

## Context

US132 made the server reject a repeated key with a different body. A retry that rebuilt the request could send different bytes, so it would hit that rejection. Storing the bytes avoids this.

## Consequences

- Retry is a byte-for-byte resend. Any change to the selection goes through a new upload.
- Marking the old batch replaced only after the new one is saved keeps the old batch if the save fails.
- Known gap: if the server committed a failed upload, and the user then changes the selection (new key), the server can hold a second batch for the same documents. The client does not detect this yet.
