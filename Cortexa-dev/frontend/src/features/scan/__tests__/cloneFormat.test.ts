import { describe, it, expect } from 'vitest';
import { cloneFileName, isInProgress, latestCloneFor, shortSha, statusLabel } from '../cloneFormat';
import type { RepositoryCloneDto } from '../scanTypes';

function clone(overrides: Partial<RepositoryCloneDto> = {}): RepositoryCloneDto {
  return {
    clone_id: 'a'.repeat(32),
    provider: 'github',
    owner: 'acme',
    repository: 'api',
    branch: 'main',
    status: 'stored',
    created_at: '2026-09-30T10:00:00Z',
    updated_at: '2026-09-30T10:01:00Z',
    size_bytes: 10,
    commit_sha: 'abcdef1234567',
    error: null,
    ...overrides,
  };
}

describe('isInProgress', () => {
  it.each(['queued', 'cloning', 'uploading'] as const)('treats %s as in progress', (status) => {
    expect(isInProgress(clone({ status }))).toBe(true);
  });

  it.each(['stored', 'failed'] as const)('treats %s as finished', (status) => {
    expect(isInProgress(clone({ status }))).toBe(false);
  });
});

describe('cloneFileName', () => {
  it('names the zip after the repository, like the stored file', () => {
    expect(cloneFileName(clone({ branch: 'feature/scan' }))).toBe('api.zip');
  });

  it('uses only the repository part of an Azure project/repository handle', () => {
    expect(cloneFileName(clone({ provider: 'azure-devops', repository: 'Platform Team/web-app' }))).toBe('web-app.zip');
  });
});

describe('shortSha', () => {
  it('returns seven characters or empty', () => {
    expect(shortSha('abcdef1234')).toBe('abcdef1');
    expect(shortSha(null)).toBe('');
  });
});

describe('latestCloneFor', () => {
  it('returns the newest clone of the same repository and branch', () => {
    const older = clone({ clone_id: 'old', created_at: '2026-09-29T00:00:00Z' });
    const newer = clone({ clone_id: 'new', created_at: '2026-09-30T00:00:00Z' });
    const other = clone({ clone_id: 'other', branch: 'dev', created_at: '2026-10-01T00:00:00Z' });

    const otherOrg = clone({ clone_id: 'org', owner: 'globex', created_at: '2026-10-02T00:00:00Z' });
    const ref = { owner: 'acme', repository: 'api', branch: 'main' };

    expect(latestCloneFor([older, other, newer, otherOrg], ref)?.clone_id).toBe('new');
    expect(latestCloneFor([older], { ...ref, branch: 'dev' })).toBeUndefined();
  });
});

describe('statusLabel', () => {
  it('names the provider a save is downloading from', () => {
    expect(statusLabel(clone({ status: 'cloning' }))).toBe('Downloading from GitHub');
    expect(statusLabel(clone({ status: 'cloning', provider: 'azure-devops' }))).toBe('Downloading from Azure DevOps');
  });

  it('uses provider-neutral text for other stages', () => {
    expect(statusLabel(clone({ status: 'uploading', provider: 'azure-devops' }))).toBe('Saving');
    expect(statusLabel(clone({ status: 'stored' }))).toBe('Saved');
  });
});
