import { type ReactNode } from 'react';
import { Badge, Button } from '../../shared/ds';
import { SectionCard } from '../../shared/layout/PageHeader';
import { BranchPicker } from './BranchPicker';
import { latestCloneFor } from './cloneFormat';
import { CloneControl } from './CloneControl';
import { ClonesPanel } from './ClonesPanel';
import { FileExplorer } from './FileExplorer';
import { GithubConnectForm } from './GithubConnectForm';
import { RepositoryPicker } from './RepositoryPicker';
import { ScanLoader } from './ScanLoader';
import { promptFor } from './scanPrompts';
import { ScanStepper } from './ScanStepper';
import type { WizardState, WizardStep } from './scanWizard';
import { StepPrompt } from './StepPrompt';
import { useClones, type CloneActionResult } from './useClones';
import { useGithubScan } from './useGithubScan';

type Scan = ReturnType<typeof useGithubScan>;
type Clones = ReturnType<typeof useClones>;

interface StepProps {
  scan: Scan;
  clones: Clones;
}

function ConnectStep({ scan }: StepProps) {
  const { connection } = scan.state;
  const error = connection.status === 'error' ? connection.error : undefined;
  const connecting = connection.status === 'loading';
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      <GithubConnectForm connecting={connecting} error={error} onConnect={(c) => void scan.connect(c)} />
      {connecting && (
        <ScanLoader
          title="Connecting to GitHub"
          message="Checking your token and finding every repository it can see."
          slowHint="Organizations with many repositories take longer, since GitHub returns them 100 at a time."
        />
      )}
    </div>
  );
}

function RepositoryStep({ scan }: StepProps) {
  const { connection, repository } = scan.state;
  if (connection.status !== 'loaded') return null;
  return <RepositoryPicker repositories={connection.data.repositories} selectedName={repository?.name} onSelect={(r) => void scan.selectRepository(r)} />;
}

function BranchStep({ scan }: StepProps) {
  const { repository, branches, branch } = scan.state;
  if (!repository) return null;
  return (
    <BranchPicker
      repository={repository}
      branches={branches}
      selected={branch}
      onSelect={(b) => void scan.selectBranch(repository, b)}
      onRetry={() => void scan.selectRepository(repository)}
    />
  );
}

function cloneFor(scan: Scan, clones: Clones, repository: string, branch: string): () => Promise<CloneActionResult> {
  return async () => {
    const credentials = scan.credentials();
    if (!credentials) return { ok: false, error: { message: 'Connect to GitHub again to save.' } };
    return clones.start(credentials, repository, branch);
  };
}

function FilesStep({ scan, clones }: StepProps) {
  const { connection, repository, branch, tree } = scan.state;
  if (connection.status !== 'loaded' || !repository || !branch) return null;
  const all = clones.clones.status === 'loaded' ? clones.clones.data : [];
  const latest = latestCloneFor(all, { owner: connection.data.owner, repository: repository.name, branch });
  return (
    <FileExplorer
      owner={connection.data.owner}
      repository={repository.name}
      branch={branch}
      tree={tree}
      onRetry={() => void scan.selectBranch(repository, branch)}
      action={<CloneControl latest={latest} onClone={cloneFor(scan, clones, repository.name, branch)} />}
    />
  );
}

const STEP_BODIES: Record<WizardStep, (props: StepProps) => ReactNode> = {
  connect: ConnectStep,
  repository: RepositoryStep,
  branch: BranchStep,
  files: FilesStep,
};

function StepBody({ scan, clones }: StepProps) {
  const Body = STEP_BODIES[scan.state.step];
  return <Body scan={scan} clones={clones} />;
}

function StepLink({ step, label, onGo }: { step: WizardStep; label: string; onGo: (step: WizardStep) => void }) {
  return (
    <Button size="sm" variant="secondary" onClick={() => onGo(step)}>
      {label}
    </Button>
  );
}

function promptActions(state: WizardState, onGo: (step: WizardStep) => void): ReactNode {
  if (state.step === 'branch') return <StepLink step="repository" label="Change repository" onGo={onGo} />;
  if (state.step !== 'files') return null;
  return (
    <>
      <StepLink step="branch" label="Change branch" onGo={onGo} />
      <StepLink step="repository" label="Change repository" onGo={onGo} />
    </>
  );
}

function ConnectionStatus({ scan }: { scan: Scan }) {
  const { connection } = scan.state;
  if (connection.status !== 'loaded') {
    return <Badge tone="neutral">{connection.status === 'loading' ? 'Connecting…' : 'Not connected'}</Badge>;
  }
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
      <Badge tone="success">Connected · {connection.data.owner}</Badge>
      <Button size="sm" variant="ghost" onClick={scan.disconnect}>
        Disconnect
      </Button>
    </span>
  );
}

const stepperBarStyle: React.CSSProperties = {
  padding: '14px 18px',
  borderRadius: 'var(--radius-md)',
  background: 'var(--surface-sunken)',
  border: '1px solid var(--border-subtle)',
};

export function GithubScanPanel() {
  const scan = useGithubScan();
  const clones = useClones();
  const { state } = scan;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      <SectionCard
        title="GitHub scan"
        subtitle="Connect to an organization, pick a repository and branch, then browse or save its files."
        actions={<ConnectionStatus scan={scan} />}
      >
        <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
          <div style={stepperBarStyle}>
            <ScanStepper state={state} onSelect={scan.goToStep} />
          </div>
          <StepPrompt copy={promptFor(state)} actions={promptActions(state, scan.goToStep)} />
          <StepBody scan={scan} clones={clones} />
        </div>
      </SectionCard>
      <ClonesPanel clones={clones.clones} downloading={clones.downloading} onDownload={clones.download} onRetry={() => void clones.refresh()} />
    </div>
  );
}
