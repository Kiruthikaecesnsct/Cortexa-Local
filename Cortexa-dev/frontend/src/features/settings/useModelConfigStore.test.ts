import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { useModelConfigStore } from './useModelConfigStore';
import * as modelsRepository from './modelsRepository';
import * as configRepository from './configRepository';

vi.mock('./modelsRepository', async () => {
  const actual = await vi.importActual<typeof import('./modelsRepository')>('./modelsRepository');
  return {
    ...actual,
    fetchModels: vi.fn(),
  };
});
vi.mock('./configRepository');

const mockFetchModels = vi.mocked(modelsRepository.fetchModels);
const mockFetchConfig = vi.mocked(configRepository.fetchConfig);
const mockSaveConfig = vi.mocked(configRepository.saveConfig);

type StoreHook = ReturnType<typeof renderHook<ReturnType<typeof useModelConfigStore>, unknown>>['result'];

// Render the store and wait for the initial load to settle.
async function renderLoadedStore(): Promise<StoreHook> {
  const { result } = renderHook(() => useModelConfigStore());
  await waitFor(() => expect(result.current.state.loading).toBe(false));
  return result;
}

// Invoke save() inside act() and return its boolean result.
async function saveStore(result: StoreHook): Promise<boolean | undefined> {
  let saved: boolean | undefined;
  await act(async () => {
    saved = await result.current.save();
  });
  return saved;
}

// Render a loaded store, apply a mutation inside act(), then save — the shape
// every save-path test shares.
async function loadMutateSave(
  mutate: (store: StoreHook['current']) => void
): Promise<{ result: StoreHook; saved: boolean | undefined }> {
  const result = await renderLoadedStore();
  await act(async () => {
    mutate(result.current);
  });
  const saved = await saveStore(result);
  return { result, saved };
}

const modelsFixture = {
  models: [
    { id: 'gpt-5.5', label: 'GPT 5.5', provider: 'azure-foundry', enabled: true, role: 'primary' as const, capabilities: ['reasoning' as const], allowedStages: ['extraction', 'scoring', 'seeding'] },
    { id: 'gpt-5.4', label: 'GPT 5.4', provider: 'azure-foundry', enabled: true, role: 'primary' as const, capabilities: ['reasoning' as const], allowedStages: ['evidence', 'scoring'] },
    { id: 'grok-4.3', label: 'Grok 4.3', provider: 'xai', enabled: true, role: 'secondary' as const, capabilities: ['grounding' as const], allowedStages: ['extraction', 'evidence', 'scoring', 'seeding'] },
    { id: 'claude-opus-4-8', label: 'Claude Opus 4.8', provider: 'anthropic', enabled: false, role: 'primary' as const, capabilities: ['reasoning' as const], allowedStages: ['extraction', 'evidence', 'scoring', 'seeding'] },
  ],
  dualModeAvailable: true,
  defaults: { single: 'gpt-5.5', dual: { primary: 'gpt-5.5', secondary: 'grok-4.3' } },
};

