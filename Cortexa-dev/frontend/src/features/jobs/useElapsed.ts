import { useEffect, useState } from 'react';

export function useElapsed(startedAt: string | null | undefined, frozen: boolean): number {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    if (!startedAt || frozen) {
      return;
    }

    const interval = setInterval(() => {
      setNow(Date.now());
    }, 1000);

    return () => clearInterval(interval);
  }, [startedAt, frozen]);

  if (!startedAt) {
    return 0;
  }

  const parsedStart = Date.parse(startedAt);
  if (isNaN(parsedStart)) {
    return 0;
  }

  const elapsed = now - parsedStart;
  return elapsed > 0 ? elapsed : 0;
}
