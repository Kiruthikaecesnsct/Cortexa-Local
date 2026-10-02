import React, { useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { AppShell } from '../../shared/layout/AppShell';
import { PageHeader, SectionCard } from '../../shared/layout/PageHeader';
import { Button, Input, Select, Modal, IconGit, IconUpload, IconFolder, Badge } from '../../shared/ds';
import { useToast } from '../../shared/ds/Toast';
import { maxSavedRepositoriesPerBatch } from '../../core/config/env';
import { SavedRepositoriesCard } from './SavedRepositoriesCard';
import { readSavedRepositoriesFromState } from './savedRepositories';
import { useUploadStore } from './useUploadStore';
import { formatBytes, validateGitParams } from './uploadValidation';
import type { AnalysisEngine, GitHost, SeedCorpusDomain } from './uploadTypes';

const ENGINE_OPTIONS: { value: AnalysisEngine; label: string }[] = [
  { value: 'harvesting', label: 'Patent Harvesting' },
  { value: 'seeding', label: 'Patent Seeding' },
];

const CORPUS_OPTIONS: { value: SeedCorpusDomain; label: string }[] = [
  { value: 'ml_ai', label: 'Machine Learning / AI' },
  { value: 'biotech', label: 'Biotech' },
  { value: 'semiconductors', label: 'Semiconductors' },
  { value: 'telecoms', label: 'Telecom / RF' },
];

const GIT_PROVIDERS: { value: GitHost; label: string }[] = [
  { value: 'github', label: 'GitHub' },
  { value: 'azure_devops', label: 'Azure DevOps' },
];

export function UploadPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const store = useUploadStore();
  const { addSavedRepositories } = store;
  const { show } = useToast();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const gitCardRef = useRef<HTMLDivElement>(null);
  const [dragOver, setDragOver] = useState(false);

  // Git modal draft
  const [repoUrl, setRepoUrl] = useState('');
  const [gitProvider, setGitProvider] = useState<GitHost>('github');
  const [gitPat, setGitPat] = useState('');
  const [gitBranch, setGitBranch] = useState('');
  const [gitValidationError, setGitValidationError] = useState<string>();

  useEffect(() => {
    if (store.status === 'success' && store.successData) {
      show({ variant: 'success', title: 'Batch created — processing started.' });
      navigate(`/batches/${store.successData.batch_id}`);
    }
  }, [store.status, store.successData, navigate, show]);

  useEffect(() => {
    if (store.status === 'error' && store.submitError) {
      show({ variant: 'error', title: 'Upload failed', message: store.submitError.message });
    }
  }, [store.status, store.submitError, show]);

  // Saved repositories handed over from Source Connectors arrive once through router
  // state; clear it so going back or reloading does not add them again.
  useEffect(() => {
    const incoming = readSavedRepositoriesFromState(location.state);
    if (incoming.length === 0) return;
    addSavedRepositories(incoming);
    navigate(location.pathname, { replace: true, state: null });
  }, [location.state, location.pathname, addSavedRepositories, navigate]);

  function onFilesPicked(list: FileList | null) {
    if (list && list.length) store.addFiles(Array.from(list));
  }

  function openGitModal() {
    if (store.gitParams) {
      setRepoUrl(store.gitParams.repoUrl);
      setGitProvider(store.gitParams.provider);
      setGitPat(store.gitParams.pat ?? '');
      setGitBranch(store.gitParams.branch ?? '');
    } else {
      setRepoUrl('');
      setGitProvider('github');
      setGitPat('');
      setGitBranch('');
    }
    setGitValidationError(undefined);
    store.openGitModal();
  }

  function saveGit() {
    const errors = validateGitParams({ repoUrl, provider: gitProvider });
    if (errors.repoUrl) {
      setGitValidationError(errors.repoUrl);
      return;
    }
    store.setGitParams({
      repoUrl: repoUrl.trim(),
      provider: gitProvider,
      pat: gitPat.trim() || undefined,
      branch: gitBranch.trim() || undefined,
    });
    show({ variant: 'success', title: 'Repository connected — it will be cloned on submit.' });
    store.closeGitModal();
    setTimeout(() => gitCardRef.current?.focus(), 50);
  }

  const uploading = store.status === 'uploading';

  return (
    <AppShell>
      <PageHeader title="New Analysis" />

      <SectionCard title="Analysis Configuration">
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2,1fr)', gap: 20 }}>
          <Select
            label="Engine"
            options={ENGINE_OPTIONS}
            value={store.engine}
            onChange={(e) => store.setEngine(e.target.value as AnalysisEngine)}
          />
          <Select
            label="Seed Corpus Domain"
            options={CORPUS_OPTIONS}
            value={store.seedCorpusDomain}
            onChange={(e) => store.setSeedCorpusDomain(e.target.value as SeedCorpusDomain)}
          />
        </div>
        <div style={{ fontSize: 12.5, color: 'var(--text-muted)', marginTop: 14 }}>
          Models are configured in{' '}
          <a href="/settings" onClick={(e) => { e.preventDefault(); navigate('/settings'); }}>
            Settings → Model Configuration
          </a>
          .
        </div>
      </SectionCard>

      {/* Dropzone */}
      <div
        data-testid="upload-dropzone"
        onDragOver={(e) => {
          e.preventDefault();
          setDragOver(true);
        }}
        onDragLeave={() => setDragOver(false)}
        onDrop={(e) => {
          e.preventDefault();
          setDragOver(false);
          onFilesPicked(e.dataTransfer.files);
        }}
        style={{
          border: `1.5px dashed ${dragOver ? 'var(--accent-primary)' : 'var(--border-strong)'}`,
          borderRadius: 'var(--radius-lg)',
          background: dragOver ? 'var(--accent-primary-subtle)' : 'var(--surface-card)',
          padding: 48,
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          gap: 12,
          transition: 'all var(--duration-fast) var(--ease-standard)',
        }}
      >
        <div
          style={{
            width: 44,
            height: 44,
            borderRadius: 12,
            background: 'var(--accent-primary-subtle)',
            color: 'var(--accent-primary)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          <IconUpload size={20} />
        </div>
        <div style={{ fontSize: 'var(--text-lg)', fontWeight: 600, color: 'var(--text-primary)' }}>
          Drop research papers, theses or code here
        </div>
        <div style={{ fontSize: 'var(--text-sm)', color: 'var(--text-muted)' }}>
          Accepts PDF and DOCX — up to 25 documents per batch
        </div>
        {store.gitParams && store.files.length === 0 && (
          <div style={{ fontSize: 'var(--text-sm)', color: 'var(--text-muted)' }}>
            Optional — your connected repository will be analyzed on its own.
          </div>
        )}
        <input
          ref={fileInputRef}
          type="file"
          multiple
          accept=".pdf,.docx"
          style={{ display: 'none' }}
          onChange={(e) => onFilesPicked(e.target.files)}
          data-testid="upload-file-input"
        />
        <div style={{ marginTop: 8 }}>
          <Button variant="primary" onClick={() => fileInputRef.current?.click()}>
            Upload File
          </Button>
        </div>
        {store.zoneError && (
          <div style={{ color: 'var(--status-danger-fg)', fontSize: 13 }}>{store.zoneError}</div>
        )}
      </div>

      {/* Selected files */}
      {store.files.length > 0 && (
        <SectionCard title={`Selected files (${store.files.length})`}>
          <div style={{ display: 'flex', flexDirection: 'column' }}>
            {store.files.map((f, i) => (
              <div
                key={f.id}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  padding: '12px 0',
                  borderBottom: i === store.files.length - 1 ? 'none' : '1px solid var(--border-subtle)',
                  gap: 12,
                }}
              >
                <div style={{ minWidth: 0 }}>
                  <div style={{ fontWeight: 500, color: 'var(--text-primary)', fontSize: 13.5, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {f.file.name}
                  </div>
                  <div
                    style={{
                      fontSize: 12,
                      fontFamily: 'var(--font-mono)',
                      color: f.valid ? 'var(--text-muted)' : 'var(--status-danger-fg)',
                    }}
                  >
                    {f.valid ? formatBytes(f.file.size) : f.rejectionReason}
                  </div>
                </div>
                <button
                  type="button"
                  onClick={() => store.removeFile(f.id)}
                  aria-label={`Remove ${f.file.name}`}
                  style={{
                    width: 28,
                    height: 28,
                    borderRadius: 'var(--radius-sm)',
                    border: '1px solid var(--border-subtle)',
                    background: 'var(--surface-card)',
                    color: 'var(--status-danger-fg)',
                    cursor: 'pointer',
                    flexShrink: 0,
                  }}
                >
                  ✕
                </button>
              </div>
            ))}
          </div>
        </SectionCard>
      )}

      <SavedRepositoriesCard
        repositories={store.savedRepositories}
        maxCount={maxSavedRepositoriesPerBatch}
        onRemove={store.removeSavedRepository}
      />

      {/* Batch name */}
      <SectionCard>
        <Input
          label="Batch Name (optional)"
          placeholder="e.g. Q3 thesis archive — robotics"
          value={store.batchName}
          error={store.fieldErrors.batchName}
          onChange={(e) => store.setBatchName(e.target.value)}
        />
      </SectionCard>

      {/* Source cards */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 20 }}>
        <SourceCard
          ref={gitCardRef}
          icon={<IconGit size={20} />}
          title="Connect Git Repository"
          desc="Public or private GitHub / Azure DevOps — clone and extract code-level patent candidates."
          badge={store.gitParams ? store.gitParams.repoUrl : undefined}
          connected={!!store.gitParams}
          onClick={openGitModal}
        />
        <SourceCard
          icon={<IconFolder size={20} />}
          title="Batch Upload Folder"
          desc="Select multiple documents at once — queued and processed automatically via the pipeline."
          onClick={() => fileInputRef.current?.click()}
        />
      </div>

      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 8 }}>
        {!store.canSubmit && getDisabledReason(store)}
        <Button
          variant="primary"
          size="lg"
          ariaDisabled={!store.canSubmit}
          describedById={!store.canSubmit ? 'submit-reason' : undefined}
          onClick={() => void store.submit()}
          data-testid="upload-submit-button"
        >
          {uploading ? 'Starting…' : 'Start Analysis'}
        </Button>
      </div>

      <Modal
        open={store.gitModalOpen}
        onClose={store.closeGitModal}
        title="Connect Git Repository"
        subtitle="Supports GitHub and Azure DevOps repos"
        footer={
          <>
            <Button variant="secondary" fullWidth onClick={store.closeGitModal}>
              Cancel
            </Button>
            <Button fullWidth onClick={saveGit}>
              Connect
            </Button>
          </>
        }
      >
        <Input
          label="Repository URL"
          placeholder="https://github.com/org/repo"
          value={repoUrl}
          error={gitValidationError}
          onChange={(e) => {
            setRepoUrl(e.target.value);
            setGitValidationError(undefined);
          }}
        />
        <Select
          label="Provider"
          options={GIT_PROVIDERS}
          value={gitProvider}
          placeholder=""
          onChange={(e) => setGitProvider(e.target.value as GitHost)}
        />
        <Input
          label="Personal Access Token (private repos)"
          type="password"
          placeholder="ghp_… or Azure DevOps PAT"
          value={gitPat}
          onChange={(e) => setGitPat(e.target.value)}
        />
        <Input label="Branch" placeholder="main" value={gitBranch} onChange={(e) => setGitBranch(e.target.value)} />
      </Modal>
    </AppShell>
  );
}

