import { useState, type ReactNode } from 'react';
import { AppShell } from '../../shared/layout/AppShell';
import { PageHeader } from '../../shared/layout/PageHeader';
import { IconCloud, IconGit, IconServer } from '../../shared/ds';
import { AzureDevOpsScanPanel } from './AzureDevOpsScanPanel';
import { GithubScanPanel } from './GithubScanPanel';
import { LocalSystemScanPanel } from './LocalSystemScanPanel';

type ScanSource = 'github' | 'azure-devops' | 'local';

interface SourceOption {
  id: ScanSource;
  title: string;
  description: string;
  icon: ReactNode;
}

const SOURCES: SourceOption[] = [
  {
    id: 'github',
    title: 'GitHub Scan',
    description: 'Connect a GitHub organization with a personal access token and browse every repository.',
    icon: <IconGit size={22} />,
  },
  {
    id: 'azure-devops',
    title: 'Azure DevOps Scan',
    description: 'Connect an Azure DevOps organization with a personal access token and browse every repository.',
    icon: <IconCloud size={22} />,
  },
  {
    id: 'local',
    title: 'Local File System Scan',
    description: 'Connect to a VM over SSH with its IP address, key, and a folder path, then browse its files.',
    icon: <IconServer size={22} />,
  },
];

function SourceCard({ option, selected, onSelect }: { option: SourceOption; selected: boolean; onSelect: () => void }) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={selected}
      onClick={onSelect}
      style={{
        display: 'flex',
        gap: 14,
        alignItems: 'flex-start',
        padding: 20,
        textAlign: 'left',
        cursor: 'pointer',
        fontFamily: 'var(--font-body)',
        color: 'var(--text-primary)',
        background: 'var(--surface-card)',
        borderRadius: 'var(--radius-lg)',
        border: `1px solid ${selected ? 'var(--accent-primary)' : 'var(--border-subtle)'}`,
        boxShadow: selected ? 'var(--focus-ring)' : 'var(--shadow-xs)',
      }}
    >
      <span style={{ color: 'var(--accent-primary)', display: 'inline-flex' }}>{option.icon}</span>
      <span style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
        <span style={{ fontWeight: 600, fontSize: 'var(--text-md)' }}>{option.title}</span>
        <span style={{ color: 'var(--text-muted)', fontSize: 'var(--text-sm)' }}>{option.description}</span>
      </span>
    </button>
  );
}

function SectionLabel({ id, children }: { id: string; children: ReactNode }) {
  return (
    <h2
      id={id}
      style={{
        margin: 0,
        fontSize: 11,
        fontWeight: 600,
        letterSpacing: '0.06em',
        textTransform: 'uppercase',
        color: 'var(--text-muted)',
      }}
    >
      {children}
    </h2>
  );
}

export function ScanPage() {
  const [source, setSource] = useState<ScanSource>('github');

  return (
    <AppShell>
      <PageHeader title="Scan" subtitle="Pick a source to scan for files and folders." />
      <section aria-labelledby="scan-source-label" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        <SectionLabel id="scan-source-label">Source</SectionLabel>
        <div role="tablist" aria-labelledby="scan-source-label" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: 16 }}>
          {SOURCES.map((option) => (
            <SourceCard key={option.id} option={option} selected={option.id === source} onSelect={() => setSource(option.id)} />
          ))}
        </div>
      </section>
      <section aria-labelledby="scan-work-label" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        <SectionLabel id="scan-work-label">
          {source === 'github' ? 'GitHub' : source === 'azure-devops' ? 'Azure DevOps' : 'Local file system'}
        </SectionLabel>
        {source === 'github' && <GithubScanPanel />}
        {source === 'azure-devops' && <AzureDevOpsScanPanel />}
        {source === 'local' && <LocalSystemScanPanel />}
      </section>
    </AppShell>
  );
}
