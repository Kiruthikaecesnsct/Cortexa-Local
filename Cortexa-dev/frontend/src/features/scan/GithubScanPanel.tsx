import { githubCloneApi } from './cloneApi';
import { GithubConnectForm } from './GithubConnectForm';
import { promptFor } from './scanPrompts';
import { ScanWizardPanel, type ScanProviderConfig } from './ScanWizardPanel';
import { useGithubScan } from './useGithubScan';

const GITHUB: ScanProviderConfig = {
  name: 'GitHub',
  subtitle: 'Connect to an organization, pick a repository and branch, then browse or save its files.',
  ConnectForm: GithubConnectForm,
  promptFor,
  cloneApi: githubCloneApi,
};

export function GithubScanPanel() {
  const scan = useGithubScan();
  return <ScanWizardPanel config={GITHUB} scan={scan} />;
}
