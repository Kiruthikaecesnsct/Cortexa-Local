import { useEffect, useRef } from 'react';
import { Button, Select, Skeleton, ErrorState, CorrelationTag } from '../../../shared/ds';
import { useToast } from '../../../shared/ds/Toast';
import { useSession } from '../../../core/auth/useSession';
import { useModelConfigStore, STAGE_LABELS } from '../useModelConfigStore';
import type { ModelDto, PipelineStage, SeedingMode } from '../settingsTypes';

const cardStyle: React.CSSProperties = {
  background: 'var(--surface-card)',
  border: '1px solid var(--border-subtle)',
  borderRadius: 'var(--radius-lg)',
  boxShadow: 'var(--shadow-xs)',
  padding: 28,
};

const SAVE_ERROR_BANNER_ID = 'model-config-save-error';
const SEEDING_MODE_HELP_ID = 'seeding-mode-help';

const SEEDING_MODE_OPTIONS: { value: string; label: string }[] = [
  { value: 'legacy', label: 'Legacy — candidate whitespace pass' },
  { value: 'deep', label: 'Deep (experimental) — memory → digest → landscape → ideation → validation' },
];

const SEEDING_MODE_HELP =
  'Controls how the seeding engine generates ideas. Deep mode is experimental and runs a longer grounded chain — expect slower, more exploratory runs. A change applies to the next batch you start; batches already in flight keep the mode they were snapshotted with. No redeploy needed.';

function zeroOptionErrorId(stage: PipelineStage): string {
  return `${stage}-zero-option-error`;
}

function toSelectOptions(models: ModelDto[]): { value: string; label: string }[] {
  return models.map((m) => ({ value: m.id, label: m.label }));
}

interface StageSlot {
  stage: PipelineStage;
  options: ModelDto[];
  value: string;
  onChange: (id: string) => void;
}

interface StageSelectProps {
  slot: StageSlot;
  readOnly: boolean;
  hasValidationError: boolean;
  hasSaveError: boolean;
  zeroOptionStages: PipelineStage[];
}

function StageSelect({ slot, readOnly, hasValidationError, hasSaveError, zeroOptionStages }: StageSelectProps) {
  const label = `${STAGE_LABELS[slot.stage]} Model`;
  const zeroOption = zeroOptionStages.includes(slot.stage);
  const offending = hasValidationError && !slot.options.some((m) => m.id === slot.value);

  const describedByIds: string[] = [];
  if (zeroOption) describedByIds.push(zeroOptionErrorId(slot.stage));
  if (offending || hasSaveError) describedByIds.push(SAVE_ERROR_BANNER_ID);

  return (
    <Select
      id={`${slot.stage}-model-select`}
      aria-label={label}
      label={label}
      options={zeroOption ? [] : toSelectOptions(slot.options)}
      placeholder={zeroOption ? 'No compatible model available' : 'Select'}
      value={zeroOption ? '' : slot.value}
      disabled={readOnly || zeroOption}
      aria-describedby={describedByIds.length > 0 ? describedByIds.join(' ') : undefined}
      onChange={(e) => slot.onChange(e.target.value)}
    />
  );
}

function CatalogRow({ model, assigned }: { model: ModelDto; assigned: boolean }) {
  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 12,
        border: '1px solid var(--border-subtle)',
        borderRadius: 10,
        padding: 14,
        opacity: model.enabled ? 1 : 0.5,
      }}
    >
      <div style={{ width: 34, height: 34, borderRadius: 8, background: 'var(--accent-grad)', color: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center', fontWeight: 700, fontSize: 14, flexShrink: 0 }}>
        {model.label[0]}
      </div>
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ fontWeight: 600, fontSize: 13.5, color: 'var(--text-primary)' }}>{model.label}</div>
        <div style={{ fontSize: 11.5, color: 'var(--text-muted)', fontFamily: 'var(--font-mono)' }}>{model.provider}</div>
      </div>
      <span style={{ fontSize: 11, fontWeight: 600, padding: '3px 9px', borderRadius: 999, background: 'var(--status-success-bg)', color: 'var(--status-success-fg)' }}>
        {model.role}
      </span>
      {!model.enabled && (
        <span style={{ fontSize: 11, padding: '3px 9px', borderRadius: 999, background: 'var(--surface-sunken)', color: 'var(--text-muted)' }}>Unavailable</span>
      )}
      {assigned && (
        <span
          aria-label="Assigned"
          style={{ width: 20, height: 20, borderRadius: '50%', background: 'var(--accent-grad)', color: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 11, flexShrink: 0 }}
        >
          ✓
        </span>
      )}
    </div>
  );
}