interface DisabledReason {
  message: string;
  danger: boolean;
}

function isOverFileLimits(store: ReturnType<typeof useUploadStore>): boolean {
  const validFiles = store.files.filter((f) => f.valid).map((f) => f.file);
  const totalSize = validFiles.reduce((sum, f) => sum + f.size, 0);
  const maxSize = 1024 * 1024 * 1024;
  const maxCount = 25;
  return validFiles.length > maxCount || totalSize > maxSize;
}

function findDisabledReason(store: ReturnType<typeof useUploadStore>): DisabledReason | undefined {
  const hasInput = store.files.length > 0 || store.gitParams !== null || store.savedRepositories.length > 0;
  const rules: [boolean, DisabledReason][] = [
    [!hasInput, { message: 'Add a file, connect a repository, or load a saved repository to begin.', danger: false }],
    [store.files.some((f) => !f.valid), { message: 'Remove the rejected files to continue.', danger: true }],
    [Boolean(store.fieldErrors.batchName), { message: 'Fix the batch name to continue.', danger: true }],
    [isOverFileLimits(store), { message: 'Reduce the batch — over the file count or size limit.', danger: true }],
    [!store.withinSavedCap, { message: 'Remove some saved repositories — over the per-batch limit.', danger: true }],
  ];
  return rules.find(([applies]) => applies)?.[1];
}

