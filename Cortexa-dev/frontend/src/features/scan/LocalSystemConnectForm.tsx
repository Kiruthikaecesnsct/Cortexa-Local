import { useState, type FormEvent } from 'react';
import { Button, IconEye, IconFolder, IconLock, IconServer, IconUser, InlineMessage, Input, Spinner } from '../../shared/ds';
import { FieldHint, FieldIcon, FORM_SECTION_GRID_STYLE, FormSection } from './FormSection';
import { SshKeyDropZone } from './SshKeyDropZone';
import type { LocalSystemCredentials, ScanError } from './scanTypes';

const MAX_KEY_FILE_BYTES = 16_384;

const DEFAULT_PORT = 22;
const PATH_PATTERN = /^(\/|~(\/.*)?)$|^\/.*$/;

interface FormValues {
  host: string;
  port: string;
  username: string;
  // The key's contents live only here, never bound to a visible input — the
  // user uploads the file, they don't type or paste it into view.
  privateKey: string;
  privateKeyFileName: string;
  passphrase: string;
  path: string;
}

type FieldErrors = Partial<Record<keyof FormValues, string>>;


const INITIAL_VALUES: FormValues = {
  host: '',
  port: String(DEFAULT_PORT),
  username: 'root',
  privateKey: '',
  privateKeyFileName: '',
  passphrase: '',
  path: '/',
};

function validate(values: FormValues): FieldErrors {
  const errors: FieldErrors = {};
  if (values.host.trim().length === 0) errors.host = 'Enter the IP address or hostname';
  const port = Number(values.port);
  if (!Number.isInteger(port) || port < 1 || port > 65535) errors.port = 'Enter a port between 1 and 65535';
  if (values.username.trim().length === 0) errors.username = 'Enter the SSH username';
  if (values.privateKey.trim().length === 0) errors.privateKey = 'Upload the SSH private key file';
  if (!PATH_PATTERN.test(values.path.trim())) errors.path = 'Enter an absolute path (e.g. /var/data) or start with ~';
  return errors;
}

function readKeyFile(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    if (file.size > MAX_KEY_FILE_BYTES) {
      reject(new Error("That file is too large to be a private key."));
      return;
    }
    const reader = new FileReader();
    reader.onload = () => resolve(typeof reader.result === 'string' ? reader.result : '');
    reader.onerror = () => reject(new Error('Could not read that file.'));
    reader.readAsText(file);
  });
}

function RevealButton({ revealed, onToggle }: { revealed: boolean; onToggle: () => void }) {
  return (
    <button
      type="button"
      onClick={onToggle}
      aria-label={revealed ? 'Hide passphrase' : 'Show passphrase'}
      aria-pressed={revealed}
      style={{ border: 'none', background: 'transparent', cursor: 'pointer', color: revealed ? 'var(--accent-primary)' : 'var(--text-muted)', display: 'inline-flex', padding: 0 }}
    >
      <IconEye size={16} />
    </button>
  );
}

interface SectionProps {
  values: FormValues;
  errors: FieldErrors;
  disabled: boolean;
  set: <K extends keyof FormValues>(key: K, value: string) => void;
}

function ServerSection({ values, errors, disabled, set }: SectionProps) {
  return (
    <FormSection step={1} title="Server" hint="Where the VM lives on the network.">
      <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) 110px', gap: 14 }}>
        <Input
          label="IP address or hostname"
          aria-label="IP address or hostname"
          placeholder="203.0.113.10"
          leading={<FieldIcon><IconServer size={16} /></FieldIcon>}
          value={values.host}
          onChange={(e) => set('host', e.target.value)}
          error={errors.host}
          disabled={disabled}
          autoComplete="off"
          spellCheck={false}
          autoFocus
        />
        <Input
          label="Port"
          aria-label="SSH port"
          type="number"
          min={1}
          max={65535}
          value={values.port}
          onChange={(e) => set('port', e.target.value)}
          error={errors.port}
          disabled={disabled}
        />
      </div>
    </FormSection>
  );
}

interface AuthSectionProps extends SectionProps {
  onKeySelected: (file: File) => void;
  onKeyCleared: () => void;
}

