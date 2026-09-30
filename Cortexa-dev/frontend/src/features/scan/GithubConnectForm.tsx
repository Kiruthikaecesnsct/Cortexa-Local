import { useState, type FormEvent } from 'react';
import { Button, IconEye, IconGit, IconLock, InlineMessage, Input } from '../../shared/ds';
import type { GithubCredentials, ScanError } from './scanTypes';

const ORG_URL_PATTERN = /^https:\/\/(www\.)?github\.com\/(orgs\/)?[A-Za-z0-9][A-Za-z0-9-]{0,38}\/?$/;
const TOKEN_SETTINGS_URL = 'https://github.com/settings/personal-access-tokens';

type FieldErrors = Partial<Record<keyof GithubCredentials, string>>;

function validate(credentials: GithubCredentials): FieldErrors {
  const errors: FieldErrors = {};
  if (!ORG_URL_PATTERN.test(credentials.orgUrl.trim())) {
    errors.orgUrl = 'Enter a URL like https://github.com/your-organization';
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

interface GithubConnectFormProps {
  connecting: boolean;
  error?: ScanError;
  onConnect: (credentials: GithubCredentials) => void;
}

export function GithubConnectForm({ connecting, error, onConnect }: GithubConnectFormProps) {
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
        placeholder="https://github.com/your-organization"
        leading={<span style={{ color: 'var(--text-muted)', display: 'inline-flex' }}><IconGit size={16} /></span>}
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
          placeholder="github_pat_…"
          leading={<span style={{ color: 'var(--text-muted)', display: 'inline-flex' }}><IconLock size={16} /></span>}
          trailing={<RevealButton revealed={revealed} onToggle={() => setRevealed((v) => !v)} />}
          value={pat}
          onChange={(e) => setPat(e.target.value)}
          error={errors.pat}
          disabled={connecting}
          autoComplete="off"
        />
        <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-muted)', lineHeight: 1.5 }}>
          Needs read access to repository metadata and contents.{' '}
          <a href={TOKEN_SETTINGS_URL} target="_blank" rel="noopener noreferrer" style={{ color: 'var(--accent-primary)' }}>
            Create a token on GitHub
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
          {connecting ? 'Connecting to GitHub…' : '3. Connect and find repositories'}
        </Button>
      </div>
    </form>
  );
}
