import { useRef, useState, type ReactNode } from 'react';
import { Button, Switch, Badge, InlineMessage, PatentStatusBadge, IconCheck, IconMinus } from '../../shared/ds';
import { relativeTime } from '../../shared/utils';
import type { WriteSecretResult } from './patentConfigRepository';
import type { PatentSecretRequest, PatentSourceId, SourceTestResult } from './patentApiTypes';

export interface SourceFieldDef {
  name: 'apiKey' | 'consumerKey' | 'oauthSecret';
  label: string;
  placeholder: string;
}

export interface SourceDef {
  id: PatentSourceId;
  name: string;
  code: string;
  subLabel: string;
  fields: SourceFieldDef[];
}

export interface CredentialBadgeSpec {
  status: 'set' | 'not_set';
  setLabel: string;
  notSetLabel: string;
}

const FIELD_TO_PAYLOAD_KEY: Record<SourceFieldDef['name'], keyof PatentSecretRequest> = {
  apiKey: 'api_key',
  consumerKey: 'consumer_key',
  oauthSecret: 'oauth_secret',
};

function buildPayload(def: SourceDef, values: Record<string, string>): PatentSecretRequest {
  const payload: PatentSecretRequest = {};
  for (const field of def.fields) {
    const value = values[field.name]?.trim();
    if (value) payload[FIELD_TO_PAYLOAD_KEY[field.name]] = value;
  }
  return payload;
}

const rowStyle = (enabled: boolean): React.CSSProperties => ({
  border: '1px solid var(--border-subtle)',
  borderRadius: 'var(--radius-md)',
  padding: 20,
  background: 'var(--surface-card-alt)',
  display: 'flex',
  flexDirection: 'column',
  gap: 16,
  opacity: enabled ? 1 : 0.62,
  transition: 'opacity var(--duration-base) var(--ease-standard)',
});

function SourceLogo({ code, enabled }: { code: string; enabled: boolean }) {
  return (
    <div
      style={{
        width: 40,
        height: 40,
        borderRadius: 'var(--radius-sm)',
        background: enabled ? 'var(--accent-grad)' : 'var(--gray-300)',
        color: '#fff',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        fontWeight: 700,
        fontSize: 14,
        flexShrink: 0,
      }}
    >
      {code}
    </div>
  );
}

function TestStatusBadge({ result }: { result: SourceTestResult }) {
  if (result.status === 'success' && result.testedAt) {
    return (
      <Badge tone="success" icon={<IconCheck size={13} />}>
        {`Reachable · checked ${relativeTime(result.testedAt)}`}
      </Badge>
    );
  }
  if (result.status === 'idle') {
    return (
      <Badge tone="neutral" icon={<IconMinus size={13} />}>
        Not yet tested
      </Badge>
    );
  }
  return null;
}

function KeyEntryField({
  field,
  value,
  onChange,
  autoFocus,
}: {
  field: SourceFieldDef;
  value: string;
  onChange: (next: string) => void;
  autoFocus: boolean;
}) {
  const [revealed, setRevealed] = useState(false);
  const inputId = `patent-secret-${field.name}`;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <label htmlFor={inputId} style={{ fontSize: 'var(--text-sm)', fontWeight: 500, color: 'var(--text-primary)' }}>
        {field.label}
      </label>
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 8,
          padding: '10px 14px',
          borderRadius: 'var(--radius-md)',
          background: 'var(--surface-card)',
          border: '1px solid var(--border-subtle)',
        }}
      >
        <input
          id={inputId}
          type={revealed ? 'text' : 'password'}
          autoComplete="off"
          placeholder={field.placeholder}
          value={value}
          autoFocus={autoFocus}
          onChange={(e) => onChange(e.target.value)}
          style={{
            flex: 1,
            border: 'none',
            outline: 'none',
            background: 'transparent',
            fontFamily: 'var(--font-body)',
            fontSize: 'var(--text-base)',
            color: 'var(--text-primary)',
            minWidth: 0,
          }}
        />
        <button
          type="button"
          onClick={() => setRevealed((prev) => !prev)}
          style={{ background: 'none', border: 'none', color: 'var(--text-muted)', cursor: 'pointer', fontSize: 'var(--text-xs)', fontWeight: 600, padding: '2px 4px' }}
        >
          {revealed ? 'Hide' : 'Show'}
        </button>
      </div>
    </div>
  );
}

interface KeyEntryPanelProps {
  def: SourceDef;
  submitting: boolean;
  testing: boolean;
  onSubmit: (payload: PatentSecretRequest) => void;
  onTestWithSupplied: (payload: PatentSecretRequest) => void;
  onCancel: () => void;
  error: { message: string; correlationId?: string } | null;
}

function KeyEntryPanel({ def, submitting, testing, onSubmit, onTestWithSupplied, onCancel, error }: KeyEntryPanelProps) {
  const [values, setValues] = useState<Record<string, string>>({});

  const helpText =
    def.fields.length > 1
      ? 'Both fields are write-only and never echoed. Fill only the field you want to replace — a blank field keeps its current stored value.'
      : 'Write-only. The saved key is never shown back. Leave blank to keep the current key. This field is cleared on save or cancel.';

  return (
    <div style={{ borderTop: '1px dashed var(--border-strong)', paddingTop: 16, display: 'flex', flexDirection: 'column', gap: 14 }}>
      {def.fields.map((field, index) => (
        <KeyEntryField
          key={field.name}
          field={field}
          value={values[field.name] ?? ''}
          onChange={(next) => setValues((prev) => ({ ...prev, [field.name]: next }))}
          autoFocus={index === 0}
        />
      ))}
      <div style={{ fontSize: 'var(--text-xs)', color: 'var(--text-muted)', lineHeight: 1.5 }}>{helpText}</div>
      {error && <InlineMessage variant="danger" correlationId={error.correlationId}>{error.message}</InlineMessage>}
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10 }}>
        <Button variant="primary" size="sm" disabled={submitting} onClick={() => onSubmit(buildPayload(def, values))}>
          {submitting ? 'Saving key…' : def.fields.length > 1 ? 'Save keys' : 'Save key'}
        </Button>
        <Button variant="secondary" size="sm" disabled={testing} onClick={() => onTestWithSupplied(buildPayload(def, values))}>
          {testing ? 'Testing…' : 'Test without saving'}
        </Button>
        <Button variant="ghost" size="sm" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </div>
  );
}

