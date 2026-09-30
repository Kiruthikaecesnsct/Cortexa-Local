import { useCallback, useEffect, useRef, useState } from 'react';
import { downloadBlob } from '../export/downloadBlob';
import { cloneFileName, isInProgress } from './cloneFormat';
import type { CloneApi } from './cloneApi';
import type { ScanCredentials } from './scanHttp';
import type { Loadable, RepositoryCloneDto, ScanError } from './scanTypes';

const POLL_INTERVAL_MS = 3000;

export interface CloneActionResult {
  ok: boolean;
  error?: ScanError;
}

/** Saved repositories for one provider, refreshed every few seconds while a save is running. */
export function useClones(api: CloneApi) {
  const [clones, setClones] = useState<Loadable<RepositoryCloneDto[]>>({ status: 'loading' });
  const [downloading, setDownloading] = useState<string | null>(null);
  const mounted = useRef(true);

  const refresh = useCallback(async () => {
    const result = await api.list();
    if (!mounted.current) return;
    setClones(result.ok ? { status: 'loaded', data: result.data.clones } : { status: 'error', error: result.error });
  }, [api]);

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
    async (credentials: ScanCredentials, repository: string, branch: string): Promise<CloneActionResult> => {
      const result = await api.start(credentials, repository, branch);
      if (result.ok) await refresh();
      return result.ok ? { ok: true } : { ok: false, error: result.error };
    },
    [api, refresh]
  );

  const download = useCallback(async (clone: RepositoryCloneDto): Promise<CloneActionResult> => {
    setDownloading(clone.clone_id);
    const result = await api.download(clone);
    if (mounted.current) setDownloading(null);
    if (!result.ok) return { ok: false, error: result.error };
    downloadBlob(result.data, cloneFileName(clone));
    return { ok: true };
  }, [api]);

  return { clones, downloading, refresh, start, download };
}
