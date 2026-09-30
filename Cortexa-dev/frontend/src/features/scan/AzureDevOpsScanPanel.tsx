import { AzureDevOpsConnectForm } from './AzureDevOpsConnectForm';
import { promptFor } from './azureDevOpsScanPrompts';
import { azureDevOpsCloneApi } from './cloneApi';
import { ScanWizardPanel, type ScanProviderConfig } from './ScanWizardPanel';
import { useAzureDevOpsScan } from './useAzureDevOpsScan';

const AZURE_DEVOPS: ScanProviderConfig = {
  name: 'Azure DevOps',
  subtitle: 'Connect to an organization, pick a repository and branch, then browse or save its files.',
  ConnectForm: AzureDevOpsConnectForm,
  promptFor,
  cloneApi: azureDevOpsCloneApi,
};

export function AzureDevOpsScanPanel() {
  const scan = useAzureDevOpsScan();
  return <ScanWizardPanel config={AZURE_DEVOPS} scan={scan} />;
}
