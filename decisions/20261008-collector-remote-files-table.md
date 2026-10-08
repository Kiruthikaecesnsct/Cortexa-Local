# Collector Remote Files Table Is Source-Agnostic

**Date**: 2026-10-08
**Item**: US134 (GitHub and Azure DevOps collectors)
**Status**: Merged to dev (PR #14).

## Decision

The desktop collector's V003 migration adds one `remote_files` table for every remote source. It is not GitHub-specific or Azure DevOps-specific.

Columns: `provider`, `repo_url`, `repo_key`, `branch`, `commit_sha`, `path`, `blob_sha`, `size_bytes`, `local_path`, `fetched_at`.

- `provider` is checked against `github`, `azure_devops`, `ssh`, `cortexa_repo`.
- Unique on `provider`, `repo_key`, `COALESCE(branch, '')`, `path`.
- Index on `local_path`, which joins to `documents.source_path`.

A fetched file skips the download when the cached `blob_sha` matches the remote blob SHA.

## Context

Each remote source needs the same cache and the same document link. A table per provider would copy the logic. One table keeps the cache check in one place.

## Consequences

- US135 (SSH) must add V004 and reuse this table. It uses `provider = ssh`, leaves `branch` and `commit_sha` null, and stores a content hash in `blob_sha`.
- Any new remote source follows the same pattern. It does not get its own cache table.
- The null `branch` is handled by the `COALESCE` in the unique key. Do not drop that expression.