describe('useModelConfigStore', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchModels.mockResolvedValue({ ok: true, data: modelsFixture });
    mockFetchConfig.mockResolvedValue({
      ok: true,
      data: {
        extraction_model: 'gpt-5.5',
        primary_evidence_model: 'gpt-5.4',
        scoring_model: 'gpt-5.5',
        seeding_model: 'gpt-5.5',
      },
    });
  });

  it('initializes with the gpt-5.4 evidence default before load resolves', () => {
    mockFetchModels.mockReturnValue(new Promise(() => {}));
    mockFetchConfig.mockReturnValue(new Promise(() => {}));

    const { result } = renderHook(() => useModelConfigStore());

    expect(result.current.state.extractionModel).toBe('gpt-5.5');
    expect(result.current.state.primaryEvidenceModel).toBe('gpt-5.4');
    expect(result.current.state.scoringModel).toBe('gpt-5.5');
    expect(result.current.state.seedingModel).toBe('gpt-5.5');
  });

  it('excludes gpt-5.5 from the evidence stage options', async () => {
    const result = await renderLoadedStore();

    expect(result.current.evidenceOptions.map((m) => m.id)).not.toContain('gpt-5.5');
    expect(result.current.evidenceOptions.map((m) => m.id)).toEqual(['gpt-5.4', 'grok-4.3']);
  });

  it('excludes gpt-5.4 from the extraction stage options', async () => {
    const result = await renderLoadedStore();

    expect(result.current.extractionOptions.map((m) => m.id)).not.toContain('gpt-5.4');
    expect(result.current.extractionOptions.map((m) => m.id)).toEqual(['gpt-5.5', 'grok-4.3']);
  });

  it('reports zero-option stages when no enabled model allows a stage', async () => {
    mockFetchModels.mockResolvedValue({
      ok: true,
      data: { ...modelsFixture, models: modelsFixture.models.map((m) => ({ ...m, enabled: false })) },
    });

    const result = await renderLoadedStore();

    expect(result.current.zeroOptionStages).toEqual(expect.arrayContaining(['extraction', 'scoring', 'evidence', 'seeding']));
  });

  it('rejects save when the extraction model is not offered for the extraction stage', async () => {
    const { result, saved } = await loadMutateSave((s) => s.setExtractionModel('gpt-5.4'));

    expect(saved).toBe(false);
    expect(result.current.state.validationError).toContain('Extraction');
    expect(mockSaveConfig).not.toHaveBeenCalled();
  });

  it('rejects save when the evidence model is not offered for the evidence stage', async () => {
    const { result, saved } = await loadMutateSave((s) => s.setPrimaryEvidenceModel('gpt-5.5'));

    expect(saved).toBe(false);
    expect(result.current.state.validationError).toContain('Evidence');
    expect(mockSaveConfig).not.toHaveBeenCalled();
  });

  it('allows save when all selected models are valid for their stage', async () => {
    mockSaveConfig.mockResolvedValue({ ok: true });

    const { result, saved } = await loadMutateSave((s) => s.setScoringModel('gpt-5.4'));

    expect(saved).toBe(true);
    expect(result.current.state.validationError).toBeNull();
    expect(mockSaveConfig).toHaveBeenCalledTimes(1);
    expect(mockSaveConfig).toHaveBeenCalledWith({
      extraction_model: 'gpt-5.5',
      primary_evidence_model: 'gpt-5.4',
      scoring_model: 'gpt-5.4',
      seeding_model: 'gpt-5.5',
      seeding_mode: 'legacy',
    });
  });

  it('keeps a server save rejection inline and preserves the form fields', async () => {
    mockSaveConfig.mockResolvedValue({
      ok: false,
      error: { message: 'gpt-5.5 is not permitted for the evidence stage.', correlationId: 'corr-123' },
    });

    const { result, saved } = await loadMutateSave((s) => s.setScoringModel('gpt-5.4'));

    expect(saved).toBe(false);
    expect(result.current.state.error).toBeNull();
    expect(result.current.state.saveError?.message).toBe('gpt-5.5 is not permitted for the evidence stage.');
    expect(result.current.state.saveError?.correlationId).toBe('corr-123');
    expect(result.current.state.scoringModel).toBe('gpt-5.4');
    expect(result.current.state.extractionModel).toBe('gpt-5.5');
  });

  it('keeps a network save failure inline without touching the load error', async () => {
    mockSaveConfig.mockResolvedValue({
      ok: false,
      error: { message: 'Unable to connect to configuration service.' },
    });

    const { result, saved } = await loadMutateSave((s) => s.setExtractionModel('grok-4.3'));

    expect(saved).toBe(false);
    expect(result.current.state.error).toBeNull();
    expect(result.current.state.saveError?.message).toBe('Unable to connect to configuration service.');
  });

  it('rejects save when the seeding model is not offered for the seeding stage', async () => {
    const { result, saved } = await loadMutateSave((s) => s.setSeedingModel('gpt-5.4'));

    expect(saved).toBe(false);
    expect(result.current.state.validationError).toContain('Seeding');
    expect(mockSaveConfig).not.toHaveBeenCalled();
  });

  it('allows save when a valid seeding model is selected', async () => {
    mockSaveConfig.mockResolvedValue({ ok: true });

    const { result, saved } = await loadMutateSave((s) => s.setSeedingModel('grok-4.3'));

    expect(saved).toBe(true);
    expect(result.current.state.validationError).toBeNull();
    expect(mockSaveConfig).toHaveBeenCalledTimes(1);
    expect(mockSaveConfig).toHaveBeenCalledWith({
      extraction_model: 'gpt-5.5',
      primary_evidence_model: 'gpt-5.4',
      scoring_model: 'gpt-5.5',
      seeding_model: 'grok-4.3',
      seeding_mode: 'legacy',
    });
  });

  it('marks the form dirty when seeding model changes', async () => {
    const result = await renderLoadedStore();

    expect(result.current.isDirty).toBe(false);

    await act(async () => {
      result.current.setSeedingModel('grok-4.3');
    });

    expect(result.current.isDirty).toBe(true);
  });

  it('falls back to legacy seeding mode when the field is absent', async () => {
    const result = await renderLoadedStore();

    expect(result.current.state.seedingMode).toBe('legacy');
    expect(result.current.state.savedSnapshot?.seedingMode).toBe('legacy');
  });

  it('loads deep seeding mode from backend config', async () => {
    mockFetchConfig.mockResolvedValue({
      ok: true,
      data: {
        extraction_model: 'gpt-5.5',
        primary_evidence_model: 'gpt-5.4',
        scoring_model: 'gpt-5.5',
        seeding_model: 'gpt-5.5',
        seeding_mode: 'deep',
      },
    });

    const result = await renderLoadedStore();

    expect(result.current.state.seedingMode).toBe('deep');
    expect(result.current.state.savedSnapshot?.seedingMode).toBe('deep');
  });

  it('marks the form dirty when seeding mode changes', async () => {
    const result = await renderLoadedStore();

    expect(result.current.isDirty).toBe(false);

    await act(async () => {
      result.current.setSeedingMode('deep');
    });

    expect(result.current.isDirty).toBe(true);
    expect(result.current.state.seedingMode).toBe('deep');
  });

  it('restores the snapshot seeding mode on cancel', async () => {
    const result = await renderLoadedStore();

    await act(async () => {
      result.current.setSeedingMode('deep');
    });
    expect(result.current.isDirty).toBe(true);

    await act(async () => {
      result.current.cancel();
    });

    expect(result.current.state.seedingMode).toBe('legacy');
    expect(result.current.isDirty).toBe(false);
  });

  it('includes seeding mode in the save payload', async () => {
    mockSaveConfig.mockResolvedValue({ ok: true });

    const { result, saved } = await loadMutateSave((s) => s.setSeedingMode('deep'));

    expect(saved).toBe(true);
    expect(mockSaveConfig).toHaveBeenCalledWith({
      extraction_model: 'gpt-5.5',
      primary_evidence_model: 'gpt-5.4',
      scoring_model: 'gpt-5.5',
      seeding_model: 'gpt-5.5',
      seeding_mode: 'deep',
    });
    expect(result.current.state.savedSnapshot?.seedingMode).toBe('deep');
  });

  it('loads seeding model from backend config', async () => {
    mockFetchConfig.mockResolvedValue({
      ok: true,
      data: {
        extraction_model: 'gpt-5.5',
        primary_evidence_model: 'gpt-5.4',
        scoring_model: 'gpt-5.5',
        seeding_model: 'grok-4.3',
      },
    });

    const result = await renderLoadedStore();

    expect(result.current.state.seedingModel).toBe('grok-4.3');
    expect(result.current.state.savedSnapshot?.seedingModel).toBe('grok-4.3');
  });

  it('BUG184: PUTs the config body using the server snake_case wire contract, not PascalCase-underscore keys', async () => {
    mockSaveConfig.mockResolvedValue({ ok: true });

    const { saved } = await loadMutateSave((s) => s.setScoringModel('gpt-5.4'));

    expect(saved).toBe(true);
    const payload = mockSaveConfig.mock.calls[0]![0];
    expect(Object.keys(payload).sort()).toEqual(
      ['extraction_model', 'primary_evidence_model', 'scoring_model', 'seeding_mode', 'seeding_model'].sort()
    );
    expect(payload).not.toHaveProperty('Extraction_Model');
    expect(payload).not.toHaveProperty('Primary_Evidence_Model');
    expect(payload).not.toHaveProperty('Scoring_Model');
    expect(payload).not.toHaveProperty('Seeding_Model');
  });
});
