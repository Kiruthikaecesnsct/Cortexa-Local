CREATE TABLE remote_files (
    id TEXT NOT NULL PRIMARY KEY,
    provider TEXT NOT NULL CHECK (provider IN ('github', 'azure_devops', 'ssh', 'cortexa_repo')),
    repo_url TEXT NOT NULL,
    repo_key TEXT NOT NULL,
    branch TEXT,
    commit_sha TEXT,
    path TEXT NOT NULL,
    blob_sha TEXT,
    size_bytes INTEGER NOT NULL,
    local_path TEXT NOT NULL,
    fetched_at TEXT NOT NULL
);

CREATE UNIQUE INDEX ux_remote_files_key ON remote_files (provider, repo_key, COALESCE(branch, ''), path);

CREATE INDEX ix_remote_files_local_path ON remote_files (local_path);
