import { useEffect, useRef, useState } from 'react';
import { rehydrateSession } from './bootstrapSession';

export function useAuthBootstrap(): { ready: boolean } {
  const [ready, setReady] = useState(false);
  const hasRun = useRef(false);

  useEffect(() => {
    if (hasRun.current) {
      return;
    }
    hasRun.current = true;

    rehydrateSession()
      .then(() => {
        setReady(true);
      })
      .catch(() => {
        setReady(true);
      });
  }, []);

  return { ready };
}
