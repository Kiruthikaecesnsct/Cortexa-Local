CREATE TABLE documents (
    id TEXT NOT NULL PRIMARY KEY,
    source_type TEXT NOT NULL,
    source_kind TEXT NOT NULL,
    source_path TEXT NOT NULL,
    filename TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    size_bytes INTEGER NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('pending', 'extracting', 'extracted', 'failed', 'excluded')),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    UNIQUE (source_type, source_path)
);

CREATE TABLE units (
    id TEXT NOT NULL PRIMARY KEY,
    document_id TEXT NOT NULL REFERENCES documents (id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    unit_kind TEXT NOT NULL CHECK (unit_kind IN ('page', 'section', 'file', 'module')),
    page_number INTEGER,
    section_title TEXT,
    file_path TEXT,
    start_line INTEGER,
    end_line INTEGER,
    text TEXT NOT NULL,
    token_count INTEGER NOT NULL,
    status TEXT NOT NULL,
    UNIQUE (document_id, ordinal)
);

CREATE TABLE batches (
    id TEXT NOT NULL PRIMARY KEY,
    idempotency_key TEXT NOT NULL UNIQUE,
    server_batch_id TEXT,
    batch_name TEXT NOT NULL,
    provider TEXT NOT NULL,
    model TEXT NOT NULL,
    prompt_version TEXT NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('draft', 'extracting', 'ready', 'uploading', 'uploaded', 'failed')),
    last_error TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE batch_documents (
    batch_id TEXT NOT NULL REFERENCES batches (id) ON DELETE CASCADE,
    document_id TEXT NOT NULL REFERENCES documents (id) ON DELETE CASCADE,
    PRIMARY KEY (batch_id, document_id)
);

CREATE INDEX ix_units_document_id ON units (document_id);
CREATE INDEX ix_batches_status ON batches (status);
