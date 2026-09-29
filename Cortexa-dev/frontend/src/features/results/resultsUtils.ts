export type ScoreBand = 'high' | 'medium' | 'low';

// Scores are 0-100 scale — do not multiply by 100 for display.
export function scoreBand(v: number): ScoreBand {
  if (v >= 70) return 'high';
  if (v >= 40) return 'medium';
  return 'low';
}
