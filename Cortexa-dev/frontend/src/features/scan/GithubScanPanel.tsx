import { type ReactNode } from 'react';
import { Button } from '../../shared/ds';
import { BranchPicker } from './BranchPicker';
import { FileExplorer } from './FileExplorer';
import { GithubConnectForm } from './GithubConnectForm';
import { RepositoryPicker } from './RepositoryPicker';
import { ScanLoader } from './ScanLoader';
import { promptFor } from './scanPrompts';
import { ScanStepper } from './ScanStepper';
import type { WizardState, WizardStep } from './scanWizard';
import { StepPrompt } from './StepPrompt';
import { useGithubScan } from './useGithubScan';

type Scan = ReturnType<typeof useGithubScan>;

function ConnectStep({ scan }: { scan: Scan }) {
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

function RepositoryStep({ scan }: { scan: Scan }) {
  const { connection, repository } = scan.state;
  if (connection.status !== 'loaded') return null;
  return <RepositoryPicker repositories={connection.data.repositories} selectedName={repository?.name} onSelect={(r) => void scan.selectRepository(r)} />;
}

function BranchStep({ scan }: { scan: Scan }) {
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

function FilesStep({ scan }: { scan: Scan }) {
  const { connection, repository, branch, tree } = scan.state;
  if (connection.status !== 'loaded' || !repository || !branch) return null;
  return (
    <FileExplorer
      owner={connection.data.owner}
      repository={repository.name}
      branch={branch}
      tree={tree}
      onRetry={() => void scan.selectBranch(repository, branch)}
    />
  );
}

const STEP_BODIES: Record<WizardStep, (props: { scan: Scan }) => ReactNode> = {
  connect: ConnectStep,
  repository: RepositoryStep,
  branch: BranchStep,
  files: FilesStep,
};

function StepBody({ scan }: { scan: Scan }) {
  const Body = STEP_BODIES[scan.state.step];
  return <Body scan={scan} />;
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

export function GithubScanPanel() {
  const scan = useGithubScan();
  const { state } = scan;
  const connected = state.connection.status === 'loaded';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
        <div style={{ flex: 1, minWidth: 0 }}>
          <ScanStepper state={state} onSelect={scan.goToStep} />
        </div>
        {connected && (
          <Button size="sm" variant="ghost" onClick={scan.disconnect}>
            Disconnect
          </Button>
        )}
      </div>
      <StepPrompt copy={promptFor(state)} actions={promptActions(state, scan.goToStep)} />
      <StepBody scan={scan} />
    </div>
  );
}