type ModelConfigStore = ReturnType<typeof useModelConfigStore>;

function useSaveToast(saveSuccess: boolean) {
  const { show } = useToast();
  const savedRef = useRef(false);
  useEffect(() => {
    if (saveSuccess && !savedRef.current) {
      savedRef.current = true;
      show({ variant: 'success', title: 'Model configuration saved.' });
    }
    if (!saveSuccess) savedRef.current = false;
  }, [saveSuccess, show]);
}

function useErrorFocus(currentError: string | null) {
  const errorBannerRef = useRef<HTMLDivElement>(null);
  const prevErrorRef = useRef<string | null>(null);
  useEffect(() => {
    if (currentError && currentError !== prevErrorRef.current) {
      errorBannerRef.current?.focus();
    }
    prevErrorRef.current = currentError;
  }, [currentError]);
  return errorBannerRef;
}

const alertStyle: React.CSSProperties = { color: 'var(--status-danger-fg)', fontSize: 'var(--text-sm)' };

function StageSelectGrid({ store, readOnly }: { store: ModelConfigStore; readOnly: boolean }) {
  const { state } = store;
  const slots: StageSlot[] = [
    { stage: 'extraction', options: store.extractionOptions, value: state.extractionModel, onChange: store.setExtractionModel },
    { stage: 'scoring', options: store.scoringOptions, value: state.scoringModel, onChange: store.setScoringModel },
    { stage: 'evidence', options: store.evidenceOptions, value: state.primaryEvidenceModel, onChange: store.setPrimaryEvidenceModel },
    { stage: 'seeding', options: store.seedingOptions, value: state.seedingModel, onChange: store.setSeedingModel },
  ];
  const hasValidationError = Boolean(state.validationError);
  const hasSaveError = Boolean(state.saveError);

  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2,1fr)', gap: 20 }}>
      {slots.map((slot) => (
        <StageSelect
          key={slot.stage}
          slot={slot}
          readOnly={readOnly}
          hasValidationError={hasValidationError}
          hasSaveError={hasSaveError}
          zeroOptionStages={store.zeroOptionStages}
        />
      ))}
    </div>
  );
}

function SeedingModeRow({ store, readOnly }: { store: ModelConfigStore; readOnly: boolean }) {
  const { state } = store;
  const describedByIds = [SEEDING_MODE_HELP_ID];
  if (state.saveError) describedByIds.push(SAVE_ERROR_BANNER_ID);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      <div style={{ borderTop: '1px solid var(--border-subtle)' }} />
      <div style={{ maxWidth: 420 }}>
        <Select
          id="seeding-mode-select"
          aria-label="Seeding Mode"
          label="Seeding Mode"
          options={SEEDING_MODE_OPTIONS}
          value={state.seedingMode}
          disabled={readOnly}
          aria-describedby={describedByIds.join(' ')}
          onChange={(e) => store.setSeedingMode(e.target.value as SeedingMode)}
        />
      </div>
      <div id={SEEDING_MODE_HELP_ID} style={{ maxWidth: 420, color: 'var(--text-muted)', fontSize: 'var(--text-sm)' }}>
        {SEEDING_MODE_HELP}
      </div>
    </div>
  );
}

function ZeroOptionAlerts({ stages }: { stages: PipelineStage[] }) {
  return (
    <>
      {stages.map((stage) => (
        <div key={stage} id={zeroOptionErrorId(stage)} role="alert" aria-live="polite" style={alertStyle}>
          No enabled model is available for the {STAGE_LABELS[stage]} stage. Contact an administrator.
        </div>
      ))}
    </>
  );
}

