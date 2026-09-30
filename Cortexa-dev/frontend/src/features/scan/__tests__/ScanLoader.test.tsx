import { act, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ScanLoader } from '../ScanLoader';

const SLOW_AFTER_MS = 1000;

describe('ScanLoader', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('shows the title and message and marks itself busy', () => {
    render(<ScanLoader title="Loading branches" message="Reading branches" />);

    expect(screen.getByText('Loading branches')).toBeTruthy();
    expect(screen.getByText('Reading branches')).toBeTruthy();
    expect(screen.getByText('Loading branches').closest('[aria-busy="true"]')).not.toBeNull();
  });

  it('shows the slow hint only after the delay', () => {
    render(<ScanLoader title="t" message="m" slowHint="Taking a while" slowAfterMs={SLOW_AFTER_MS} />);

    expect(screen.queryByText('Taking a while')).toBeNull();
    act(() => {
      vi.advanceTimersByTime(SLOW_AFTER_MS);
    });
    expect(screen.getByText('Taking a while')).toBeTruthy();
  });
});
