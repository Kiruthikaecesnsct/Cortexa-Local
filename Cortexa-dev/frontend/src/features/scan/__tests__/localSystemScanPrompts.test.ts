import { describe, it, expect } from 'vitest';
import { promptFor } from '../localSystemScanPrompts';
import {
  INITIAL_LOCAL_SCAN_STATE,
  localScanReducer,
  type LocalScanAction,
  type LocalScanState,
} from '../localSystemScanState';
import type { DirectoryListingDto } from '../scanTypes';

const listing: DirectoryListingDto = {
  path: '/home/ubuntu',
  entries: [
    { name: 'app', path: '/home/ubuntu/app', type: 'directory', size: null, modified_at: null },
    { name: 'readme.md', path: '/home/ubuntu/readme.md', type: 'file', size: 120, modified_at: null },
  ],
  truncated: false,
};

function run(...actions: LocalScanAction[]): LocalScanState {
  return actions.reduce(localScanReducer, INITIAL_LOCAL_SCAN_STATE);
}

describe('promptFor (Local file system)', () => {
  it('asks the user to connect first', () => {
    expect(promptFor(INITIAL_LOCAL_SCAN_STATE)).toMatchObject({ stepLabel: 'Step 1 of 2', title: 'Connect to a VM' });
  });

  it('stays on the connect prompt while the initial connection is in flight', () => {
    const state = run({ type: 'connectStarted', path: '/home/ubuntu' });

    expect(promptFor(state)).toMatchObject({ stepLabel: 'Step 1 of 2', title: 'Connect to a VM' });
  });

  it('reports the item count once connected', () => {
    const state = run(
      { type: 'connectStarted', path: '/home/ubuntu' },
      { type: 'connectSucceeded', data: listing }
    );

    expect(promptFor(state)).toMatchObject({ stepLabel: 'Step 2 of 2', title: 'Browse files' });
    expect(promptFor(state).message).toContain('Showing 2 items in /home/ubuntu');
  });

  it('reports singular item count for one entry', () => {
    const single: DirectoryListingDto = { ...listing, entries: [listing.entries[0]!] };
    const state = run(
      { type: 'connectStarted', path: '/home/ubuntu' },
      { type: 'connectSucceeded', data: single }
    );

    expect(promptFor(state).message).toContain('Showing 1 item in /home/ubuntu');
  });

  it('tracks the path being navigated to while loading the next folder', () => {
    const state = run(
      { type: 'connectStarted', path: '/home/ubuntu' },
      { type: 'connectSucceeded', data: listing },
      { type: 'navigateStarted', path: '/home/ubuntu/app' }
    );

    expect(promptFor(state).message).toContain('Loading /home/ubuntu/app');
  });
});
