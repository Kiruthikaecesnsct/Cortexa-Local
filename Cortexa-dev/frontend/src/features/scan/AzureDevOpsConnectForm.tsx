import { useState, type FormEvent } from 'react';
import { Button, IconCloud, IconEye, IconLock, InlineMessage, Input } from '../../shared/ds';
import type { AzureDevOpsCredentials, ScanError } from './scanTypes';

const ORG_URL_PATTERN = /^https:\/\/dev\.azure\.com\/[A-Za-z0-9][A-Za-z0-9-]{0,48}[A-Za-z0-9]?\/?$/;
const PAT_DOCS_URL =
  'https://learn.microsoft.com/en-us/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate';

type FieldErrors = Partial<Record<keyof AzureDevOpsCredentials, string>>;

function validate(credentials: AzureDevOpsCredentials): FieldErrors {
  const errors: FieldErrors = {};
  if (!ORG_URL_PATTERN.test(credentials.orgUrl.trim())) {
    errors.orgUrl = 'Enter a URL like https://dev.azure.com/your-organization';
  }
  if (credentials.pat.trim().length === 0) {
    errors.pat = 'Enter a personal access token';
  }
  return errors;
}

function RevealButton({ revealed, onToggle }: { revealed: boolean; onToggle: () => void }) {
  return (
    <button
      type="button"
      onClick={onToggle}
      aria-label={revealed ? 'Hide token' : 'Show token'}
      aria-pressed={revealed}
      style={{ border: 'none', background: 'transparent', cursor: 'pointer', color: 'var(--text-muted)', display: 'inline-flex', padding: 0 }}
    >
      <IconEye size={16} />
    </button>
  );
}

interface AzureDevOpsConnectFormProps {
  connecting: boolean;
  error?: ScanError;
  onConnect: (credentials: AzureDevOpsCredentials) => void;
}

export function AzureDevOpsConnectForm({ connecting, error, onConnect }: AzureDevOpsConnectFormProps) {
  const [orgUrl, setOrgUrl] = useState('');
  const [pat, setPat] = useState('');
  const [revealed, setRevealed] = useState(false);
  const [errors, setErrors] = useState<FieldErrors>({});

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const credentials = { orgUrl, pat };
    const found = validate(credentials);
    setErrors(found);
    if (Object.keys(found).length === 0) onConnect(credentials);
  }

  return (
    <form onSubmit={onSubmit} noValidate style={{ display: 'flex', flexDirection: 'column', gap: 18, maxWidth: 560 }}>
      <Input
        label="1. Organization URL"
        aria-label="Organization URL"
        placeholder="https://dev.azure.com/your-organization"
        leading={<span style={{ color: 'var(--text-muted)', display: 'inline-flex' }}><IconCloud size={16} /></span>}
        value={orgUrl}
        onChange={(e) => setOrgUrl(e.target.value)}
        error={errors.orgUrl}
        disabled={connecting}
        autoComplete="off"
        spellCheck={false}
        autoFocus
      />
      <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        <Input
          label="2. Personal access token"
          aria-label="Personal access token"
          type={revealed ? 'text' : 'password'}
          placeholder="Paste your Azure DevOps PAT"
          leading={<span style={{ color: 'var(--text-muted)', display: 'inline-flex' }}><IconLock size={16} /></span>}
          trailing={<RevealButton revealed={revealed} onToggle={() => setRevealed((v) => !v)} />}
          value={pat}
          onChange={(e) => setPat(e.target.value)}
          error={errors.pat}
          disabled={connecting}
          autoComplete="off"
        />
        <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-muted)', lineHeight: 1.5 }}>
          Needs read access to code (Code &gt; Read).{' '}
          <a href={PAT_DOCS_URL} target="_blank" rel="noopener noreferrer" style={{ color: 'var(--accent-primary)' }}>
            How to create a PAT
          </a>
          . The token is used only for this scan and is never stored.
        </span>
      </div>
      {error && (
        <InlineMessage variant="danger" correlationId={error.correlationId}>
          {error.message}
        </InlineMessage>
      )}
      <div>
        <Button type="submit" disabled={connecting}>
          {connecting ? 'Connecting to Azure DevOps…' : '3. Connect and find repositories'}
        </Button>
      </div>
    </form>
  );
}
