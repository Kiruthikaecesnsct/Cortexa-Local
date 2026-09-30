import { useState, type FormEvent, type ReactNode } from 'react';
import { Button, IconEye, IconLock, InlineMessage, Input, Spinner } from '../../shared/ds';
import { FieldHint, FieldIcon, FORM_SECTION_GRID_STYLE, FormSection } from './FormSection';
import type { ScanCredentials } from './scanHttp';
import type { ScanError } from './scanTypes';

/** Everything that differs between providers that connect with an org URL and a PAT. */
export interface TokenProviderCopy {
  name: string;
  orgIcon: ReactNode;
  orgHint: string;
  orgUrlPattern: RegExp;
  orgUrlPlaceholder: string;
  tokenHint: string;
  tokenPlaceholder: string;
  tokenHelp: ReactNode;
}

type FieldErrors = Partial<Record<keyof ScanCredentials, string>>;

function validate(provider: TokenProviderCopy, credentials: ScanCredentials): FieldErrors {
  const errors: FieldErrors = {};
  if (!provider.orgUrlPattern.test(credentials.orgUrl.trim())) {
    errors.orgUrl = `Enter a URL like ${provider.orgUrlPlaceholder}`;
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
      style={{ border: 'none', background: 'transparent', cursor: 'pointer', color: revealed ? 'var(--accent-primary)' : 'var(--text-muted)', display: 'inline-flex', padding: 0 }}
    >
      <IconEye size={16} />
    </button>
  );
}

interface FieldProps {
  provider: TokenProviderCopy;
  value: string;
  error?: string;
  disabled: boolean;
  onChange: (value: string) => void;
}

function OrganizationSection({ provider, value, error, disabled, onChange }: FieldProps) {
  return (
    <FormSection step={1} title="Organization" hint={provider.orgHint}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        <Input
          label="Organization URL"
          aria-label="Organization URL"
          placeholder={provider.orgUrlPlaceholder}
          leading={<FieldIcon>{provider.orgIcon}</FieldIcon>}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          error={error}
          disabled={disabled}
          autoComplete="off"
          spellCheck={false}
          autoFocus
        />
        {!error && <FieldHint>Copy it from the browser address bar on the organization's {provider.name} page.</FieldHint>}
      </div>
    </FormSection>
  );
}

function TokenSection({ provider, value, error, disabled, onChange }: FieldProps) {
  const [revealed, setRevealed] = useState(false);
  return (
    <FormSection step={2} title="Access token" hint={provider.tokenHint}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        <Input
          label="Personal access token"
          aria-label="Personal access token"
          type={revealed ? 'text' : 'password'}
          placeholder={provider.tokenPlaceholder}
          leading={<FieldIcon><IconLock size={16} /></FieldIcon>}
          trailing={<RevealButton revealed={revealed} onToggle={() => setRevealed((v) => !v)} />}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          error={error}
          disabled={disabled}
          autoComplete="off"
        />
        <FieldHint>{provider.tokenHelp} Used only for this scan and never stored.</FieldHint>
      </div>
    </FormSection>
  );
}

interface TokenConnectFormProps {
  provider: TokenProviderCopy;
  connecting: boolean;
  error?: ScanError;
  onConnect: (credentials: ScanCredentials) => void;
}

export function TokenConnectForm({ provider, connecting, error, onConnect }: TokenConnectFormProps) {
  const [orgUrl, setOrgUrl] = useState('');
  const [pat, setPat] = useState('');
  const [errors, setErrors] = useState<FieldErrors>({});

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const credentials = { orgUrl, pat };
    const found = validate(provider, credentials);
    setErrors(found);
    if (Object.keys(found).length === 0) onConnect(credentials);
  }

  return (
    <form onSubmit={onSubmit} noValidate style={{ display: 'flex', flexDirection: 'column', gap: 16, width: '100%' }}>
      <div style={FORM_SECTION_GRID_STYLE}>
        <OrganizationSection provider={provider} value={orgUrl} error={errors.orgUrl} disabled={connecting} onChange={setOrgUrl} />
        <TokenSection provider={provider} value={pat} error={errors.pat} disabled={connecting} onChange={setPat} />
      </div>
      {error && (
        <InlineMessage variant="danger" correlationId={error.correlationId}>
          {error.message}
        </InlineMessage>
      )}
      <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
        <Button type="submit" disabled={connecting} icon={connecting ? <Spinner size={14} /> : undefined}>
          {connecting ? `Connecting to ${provider.name}…` : 'Connect and find repositories'}
        </Button>
      </div>
    </form>
  );
}
