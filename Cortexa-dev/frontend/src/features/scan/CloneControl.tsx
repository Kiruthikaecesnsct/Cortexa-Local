import { useState } from 'react';
import { Badge, Button, InlineMessage, Spinner } from '../../shared/ds';
import { STATUS_LABELS, isInProgress } from './cloneFormat';
import type { RepositoryCloneDto, ScanError } from './scanTypes';
import type { CloneActionResult } from './useClones';

interface CloneControlProps {
  latest?: RepositoryCloneDto;
  onClone: () => Promise<CloneActionResult>;
}

function ControlButton({ latest, starting, onClick }: { latest?: RepositoryCloneDto; starting: boolean; onClick: () => void }) {
  if (starting || (latest && isInProgress(latest))) {
    return (
      <Button size="sm" disabled icon={<Spinner size={14} />}>
        {latest && !starting ? `${STATUS_LABELS[latest.status]}…` : 'Starting save…'}
      </Button>
    );
  }
  if (latest?.status === 'stored') {
    return (
      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
        <Badge tone="success">Saved</Badge>
        <Button size="sm" variant="secondary" onClick={onClick}>
          Save again
        </Button>
      </span>
    );
  }
  return (
    <Button size="sm" onClick={onClick}>
      {latest?.status === 'failed' ? 'Retry save' : 'Save repository'}
    </Button>
  );
}

export function CloneControl({ latest, onClone }: CloneControlProps) {
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState<ScanError | undefined>();

  async function handleClick() {
    setStarting(true);
    setError(undefined);
    const result = await onClone();
    setStarting(false);
    if (!result.ok) setError(result.error);
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 8 }}>
      <ControlButton latest={latest} starting={starting} onClick={() => void handleClick()} />
      {error && (
        <InlineMessage variant="danger" correlationId={error.correlationId}>
          {error.message}
        </InlineMessage>
      )}
    </div>
  );
}
