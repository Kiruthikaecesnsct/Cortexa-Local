import { useMemo, useState } from 'react';
import { Button, IconChevronRight, IconFile, IconFolder, IconSearch, InlineMessage, Input } from '../../shared/ds';
import { formatBytes } from '../upload/uploadValidation';
import { ScanLoader } from './ScanLoader';
import type { DirectoryEntryDto, DirectoryListingDto, Loadable } from './scanTypes';

function breadcrumbSegments(path: string): { label: string; target: string }[] {
  const parts = path.split('/').filter(Boolean);
  const segments = [{ label: '/', target: '/' }];
  let acc = '';
  for (const part of parts) {
    acc += `/${part}`;
    segments.push({ label: part, target: acc });
  }
  return segments;
}

function Breadcrumb({ path, onNavigate }: { path: string; onNavigate: (path: string) => void }) {
  const segments = breadcrumbSegments(path);
  return (
    <nav aria-label="Current folder" style={{ display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: 2, minWidth: 0 }}>
      {segments.map((segment, i) => (
        <span key={segment.target} style={{ display: 'inline-flex', alignItems: 'center', gap: 2 }}>
          {i > 0 && <IconChevronRight size={12} />}
          <button
            type="button"
            onClick={() => onNavigate(segment.target)}
            disabled={i === segments.length - 1}
            style={{
              border: 'none',
              background: 'transparent',
              padding: '2px 4px',
              cursor: i === segments.length - 1 ? 'default' : 'pointer',
              fontFamily: 'var(--font-mono)',
              fontSize: 'var(--text-sm)',
              fontWeight: i === segments.length - 1 ? 600 : 500,
              color: i === segments.length - 1 ? 'var(--text-primary)' : 'var(--accent-primary)',
            }}
          >
            {segment.label}
          </button>
        </span>
      ))}
    </nav>
  );
}

function EntryIcon({ entry }: { entry: DirectoryEntryDto }) {
  if (entry.type === 'directory') return <span style={{ color: 'var(--accent-primary)', display: 'inline-flex' }}><IconFolder size={15} /></span>;
  return <span style={{ color: 'var(--text-muted)', display: 'inline-flex' }}><IconFile size={15} /></span>;
}

function EntryRow({ entry, onOpen }: { entry: DirectoryEntryDto; onOpen: (entry: DirectoryEntryDto) => void }) {
  const isFolder = entry.type === 'directory';
  return (
    <li role="treeitem" aria-selected={false}>
      <button
        type="button"
        onClick={() => isFolder && onOpen(entry)}
        disabled={!isFolder}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 8,
          width: '100%',
          padding: '6px 10px',
          border: 'none',
          background: 'transparent',
          borderRadius: 'var(--radius-sm)',
          fontFamily: 'var(--font-mono)',
          fontSize: 'var(--text-sm)',
          color: 'var(--text-primary)',
          textAlign: 'left',
          cursor: isFolder ? 'pointer' : 'default',
        }}
      >
        <EntryIcon entry={entry} />
        <span style={{ flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{entry.name}</span>
        <span style={{ color: 'var(--text-muted)', fontSize: 'var(--text-xs)' }}>
          {entry.type === 'directory' ? '' : entry.size !== null ? formatBytes(entry.size) : ''}
        </span>
      </button>
    </li>
  );
}

function EntryList({ entries, search, onOpen }: { entries: DirectoryEntryDto[]; search: string; onOpen: (entry: DirectoryEntryDto) => void }) {
  const needle = search.trim().toLowerCase();
  const visible = needle ? entries.filter((e) => e.name.toLowerCase().includes(needle)) : entries;
  if (visible.length === 0) {
    return <div style={{ color: 'var(--text-muted)', padding: 16 }}>{needle ? `Nothing matches "${search}".` : 'This folder is empty.'}</div>;
  }
  return (
    <ul role="tree" aria-label="Folder contents" style={{ listStyle: 'none', margin: 0, padding: '6px 0', maxHeight: 480, overflow: 'auto' }}>
      {visible.map((entry) => (
        <EntryRow key={entry.path} entry={entry} onOpen={onOpen} />
      ))}
    </ul>
  );
}

interface RemoteFileBrowserProps {
  listing: Loadable<DirectoryListingDto>;
  currentPath: string;
  onNavigate: (path: string) => void;
  onUp: () => void;
  onRetry: () => void;
}

export function RemoteFileBrowser({ listing, currentPath, onNavigate, onUp, onRetry }: RemoteFileBrowserProps) {
  const [search, setSearch] = useState('');
  const counts = useMemo(() => {
    if (listing.status !== 'loaded') return undefined;
    const folders = listing.data.entries.filter((e) => e.type === 'directory').length;
    return { folders, files: listing.data.entries.length - folders };
  }, [listing]);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 16, flexWrap: 'wrap' }}>
        <div style={{ flex: 1, minWidth: 0 }}>
          <Breadcrumb path={currentPath} onNavigate={onNavigate} />
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          {counts && (
            <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-muted)', whiteSpace: 'nowrap' }}>
              {counts.folders} folders · {counts.files} files
            </span>
          )}
          <Button size="sm" variant="secondary" onClick={onUp} disabled={currentPath === '/' || listing.status === 'loading'}>
            Up
          </Button>
        </div>
      </div>
      {listing.status === 'loaded' && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          {listing.data.truncated && (
            <InlineMessage variant="info">This folder is very large; only the first entries are shown.</InlineMessage>
          )}
          <Input
            aria-label="Search this folder"
            placeholder="Search this folder"
            leading={<span style={{ color: 'var(--text-muted)', display: 'inline-flex' }}><IconSearch size={16} /></span>}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          <div style={{ border: '1px solid var(--border-subtle)', borderRadius: 'var(--radius-md)', background: 'var(--surface-sunken)' }}>
            <EntryList entries={listing.data.entries} search={search} onOpen={(entry) => onNavigate(entry.path)} />
          </div>
        </div>
      )}
      {listing.status === 'error' && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10, alignItems: 'flex-start' }}>
          <InlineMessage variant="danger" correlationId={listing.error.correlationId}>
            {listing.error.message}
          </InlineMessage>
          <Button size="sm" variant="secondary" onClick={onRetry}>
            Try again
          </Button>
        </div>
      )}
      {(listing.status === 'loading' || listing.status === 'idle') && (
        <ScanLoader title="Reading folder" message={`Listing files in ${currentPath}.`} slowHint="Large folders can take a few seconds. Hang tight." />
      )}
    </div>
  );
}
