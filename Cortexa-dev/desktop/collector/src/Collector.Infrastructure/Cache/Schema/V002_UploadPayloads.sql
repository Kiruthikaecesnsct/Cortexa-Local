CREATE TABLE batch_payloads (
    batch_id TEXT NOT NULL PRIMARY KEY REFERENCES batches (id) ON DELETE CASCADE,
    body BLOB NOT NULL,
    body_sha256 TEXT NOT NULL,
    created_at TEXT NOT NULL
);

ALTER TABLE batches ADD COLUMN replaced_at TEXT;

CREATE INDEX ix_batches_retryable ON batches (status, replaced_at);
