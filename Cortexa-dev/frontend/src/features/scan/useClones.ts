import { useCallback, useEffect, useRef, useState } from 'react';
import { downloadBlob } from '../export/downloadBlob';
import { cloneFileName, isInProgress } from './cloneFormat';
import { downloadClone, listClones, startClone } from './scanRepository';
import type { GithubCredentials, Loadable, RepositoryCloneDto, ScanError } from './scanTypes';

const POLL_INTERVAL_MS = 3000;

export interface CloneActionResult {
  ok: boolean;
  error?: ScanError;
}

export function useClones() {
  const [clones, setClones] = useState<Loadable<RepositoryCloneDto[]>>({ status: 'loading' });
  const [downloading, setDownloading] = useState<string | null>(null);
  const mounted = useRef(true);

  const refresh = useCallback(async () => {
    const result = await listClones();
    if (!mounted.current) return;
    setClones(result.ok ? { status: 'loaded', data: result.data.clones } : { status: 'error', error: result.error });
  }, []);

  useEffect(() => {
    mounted.current = true;
    void refresh();
    return () => {
      mounted.current = false;
    };
  }, [refresh]);

  const anyInProgress = clones.status === 'loaded' && clones.data.some(isInProgress);
  useEffect(() => {
    if (!anyInProgress) return undefined;
    const timer = window.setInterval(() => void refresh(), POLL_INTERVAL_MS);
    return () => window.clearInterval(timer);
  }, [anyInProgress, refresh]);

  const start = useCallback(
    async (credentials: GithubCredentials, repository: string, branch: string): Promise<CloneActionResult> => {
      const result = await startClone(credentials, repository, branch);
      if (result.ok) await refresh();
      return result.ok ? { ok: true } : { ok: false, error: result.error };
    },
    [refresh]
  );

  const download = useCallback(async (clone: RepositoryCloneDto): Promise<CloneActionResult> => {
    setDownloading(clone.clone_id);
    const result = await downloadClone(clone);
    if (mounted.current) setDownloading(null);
    if (!result.ok) return { ok: false, error: result.error };
    downloadBlob(result.data, cloneFileName(clone));
    return { ok: true };
  }, []);

  return { clones, downloading, refresh, start, download };
}