function getDisabledReason(store: ReturnType<typeof useUploadStore>): React.ReactNode {
  const reason = findDisabledReason(store);
  if (!reason) return null;
  return (
    <div
      id="submit-reason"
      style={{ fontSize: 'var(--text-sm)', color: reason.danger ? 'var(--status-danger-fg)' : 'var(--text-muted)' }}
    >
      {reason.message}
    </div>
  );
}

const SourceCard = React.forwardRef<HTMLDivElement, {
  icon: React.ReactNode;
  title: string;
  desc: string;
  badge?: string;
  connected?: boolean;
  onClick: () => void;
}>(function SourceCard({ icon, title, desc, badge, connected, onClick }, ref) {
  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      onClick();
    }
  };

  return (
    <div
      ref={ref}
      role="button"
      tabIndex={0}
      onClick={onClick}
      onKeyDown={handleKeyDown}
      style={{
        cursor: 'pointer',
        background: 'var(--surface-card)',
        border: '1px solid var(--border-subtle)',
        borderRadius: 'var(--radius-lg)',
        boxShadow: 'var(--shadow-xs)',
        padding: 20,
        display: 'flex',
        flexDirection: 'column',
        gap: 10,
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <div style={{ color: 'var(--text-primary)' }}>{icon}</div>
        {connected && <Badge tone="success">Connected</Badge>}
      </div>
      <div style={{ fontWeight: 600, color: 'var(--text-primary)' }}>{title}</div>
      <div style={{ fontSize: 13, color: 'var(--text-muted)' }}>{desc}</div>
      {badge && (
        <div style={{ fontSize: 12, fontFamily: 'var(--font-mono)', color: 'var(--accent-primary)', wordBreak: 'break-all' }}>
          ✓ {badge}
        </div>
      )}
    </div>
  );
});
