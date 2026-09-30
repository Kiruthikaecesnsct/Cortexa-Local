import { IconGit } from '../../shared/ds';
import type { ConnectFormProps } from './ScanWizardPanel';
import { TokenConnectForm, type TokenProviderCopy } from './TokenConnectForm';

const TOKEN_SETTINGS_URL = 'https://github.com/settings/personal-access-tokens';

const GITHUB: TokenProviderCopy = {
  name: 'GitHub',
  orgIcon: <IconGit size={16} />,
  orgHint: 'The GitHub organization whose repositories you want to scan.',
  orgUrlPattern: /^https:\/\/(www\.)?github\.com\/(orgs\/)?[A-Za-z0-9][A-Za-z0-9-]{0,38}\/?$/,
  orgUrlPlaceholder: 'https://github.com/your-organization',
  tokenHint: "A personal access token that can read the organization's repositories.",
  tokenPlaceholder: 'github_pat_…',
  tokenHelp: (
    <>
      Needs read access to repository metadata and contents.{' '}
      <a href={TOKEN_SETTINGS_URL} target="_blank" rel="noopener noreferrer" style={{ color: 'var(--accent-primary)' }}>
        Create a token on GitHub
      </a>
      .
    </>
  ),
};

export function GithubConnectForm(props: ConnectFormProps) {
  return <TokenConnectForm provider={GITHUB} {...props} />;
}