function InlineErrorBanner({ state }: { state: ModelConfigStore['state'] }) {
  const errorBannerRef = useErrorFocus(state.validationError ?? state.saveError?.message ?? null);
  if (!state.validationError && !state.saveError) return null;

  return (
    <div ref={errorBannerRef} id={SAVE_ERROR_BANNER_ID} role="alert" aria-live="polite" tabIndex={-1} style={{ ...alertStyle, display: 'flex', flexDirection: 'column', gap: 8 }}>
      <span>{state.validationError ?? state.saveError?.message}</span>
      {state.saveError?.correlationId && <CorrelationTag correlationId={state.saveError.correlationId} />}
    </div>
  );
}

function SaveActions({ store, canSave }: { store: ModelConfigStore; canSave: boolean }) {
  return (
    <div style={{ display: 'flex', gap: 10 }}>
      <Button variant="primary" disabled={!canSave} onClick={() => void store.save()}>
        {store.state.saving ? 'Saving…' : 'Save Changes'}
      </Button>
      {store.isDirty && (
        <Button variant="ghost" onClick={() => store.cancel()}>
          Cancel
        </Button>
      )}
    </div>
  );
}

function ModelConfigCard({ store, canWrite, readOnly }: { store: ModelConfigStore; canWrite: boolean; readOnly: boolean }) {
  const { state } = store;
  useSaveToast(state.saveSuccess);
  const canSave = canWrite && store.isDirty && !state.saving && store.zeroOptionStages.length === 0;

  return (
    <div style={{ ...cardStyle, display: 'flex', flexDirection: 'column', gap: 20 }}>
      <div style={{ fontWeight: 600, fontSize: 18, color: 'var(--text-primary)' }}>Per-Stage Model Configuration</div>
      <StageSelectGrid store={store} readOnly={readOnly} />
      <SeedingModeRow store={store} readOnly={readOnly} />
      <ZeroOptionAlerts stages={store.zeroOptionStages} />
      <InlineErrorBanner state={state} />
      {canWrite && <SaveActions store={store} canSave={canSave} />}
    </div>
  );
}

function CatalogCard({ models, usedIds }: { models: ModelDto[]; usedIds: Set<string> }) {
  return (
    <div style={cardStyle}>
      <div style={{ fontWeight: 600, fontSize: 18, color: 'var(--text-primary)', marginBottom: 4 }}>Model Catalog</div>
      <div style={{ fontSize: 13, color: 'var(--text-muted)', marginBottom: 18 }}>
        Models available to the pipeline. A checkmark shows where a model is currently assigned above.
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2,1fr)', gap: 14 }}>
        {models.map((m) => (
          <CatalogRow key={m.id} model={m} assigned={usedIds.has(m.id)} />
        ))}
      </div>
    </div>
  );
}

export function ModelConfigTab() {
  const { hasPermission } = useSession();
  const canWrite = hasPermission('admin:config:write');
  const readOnly = !canWrite;
  const store = useModelConfigStore();
  const { state } = store;

  const usedIds = new Set([state.extractionModel, state.primaryEvidenceModel, state.scoringModel, state.seedingModel]);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20, maxWidth: 900 }}>
      {readOnly && (
        <div style={{ padding: '12px 16px', borderRadius: 'var(--radius-md)', background: 'var(--status-info-bg)', color: 'var(--status-info-fg)', fontSize: 13 }}>
          Read-only — you need Admin access to change model configuration.
        </div>
      )}

      {state.loading ? (
        <Skeleton height={210} radius="var(--radius-lg)" />
      ) : state.error ? (
        <ErrorState title="Couldn't load model configuration" message={state.error} onRetry={() => void store.retry()} />
      ) : (
        <>
          <ModelConfigCard store={store} canWrite={canWrite} readOnly={readOnly} />
          <CatalogCard models={state.models} usedIds={usedIds} />
        </>
      )}
    </div>
  );
}
