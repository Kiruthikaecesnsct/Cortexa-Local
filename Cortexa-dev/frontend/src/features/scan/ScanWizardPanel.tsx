import { type ComponentType, type ReactNode } from 'react';
import { Badge, Button } from '../../shared/ds';
import { SectionCard } from '../../shared/layout/PageHeader';
import { BranchPicker } from './BranchPicker';
import type { CloneApi } from './cloneApi';
import { latestCloneFor } from './cloneFormat';
import { CloneControl } from './CloneControl';
import { ClonesPanel } from './ClonesPanel';
import { FileExplorer } from './FileExplorer';
import { RepositoryPicker } from './RepositoryPicker';
import type { ScanCredentials } from './scanHttp';
import { ScanLoader } from './ScanLoader';
import type { StepPromptCopy } from './scanPrompts';
import { ScanStepper } from './ScanStepper';
import type { RepositorySummaryDto, ScanError } from './scanTypes';
import type { WizardState, WizardStep } from './scanWizard';
import { StepPrompt } from './StepPrompt';
import { useClones, type CloneActionResult } from './useClones';

/** What every provider's scan hook exposes to the shared wizard. */
export interface ScanController {
  state: WizardState;
  connect: (credentials: ScanCredentials) => Promise<void>;
  selectRepository: (repository: RepositorySummaryDto) => Promise<void>;
  selectBranch: (repository: RepositorySummaryDto, branch: string) => Promise<void>;
  goToStep: (step: WizardStep) => void;
  disconnect: () => void;
  credentials: () => ScanCredentials | null;
}

export interface ConnectFormProps {
  connecting: boolean;
  error?: ScanError;
  onConnect: (credentials: ScanCredentials) => void;
}

/** The provider-specific pieces of the scan wizard. */
export interface ScanProviderConfig {
  name: string;
  subtitle: string;
  ConnectForm: ComponentType<ConnectFormProps>;
  promptFor: (state: WizardState) => StepPromptCopy;
  cloneApi: CloneApi;
}

type Clones = ReturnType<typeof useClones>;

interface StepProps {
  config: ScanProviderConfig;
  scan: ScanController;
  clones: Clones;
}

function ConnectStep({ config, scan }: StepProps) {
  const { connection } = scan.state;
  const error = connection.status === 'error' ? connection.error : undefined;
  const connecting = connection.status === 'loading';
  const { ConnectForm } = config;
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      <ConnectForm connecting={connecting} error={error} onConnect={(c) => void scan.connect(c)} />
      {connecting && (
        <ScanLoader
          title={`Connecting to ${config.name}`}
          message="Checking your token and finding every repository it can see."
          slowHint="Organizations with many repositories take longer to list."
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

function saveAction({ config, scan, clones }: StepProps, repository: string, branch: string): () => Promise<CloneActionResult> {
  return async () => {
    const credentials = scan.credentials();
    if (!credentials) return { ok: false, error: { message: `Connect to ${config.name} again to save.` } };
    return clones.start(credentials, repository, branch);
  };
}

function FilesStep(props: StepProps) {
  const { connection, repository, branch, tree } = props.scan.state;
  if (connection.status !== 'loaded' || !repository || !branch) return null;
  const all = props.clones.clones.status === 'loaded' ? props.clones.clones.data : [];
  const latest = latestCloneFor(all, { owner: connection.data.owner, repository: repository.name, branch });
  return (
    <FileExplorer
      owner={connection.data.owner}
      repository={repository.name}
      branch={branch}
      tree={tree}
      onRetry={() => void props.scan.selectBranch(repository, branch)}
      action={<CloneControl latest={latest} onClone={saveAction(props, repository.name, branch)} />}
    />
  );
}

const STEP_BODIES: Record<WizardStep, (props: StepProps) => ReactNode> = {
  connect: ConnectStep,
  repository: RepositoryStep,
  branch: BranchStep,
  files: FilesStep,
};

function StepBody(props: StepProps) {
  const Body = STEP_BODIES[props.scan.state.step];
  return <Body {...props} />;
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

function ConnectionStatus({ scan }: { scan: ScanController }) {
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

/** Connect → repository → branch → files, plus the provider's saved repositories. */
export function ScanWizardPanel({ config, scan }: { config: ScanProviderConfig; scan: ScanController }) {
  const clones = useClones(config.cloneApi);
  const { state } = scan;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      <SectionCard title={`${config.name} scan`} subtitle={config.subtitle} actions={<ConnectionStatus scan={scan} />}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
          <div style={stepperBarStyle}>
            <ScanStepper state={state} onSelect={scan.goToStep} />
          </div>
          <StepPrompt copy={config.promptFor(state)} actions={promptActions(state, scan.goToStep)} />
          <StepBody config={config} scan={scan} clones={clones} />
        </div>
      </SectionCard>
      <ClonesPanel clones={clones.clones} downloading={clones.downloading} onDownload={clones.download} onRetry={() => void clones.refresh()} />
    </div>
  );
}
