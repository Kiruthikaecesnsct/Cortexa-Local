import { useRef, useState, type ChangeEvent, type FormEvent } from 'react';
import { Button, IconEye, IconFile, IconFolder, IconLock, IconServer, IconUpload, IconUser, InlineMessage, Input } from '../../shared/ds';
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
      style={{ border: 'none', background: 'transparent', cursor: 'pointer', color: 'var(--text-muted)', display: 'inline-flex', padding: 0 }}
    >
      <IconEye size={16} />
    </button>
  );
}

function PrivateKeyField({
  fileName,
  error,
  disabled,
  onSelect,
  onClear,
}: {
  fileName: string;
  error?: string;
  disabled: boolean;
  onSelect: (file: File) => void;
  onClear: () => void;
}) {
  const inputRef = useRef<HTMLInputElement>(null);

  function onFileInputChange(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    // Reset so selecting the same file again still fires onChange.
    event.target.value = '';
    if (file) onSelect(file);
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8, fontFamily: 'var(--font-body)' }}>
      <label style={{ fontSize: 'var(--text-sm)', fontWeight: 500, color: 'var(--text-primary)' }}>3. SSH private key</label>
      <input ref={inputRef} type="file" hidden aria-hidden="true" onChange={onFileInputChange} disabled={disabled} />
      {fileName ? (
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 10,
            padding: '10px 14px',
            borderRadius: 'var(--radius-md)',
            background: 'var(--surface-card)',
            border: `1px solid ${error ? 'var(--status-danger-fg)' : 'var(--border-subtle)'}`,
          }}
        >
          <span style={{ color: 'var(--text-muted)', display: 'inline-flex' }}><IconFile size={16} /></span>
          <span style={{ flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', fontFamily: 'var(--font-mono)', fontSize: 'var(--text-sm)' }}>
            {fileName}
          </span>
          <Button size="sm" variant="ghost" onClick={onClear} disabled={disabled}>
            Remove
          </Button>
        </div>
      ) : (
        <Button
          type="button"
          variant="secondary"
          icon={<IconUpload size={16} />}
          onClick={() => inputRef.current?.click()}
          disabled={disabled}
        >
          Upload private key file
        </Button>
      )}
      {error && <span style={{ fontSize: 'var(--text-xs)', color: 'var(--status-danger-fg)' }}>{error}</span>}
      <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-muted)', lineHeight: 1.5 }}>
        Read once in your browser for this connection — never shown on screen, never stored.
      </span>
    </div>
  );
}

interface LocalSystemConnectFormProps {
  connecting: boolean;
  error?: ScanError;
  onConnect: (credentials: LocalSystemCredentials, path: string) => void;
}

export function LocalSystemConnectForm({ connecting, error, onConnect }: LocalSystemConnectFormProps) {
  const [values, setValues] = useState<FormValues>({
    host: '',
    port: String(DEFAULT_PORT),
    username: 'root',
    privateKey: '',
    privateKeyFileName: '',
    passphrase: '',
    path: '/',
  });
  const [revealed, setRevealed] = useState(false);
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

  return (
    <form onSubmit={onSubmit} noValidate style={{ display: 'flex', flexDirection: 'column', gap: 18, maxWidth: 560 }}>
      <div style={{ display: 'flex', gap: 14 }}>
        <div style={{ flex: 3, minWidth: 0 }}>
          <Input
            label="1. IP address or hostname"
            aria-label="IP address or hostname"
            placeholder="203.0.113.10"
            leading={<span style={{ color: 'var(--text-muted)', display: 'inline-flex' }}><IconServer size={16} /></span>}
            value={values.host}
            onChange={(e) => set('host', e.target.value)}
            error={errors.host}
            disabled={connecting}
            autoComplete="off"
            spellCheck={false}
            autoFocus
          />
        </div>
        <div style={{ flex: 1, minWidth: 90 }}>
          <Input
            label="Port"
            aria-label="SSH port"
            type="number"
            min={1}
            max={65535}
            value={values.port}
            onChange={(e) => set('port', e.target.value)}
            error={errors.port}
            disabled={connecting}
          />
        </div>
      </div>
      <Input
        label="2. SSH username"
        aria-label="SSH username"
        placeholder="root"
        leading={<span style={{ color: 'var(--text-muted)', display: 'inline-flex' }}><IconUser size={16} /></span>}
        value={values.username}
        onChange={(e) => set('username', e.target.value)}
        error={errors.username}
        disabled={connecting}
        autoComplete="off"
      />
      <PrivateKeyField
        fileName={values.privateKeyFileName}
        error={errors.privateKey}
        disabled={connecting}
        onSelect={(file) => void onKeyFileSelected(file)}
        onClear={onKeyFileCleared}
      />
      <Input
        label="4. Private key passphrase (optional)"
        aria-label="Private key passphrase"
        type={revealed ? 'text' : 'password'}
        placeholder="Leave blank if the key isn't encrypted"
        leading={<span style={{ color: 'var(--text-muted)', display: 'inline-flex' }}><IconLock size={16} /></span>}
        trailing={<RevealButton revealed={revealed} onToggle={() => setRevealed((v) => !v)} />}
        value={values.passphrase}
        onChange={(e) => set('passphrase', e.target.value)}
        disabled={connecting}
        autoComplete="off"
      />
      <Input
        label="5. Folder to browse"
        aria-label="Folder to browse"
        placeholder="/home/ubuntu/project"
        leading={<span style={{ color: 'var(--text-muted)', display: 'inline-flex' }}><IconFolder size={16} /></span>}
        value={values.path}
        onChange={(e) => set('path', e.target.value)}
        error={errors.path}
        disabled={connecting}
        autoComplete="off"
        spellCheck={false}
      />
      {error && (
        <InlineMessage variant="danger" correlationId={error.correlationId}>
          {error.message}
        </InlineMessage>
      )}
      <div>
        <Button type="submit" disabled={connecting}>
          {connecting ? 'Connecting to the VM…' : '6. Connect and browse files'}
        </Button>
      </div>
    </form>
  );
}
