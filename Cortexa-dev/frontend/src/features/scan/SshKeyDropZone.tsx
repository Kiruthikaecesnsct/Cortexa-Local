import { useRef, useState, type ChangeEvent, type DragEvent } from 'react';
import { Button, IconCheck, IconFile, IconUpload } from '../../shared/ds';

interface SshKeyDropZoneProps {
  fileName: string;
  error?: string;
  disabled: boolean;
  onSelect: (file: File) => void;
  onClear: () => void;
}

const LABEL_STYLE = { fontSize: 'var(--text-sm)', fontWeight: 500, color: 'var(--text-primary)' } as const;

function borderColor(error: string | undefined, dragging: boolean): string {
  if (error) return 'var(--status-danger-fg)';
  return dragging ? 'var(--accent-primary)' : 'var(--border-strong)';
}

function SelectedKey({ fileName, disabled, onClear }: Pick<SshKeyDropZoneProps, 'fileName' | 'disabled' | 'onClear'>) {
  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 12,
        padding: '12px 14px',
        borderRadius: 'var(--radius-md)',
        background: 'var(--status-success-bg)',
        border: '1px solid var(--border-subtle)',
      }}
    >
      <span style={{ color: 'var(--status-success-fg)', display: 'inline-flex' }}><IconCheck size={18} /></span>
      <span style={{ display: 'flex', flexDirection: 'column', gap: 2, flex: 1, minWidth: 0 }}>
        <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', fontFamily: 'var(--font-mono)', fontSize: 'var(--text-sm)', color: 'var(--text-primary)' }}>
          {fileName}
        </span>
        <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-muted)' }}>Key loaded</span>
      </span>
      <Button size="sm" variant="ghost" onClick={onClear} disabled={disabled}>
        Remove
      </Button>
    </div>
  );
}

function EmptyDropZone({ error, disabled, onSelect }: Pick<SshKeyDropZoneProps, 'error' | 'disabled' | 'onSelect'>) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);

  function onFileInputChange(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    // Reset so selecting the same file again still fires onChange.
    event.target.value = '';
    if (file) onSelect(file);
  }

  function onDragOver(event: DragEvent<HTMLDivElement>) {
    event.preventDefault();
    if (!disabled) setDragging(true);
  }

  function onDrop(event: DragEvent<HTMLDivElement>) {
    event.preventDefault();
    setDragging(false);
    const file = event.dataTransfer.files?.[0];
    if (file && !disabled) onSelect(file);
  }

  return (
    <div
      onDragOver={onDragOver}
      onDragLeave={() => setDragging(false)}
      onDrop={onDrop}
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: 10,
        padding: '22px 16px',
        textAlign: 'center',
        borderRadius: 'var(--radius-md)',
        border: `1.5px dashed ${borderColor(error, dragging)}`,
        background: dragging ? 'var(--accent-primary-subtle)' : 'var(--surface-sunken)',
        transition: 'background var(--duration-fast) var(--ease-standard), border-color var(--duration-fast) var(--ease-standard)',
      }}
    >
      <input ref={inputRef} type="file" hidden aria-hidden="true" onChange={onFileInputChange} disabled={disabled} />
      <span style={{ color: 'var(--accent-primary)', display: 'inline-flex' }}><IconFile size={22} /></span>
      <span style={{ fontSize: 'var(--text-sm)', color: 'var(--text-primary)' }}>Drag your key file here, or</span>
      <Button type="button" size="sm" variant="secondary" icon={<IconUpload size={14} />} onClick={() => inputRef.current?.click()} disabled={disabled}>
        Choose file
      </Button>
    </div>
  );
}

export function SshKeyDropZone({ fileName, error, disabled, onSelect, onClear }: SshKeyDropZoneProps) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8, fontFamily: 'var(--font-body)' }}>
      <span style={LABEL_STYLE}>SSH private key</span>
      {fileName ? (
        <SelectedKey fileName={fileName} disabled={disabled} onClear={onClear} />
      ) : (
        <EmptyDropZone error={error} disabled={disabled} onSelect={onSelect} />
      )}
      {error && <span style={{ fontSize: 'var(--text-xs)', color: 'var(--status-danger-fg)' }}>{error}</span>}
      <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-muted)', lineHeight: 1.5 }}>
        Read once in your browser for this connection. Never shown on screen, never stored.
      </span>
    </div>
  );
}
