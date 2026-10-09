# Remote Intake Picks Files Before Splitting

**Date**: 2026-10-10
**Item**: US140 (GitHub: pick files before splitting)
**Status**: Merged to dev (PR #22).

## Decision

The remote intake wizard has five steps: Connect, Repository, Branch, Files, Proceed.

- Choosing a branch opens the Files step for both GitHub and Azure DevOps. The tree API is provider-agnostic and is reached through `Clients.For`, so the same step serves both.
- Cortexa (saved repository) and SSH skip the Files step. Their selection is null and the whole source is fetched as before.
- A user selection skips `RemoteFetchOptions.MaxFilesPerFetch` (2,000). The selection is capped by `RemoteSources:Fetch:MaxSelectedFiles` (10,000) instead.
- Splitting runs in bounded parallel. The limit is `Extraction:MaxParallelSplits` (default 4).
- The Extract display label changes from "Extracted" to "Analyzed". This applies to the Extract screen only. `DocumentStatus.Extracted` (the SQLite wire value) and the History stage "Extracted" stay as they are.

## Shared pieces for US141

US141 reuses these. Do not copy them into the Azure DevOps path.

- `RemoteFileTreeViewModel` with `FileTreeSelection` and `FileTreeBuilder` in `Collector.Application.Remote.Selection`.
- `IntakeProgressViewModel`. One card covers fetch and split, with one shared cancel token. `Begin()` is idempotent.
- `DocumentBatcher`, which flushes UI updates every 250 ms.
- The virtualized documents DataGrid and the split unit preview.

## Scope for US141

The branch-to-Files flow already exists for Azure DevOps. US141 is verification and polish for Azure DevOps. It is not a rebuild of the picker.

## WPF gotcha

A DataGrid column `Width` needs a `DataGridLength` resource. A `sys:Double` value crashes on load. This failure only showed up at runtime, not at build time. Check XAML column widths against a live window, not only a compile.

## Consequences

- Any story or test step that expects GitHub to fetch the whole repository without a Files step is stale.
- Any change to the 2,000 cap applies only to the unselected path. The selection path uses the 10,000 cap.
- Renaming the Extract label does not touch the SQLite enum or History data. Do not migrate stored `Extracted` values.
