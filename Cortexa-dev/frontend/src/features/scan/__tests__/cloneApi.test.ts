import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { RepositoryCloneDto } from '../scanTypes';

const postScan = vi.fn();
const getScan = vi.fn();
const getScanBlob = vi.fn();

vi.mock('../scanHttp', () => ({
  credentialsBody: (c: { orgUrl: string; pat: string }) => ({ org_url: c.orgUrl.trim(), pat: c.pat.trim() }),
  postScan: (...args: unknown[]) => postScan(...args),
  getScan: (...args: unknown[]) => getScan(...args),
  getScanBlob: (...args: unknown[]) => getScanBlob(...args),
}));

const { azureDevOpsCloneApi, githubCloneApi } = await import('../cloneApi');

const azureClone: RepositoryCloneDto = {
  clone_id: 'x',
  provider: 'azure-devops',
  owner: 'contoso',
  repository: 'Platform Team/api',
  branch: 'feature/x',
  status: 'stored',
  created_at: '',
  updated_at: '',
  size_bytes: 1,
  commit_sha: null,
  error: null,
};

describe('clone APIs', () => {
  beforeEach(() => vi.clearAllMocks());

  it('posts saves to each provider base path with trimmed credentials', async () => {
    await githubCloneApi.start({ orgUrl: ' https://github.com/acme ', pat: ' tok ' }, 'api', 'main');
    await azureDevOpsCloneApi.start({ orgUrl: 'https://dev.azure.com/contoso', pat: 'tok' }, 'Platform/api', 'main');

    expect(postScan.mock.calls[0]?.slice(0, 2)).toEqual([
      '/scan/github/clones',
      { org_url: 'https://github.com/acme', pat: 'tok', repository: 'api', branch: 'main' },
    ]);
    expect(postScan.mock.calls[1]?.[0]).toBe('/scan/azure-devops/clones');
  });

  it('lists from the provider base path', async () => {
    await azureDevOpsCloneApi.list();

    expect(getScan.mock.calls[0]?.[0]).toBe('/scan/azure-devops/clones');
  });

  it('encodes owner, project/repository and branch in the download query', async () => {
    await azureDevOpsCloneApi.download(azureClone);

    const url = new URL(getScanBlob.mock.calls[0]?.[0] as string, 'http://x');
    expect(url.pathname).toBe('/scan/azure-devops/clones/download');
    expect(Object.fromEntries(url.searchParams)).toEqual({
      owner: 'contoso',
      repository: 'Platform Team/api',
      branch: 'feature/x',
    });
  });
});