export interface SourceRowProps {
  def: SourceDef;
  enabled: boolean;
  readOnly: boolean;
  credentialBadges: CredentialBadgeSpec[];
  testResult: SourceTestResult;
  onToggleEnabled: (next: boolean) => void;
  onSubmitSecret: (payload: PatentSecretRequest) => Promise<WriteSecretResult>;
  onTestConnection: (payload?: PatentSecretRequest) => void;
}

function useKeyPanel(onTestConnection: SourceRowProps['onTestConnection'], onSubmitSecret: SourceRowProps['onSubmitSecret']) {
  const [open, setOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<{ message: string; correlationId?: string } | null>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);

  function openPanel() {
    setSubmitError(null);
    setOpen(true);
  }

  function closePanel() {
    setOpen(false);
    setSubmitError(null);
    triggerRef.current?.focus();
  }

  async function submit(payload: PatentSecretRequest) {
    setSubmitting(true);
    const result = await onSubmitSecret(payload);
    setSubmitting(false);
    if (!result.ok) {
      setSubmitError({ message: result.error.message, correlationId: result.error.correlationId });
      return;
    }
    closePanel();
  }

  function testSupplied(payload: PatentSecretRequest) {
    onTestConnection(payload);
  }

  return { open, submitting, submitError, triggerRef, openPanel, closePanel, submit, testSupplied };
}

function SourceActions({
  def,
  hasAnyCredential,
  showTestButton,
  readOnly,
  testing,
  onToggle,
  onTest,
  triggerRef,
}: {
  def: SourceDef;
  hasAnyCredential: boolean;
  showTestButton: boolean;
  readOnly: boolean;
  testing: boolean;
  onToggle: () => void;
  onTest: () => void;
  triggerRef: React.RefObject<HTMLButtonElement | null>;
}) {
  const replaceLabel = hasAnyCredential ? (def.fields.length > 1 ? 'Replace keys' : 'Replace key') : 'Add key';
  return (
    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10, alignItems: 'center' }}>
      <Button ref={triggerRef} variant="secondary" size="sm" disabled={readOnly} onClick={onToggle}>
        {replaceLabel}
      </Button>
      {showTestButton && (
        <Button variant="secondary" size="sm" disabled={readOnly || testing} onClick={onTest}>
          {testing ? 'Testing…' : 'Test connection'}
        </Button>
      )}
    </div>
  );
}

function StoredTestMessage({ result }: { result: SourceTestResult }): ReactNode {
  if (result.status === 'testing') {
    return (
      <InlineMessage variant="info" loading>
        Testing connection…
      </InlineMessage>
    );
  }
  if (result.status === 'failure') {
    return (
      <InlineMessage variant="danger" correlationId={result.correlationId}>
        {`Connection failed${result.message ? ` — ${result.message}` : '.'}`}
      </InlineMessage>
    );
  }
  return null;
}

export function SourceRow({ def, enabled, readOnly, credentialBadges, testResult, onToggleEnabled, onSubmitSecret, onTestConnection }: SourceRowProps) {
  const panel = useKeyPanel(onTestConnection, onSubmitSecret);
  const hasAnyCredential = credentialBadges.some((b) => b.status === 'set');
  const showTestButton = hasAnyCredential;
  const testing = testResult.status === 'testing';

  return (
    <div style={rowStyle(enabled)}>
      <div style={{ display: 'flex', alignItems: 'flex-start', gap: 14 }}>
        <SourceLogo code={def.code} enabled={enabled} />
        <div style={{ flex: 1 }}>
          <div style={{ fontWeight: 600, fontSize: 'var(--text-md)', color: 'var(--text-primary)' }}>{def.name}</div>
          <div style={{ fontSize: 'var(--text-xs)', color: 'var(--text-muted)', fontFamily: 'var(--font-mono)', marginTop: 2 }}>{def.subLabel}</div>
        </div>
        <Switch checked={enabled} onChange={onToggleEnabled} disabled={readOnly} ariaLabel={`Enable ${def.name}`} />
      </div>

      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10, alignItems: 'center' }}>
        {credentialBadges.map((badge, index) => (
          <PatentStatusBadge key={index} status={badge.status} setLabel={badge.setLabel} notSetLabel={badge.notSetLabel} />
        ))}
        <TestStatusBadge result={testResult} />
      </div>

      <SourceActions
        def={def}
        hasAnyCredential={hasAnyCredential}
        showTestButton={showTestButton}
        readOnly={readOnly}
        testing={testing}
        onToggle={() => (panel.open ? panel.closePanel() : panel.openPanel())}
        onTest={() => onTestConnection()}
        triggerRef={panel.triggerRef}
      />

      <StoredTestMessage result={testResult} />

      {panel.open && (
        <KeyEntryPanel
          def={def}
          submitting={panel.submitting}
          testing={testing}
          onSubmit={panel.submit}
          onTestWithSupplied={panel.testSupplied}
          onCancel={panel.closePanel}
          error={panel.submitError}
        />
      )}
    </div>
  );
}
