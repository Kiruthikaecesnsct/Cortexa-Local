import { useEffect, useRef } from 'react';
import { Button, Skeleton, ErrorState, CorrelationTag } from '../../../shared/ds';
import { useToast } from '../../../shared/ds/Toast';
import { useSession } from '../../../core/auth/useSession';
import { relativeTime } from '../../../shared/utils';
import { usePatentConfigStore } from '../usePatentConfigStore';
import { SourceRow, type CredentialBadgeSpec, type SourceDef } from '../SourceRow';
import type { PatentSourceId } from '../patentApiTypes';

const PATENT_SOURCE_DEFS: SourceDef[] = [
  {
    id: 'uspto',
    name: 'USPTO',
    code: 'U',
    subLabel: 'uspto-api-key · United States Patent and Trademark Office',
    fields: [{ name: 'apiKey', label: 'New USPTO API key', placeholder: 'Paste new key — we never display it back' }],
  },
  {
    id: 'epo',
    name: 'EPO OPS',
    code: 'E',
    subLabel: 'epo-consumer-key + epo-oauth-secret · European Patent Office Open Patent Services',
    fields: [
      { name: 'consumerKey', label: 'New consumer key', placeholder: 'Paste consumer key' },
      { name: 'oauthSecret', label: 'New OAuth secret', placeholder: 'Paste OAuth secret' },
    ],
  },
  {
    id: 'lens',
    name: 'Lens',
    code: 'L',
    subLabel: 'lens-api-key · Lens.org scholarly + patent data',
    fields: [{ name: 'apiKey', label: 'New Lens API key', placeholder: 'Paste new key — we never display it back' }],
  },
];

type PatentConfigStore = ReturnType<typeof usePatentConfigStore>;

const ENABLED_KEY: Record<PatentSourceId, 'usptoEnabled' | 'epoEnabled' | 'lensEnabled'> = {
  uspto: 'usptoEnabled',
  epo: 'epoEnabled',
  lens: 'lensEnabled',
};

function credentialBadgesFor(source: PatentSourceId, state: PatentConfigStore['state']): CredentialBadgeSpec[] {
  if (source === 'epo') {
    return [
      { status: state.epoCredentialStatus.consumer, setLabel: 'Consumer key: set', notSetLabel: 'Consumer key: not set' },
      { status: state.epoCredentialStatus.oauth, setLabel: 'OAuth secret: set', notSetLabel: 'OAuth secret: not set' },
    ];
  }
  const status = source === 'uspto' ? state.usptoCredentialStatus : state.lensCredentialStatus;
  return [{ status, setLabel: 'Key set', notSetLabel: 'No key' }];
}

function useSaveToast(saveSuccess: boolean) {
  const { show } = useToast();
  const savedRef = useRef(false);
  useEffect(() => {
    if (saveSuccess && !savedRef.current) {
      savedRef.current = true;
      show({ variant: 'success', title: 'Patent source configuration saved.' });
    }
    if (!saveSuccess) savedRef.current = false;
  }, [saveSuccess, show]);
}

const SAVE_ERROR_BANNER_ID = 'patent-config-save-error';

function SaveErrorBanner({ store }: { store: PatentConfigStore }) {
  const { saveError } = store.state;
  if (!saveError) return null;
  return (
    <div
      id={SAVE_ERROR_BANNER_ID}
      role="alert"
      aria-live="polite"
      style={{ color: 'var(--status-danger-fg)', fontSize: 'var(--text-sm)', display: 'flex', flexDirection: 'column', gap: 8 }}
    >
      <span>{saveError.message}</span>
      {saveError.correlationId && <CorrelationTag correlationId={saveError.correlationId} />}
    </div>
  );
}

function SaveActions({ store, canWrite }: { store: PatentConfigStore; canWrite: boolean }) {
  if (!canWrite) return null;
  const canSave = store.isDirty && !store.state.saving;
  return (
    <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
      <Button variant="primary" disabled={!canSave} onClick={() => void store.save()}>
        {store.state.saving ? 'Saving…' : 'Save changes'}
      </Button>
      {store.isDirty && (
        <Button variant="ghost" onClick={() => store.cancel()}>
          Cancel
        </Button>
      )}
      <span style={{ color: 'var(--text-muted)', fontSize: 'var(--text-sm)' }}>
        Enable toggles are saved together. Key writes and tests apply immediately, per source.
      </span>
    </div>
  );
}

function TabHeader({ configVersion, updatedAt }: { configVersion: number; updatedAt: string }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap' }}>
      <div>
        <div style={{ fontWeight: 600, fontSize: 'var(--text-lg)', color: 'var(--text-primary)' }}>Patent Sources</div>
        <div style={{ color: 'var(--text-muted)', fontSize: 'var(--text-sm)' }}>
          Enable the live patent APIs evidence should query, and manage their credentials. Keys are written to Key Vault and never
          shown again.
        </div>
      </div>
      {updatedAt && (
        <span
          title="Version of the saved configuration"
          style={{
            fontFamily: 'var(--font-mono)',
            fontSize: 'var(--text-xs)',
            color: 'var(--text-muted)',
            background: 'var(--surface-sunken)',
            border: '1px solid var(--border-subtle)',
            borderRadius: 'var(--radius-sm)',
            padding: '4px 10px',
          }}
        >
          {`config v${configVersion} · saved ${relativeTime(updatedAt)}`}
        </span>
      )}
    </div>
  );
}

function PatentApiContent({ store, canWrite }: { store: PatentConfigStore; canWrite: boolean }) {
  const { state } = store;
  useSaveToast(state.saveSuccess);
  const readOnly = !canWrite;

  return (
    <>
      <TabHeader configVersion={state.configVersion} updatedAt={state.updatedAt} />
      {PATENT_SOURCE_DEFS.map((def) => (
        <SourceRow
          key={def.id}
          def={def}
          enabled={state[ENABLED_KEY[def.id]]}
          readOnly={readOnly}
          credentialBadges={credentialBadgesFor(def.id, state)}
          testResult={state.testResults[def.id]}
          onToggleEnabled={(next) => store.setEnabled(def.id, next)}
          onSubmitSecret={(payload) => store.submitSecret(def.id, payload)}
          onTestConnection={(payload) => void store.testConnection(def.id, payload)}
        />
      ))}
      <SaveErrorBanner store={store} />
      <SaveActions store={store} canWrite={canWrite} />
    </>
  );
}

export function PatentApiTab() {
  const { hasPermission } = useSession();
  const canWrite = hasPermission('admin:config:write');
  const store = usePatentConfigStore();
  const { state } = store;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20, maxWidth: 900 }}>
      {!canWrite && (
        <div style={{ padding: '12px 16px', borderRadius: 'var(--radius-md)', background: 'var(--status-info-bg)', color: 'var(--status-info-fg)', fontSize: 13 }}>
          Read-only — you need Admin access to change patent source configuration.
        </div>
      )}

      {state.loading ? (
        <Skeleton height={210} radius="var(--radius-lg)" />
      ) : state.error ? (
        <ErrorState title="Couldn't load patent configuration" message={state.error} onRetry={() => void store.retry()} />
      ) : (
        <PatentApiContent store={store} canWrite={canWrite} />
      )}
    </div>
  );
}