function AuthSection({ values, errors, disabled, set, onKeySelected, onKeyCleared }: AuthSectionProps) {
  const [revealed, setRevealed] = useState(false);
  return (
    <FormSection step={2} title="Authentication" hint="Sign in with an SSH key. Passwords are not supported.">
      <Input
        label="SSH username"
        aria-label="SSH username"
        placeholder="root"
        leading={<FieldIcon><IconUser size={16} /></FieldIcon>}
        value={values.username}
        onChange={(e) => set('username', e.target.value)}
        error={errors.username}
        disabled={disabled}
        autoComplete="off"
      />
      <SshKeyDropZone
        fileName={values.privateKeyFileName}
        error={errors.privateKey}
        disabled={disabled}
        onSelect={onKeySelected}
        onClear={onKeyCleared}
      />
      <Input
        label="Key passphrase (optional)"
        aria-label="Private key passphrase"
        type={revealed ? 'text' : 'password'}
        placeholder="Leave blank if the key isn't encrypted"
        leading={<FieldIcon><IconLock size={16} /></FieldIcon>}
        trailing={<RevealButton revealed={revealed} onToggle={() => setRevealed((v) => !v)} />}
        value={values.passphrase}
        onChange={(e) => set('passphrase', e.target.value)}
        disabled={disabled}
        autoComplete="off"
      />
    </FormSection>
  );
}

function FolderSection({ values, errors, disabled, set }: SectionProps) {
  return (
    <FormSection step={3} title="Folder" hint="The folder to open first. You can move around after connecting.">
      <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        <Input
          label="Folder to browse"
          aria-label="Folder to browse"
          placeholder="/home/ubuntu/project"
          leading={<FieldIcon><IconFolder size={16} /></FieldIcon>}
          value={values.path}
          onChange={(e) => set('path', e.target.value)}
          error={errors.path}
          disabled={disabled}
          autoComplete="off"
          spellCheck={false}
        />
        {!errors.path && <FieldHint>Use an absolute path like /var/data, or start with ~ for the user's home folder.</FieldHint>}
      </div>
    </FormSection>
  );
}

interface LocalSystemConnectFormProps {
  connecting: boolean;
  error?: ScanError;
  onConnect: (credentials: LocalSystemCredentials, path: string) => void;
}

export function LocalSystemConnectForm({ connecting, error, onConnect }: LocalSystemConnectFormProps) {
  const [values, setValues] = useState<FormValues>(INITIAL_VALUES);
  const [errors, setErrors] = useState<FieldErrors>({});

  function set<K extends keyof FormValues>(key: K, value: string) {
    setValues((v) => ({ ...v, [key]: value }));
  }

  async function onKeyFileSelected(file: File) {
    try {
      const contents = await readKeyFile(file);
      setValues((v) => ({ ...v, privateKey: contents, privateKeyFileName: file.name }));
      setErrors((e) => ({ ...e, privateKey: undefined }));
    } catch (err) {
      setValues((v) => ({ ...v, privateKey: '', privateKeyFileName: '' }));
      setErrors((e) => ({ ...e, privateKey: err instanceof Error ? err.message : 'Could not read that file.' }));
    }
  }

  function onKeyFileCleared() {
    setValues((v) => ({ ...v, privateKey: '', privateKeyFileName: '' }));
  }

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const found = validate(values);
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    const credentials: LocalSystemCredentials = {
      host: values.host,
      port: Number(values.port),
      username: values.username,
      privateKey: values.privateKey,
      passphrase: values.passphrase.trim() || undefined,
    };
    onConnect(credentials, values.path);
  }

  const sectionProps: SectionProps = { values, errors, disabled: connecting, set };

  return (
    <form onSubmit={onSubmit} noValidate style={{ display: 'flex', flexDirection: 'column', gap: 16, width: '100%' }}>
      <div style={FORM_SECTION_GRID_STYLE}>
        <ServerSection {...sectionProps} />
        <AuthSection {...sectionProps} onKeySelected={(file) => void onKeyFileSelected(file)} onKeyCleared={onKeyFileCleared} />
        <FolderSection {...sectionProps} />
      </div>
      {error && (
        <InlineMessage variant="danger" correlationId={error.correlationId}>
          {error.message}
        </InlineMessage>
      )}
      <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
        <Button type="submit" disabled={connecting} icon={connecting ? <Spinner size={14} /> : undefined}>
          {connecting ? 'Connecting to the VM…' : 'Connect and browse files'}
        </Button>
      </div>
    </form>
  );
}
