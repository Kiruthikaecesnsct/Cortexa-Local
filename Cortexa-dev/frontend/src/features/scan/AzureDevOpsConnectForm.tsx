import { IconCloud } from '../../shared/ds';
import type { ConnectFormProps } from './ScanWizardPanel';
import { TokenConnectForm, type TokenProviderCopy } from './TokenConnectForm';

const PAT_DOCS_URL =
  'https://learn.microsoft.com/en-us/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate';

const AZURE_DEVOPS: TokenProviderCopy = {
  name: 'Azure DevOps',
  orgIcon: <IconCloud size={16} />,
  orgHint: 'The Azure DevOps organization whose repositories you want to scan.',
  orgUrlPattern: /^https:\/\/dev\.azure\.com\/[A-Za-z0-9][A-Za-z0-9-]{0,48}[A-Za-z0-9]?\/?$/,
  orgUrlPlaceholder: 'https://dev.azure.com/your-organization',
  tokenHint: "A personal access token that can read the organization's code.",
  tokenPlaceholder: 'Paste your Azure DevOps PAT',
  tokenHelp: (
    <>
      Needs read access to code (Code &gt; Read).{' '}
      <a href={PAT_DOCS_URL} target="_blank" rel="noopener noreferrer" style={{ color: 'var(--accent-primary)' }}>
        How to create a PAT
      </a>
      .
    </>
  ),
};

export function AzureDevOpsConnectForm(props: ConnectFormProps) {
  return <TokenConnectForm provider={AZURE_DEVOPS} {...props} />;
}
