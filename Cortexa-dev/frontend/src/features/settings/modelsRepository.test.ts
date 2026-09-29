import { describe, it, expect, vi, beforeEach } from 'vitest';

const { getRaw } = vi.hoisted(() => ({ getRaw: vi.fn() }));

vi.mock('../../core/api/client', () => ({
  ApiClient: vi.fn().mockImplementation(() => ({ getRaw })),
}));
vi.mock('../../core/auth/tokenStore', () => ({ getAccessToken: () => undefined }));
vi.mock('../../core/config/env', () => ({ apiBaseUrl: '/api' }));

import { fetchModels, getModelsForStage } from './modelsRepository';
import type { ModelDto } from './settingsTypes';

const bareBody = {
  models: [
    {
      id: 'gpt-5.5',
      label: 'GPT 5.5',
      provider: 'azure-foundry',
      role: 'primary',
      enabled: true,
      capabilities: ['reasoning'],
      allowedStages: ['extraction', 'scoring'],
    },
  ],
  dualModeAvailable: true,
  defaults: { single: 'gpt-5.5', dual: { primary: 'gpt-5.5', secondary: 'grok-4.3' } },
};

describe('fetchModels', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('accepts the bare model-router body (no ApiResponse envelope)', async () => {
    getRaw.mockResolvedValue(bareBody);

    const result = await fetchModels();

    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.data.models).toHaveLength(1);
      expect(result.data.models[0]?.id).toBe('gpt-5.5');
      expect(result.data.models[0]?.allowedStages).toEqual(['extraction', 'scoring']);
    }
  });

  it('returns an error when the body is not a models response', async () => {
    getRaw.mockResolvedValue({ unexpected: true });

    const result = await fetchModels();

    expect(result.ok).toBe(false);
  });

  it('returns an error when the request throws', async () => {
    getRaw.mockRejectedValue(new Error('network down'));

    const result = await fetchModels();

    expect(result.ok).toBe(false);
  });
});

describe('getModelsForStage', () => {
  const models: ModelDto[] = [
    { id: 'gpt-5.5', label: 'GPT 5.5', provider: 'azure-foundry', role: 'primary', enabled: true, capabilities: ['reasoning'], allowedStages: ['extraction', 'scoring'] },
    { id: 'gpt-5.4', label: 'GPT 5.4', provider: 'azure-foundry', role: 'primary', enabled: true, capabilities: ['reasoning'], allowedStages: ['evidence', 'scoring'] },
    { id: 'grok-4.3', label: 'Grok 4.3', provider: 'xai', role: 'secondary', enabled: true, capabilities: ['grounding'], allowedStages: ['extraction', 'evidence', 'scoring'] },
    { id: 'claude-opus-4-8', label: 'Claude Opus 4.8', provider: 'anthropic', role: 'primary', enabled: false, capabilities: ['reasoning'], allowedStages: ['extraction', 'evidence', 'scoring'] },
  ];

  it('excludes gpt-5.5 from the evidence stage', () => {
    const evidenceModels = getModelsForStage(models, 'evidence');
    expect(evidenceModels.map((m) => m.id)).not.toContain('gpt-5.5');
    expect(evidenceModels.map((m) => m.id)).toEqual(['gpt-5.4', 'grok-4.3']);
  });

  it('excludes gpt-5.4 from the extraction stage', () => {
    const extractionModels = getModelsForStage(models, 'extraction');
    expect(extractionModels.map((m) => m.id)).not.toContain('gpt-5.4');
    expect(extractionModels.map((m) => m.id)).toEqual(['gpt-5.5', 'grok-4.3']);
  });

  it('excludes disabled models even when the stage is allowed', () => {
    const extractionModels = getModelsForStage(models, 'extraction');
    expect(extractionModels.map((m) => m.id)).not.toContain('claude-opus-4-8');
  });

  it('returns an empty list when no enabled model allows the stage', () => {
    const noneModels: ModelDto[] = models.map((m) => ({ ...m, enabled: false }));
    expect(getModelsForStage(noneModels, 'scoring')).toHaveLength(0);
  });

  it('filters models for the seeding stage', () => {
    const seedingModels: ModelDto[] = [
      { id: 'gpt-5.5', label: 'GPT 5.5', provider: 'azure-foundry', role: 'primary', enabled: true, capabilities: ['reasoning'], allowedStages: ['extraction', 'scoring', 'seeding'] },
      { id: 'gpt-5.4', label: 'GPT 5.4', provider: 'azure-foundry', role: 'primary', enabled: true, capabilities: ['reasoning'], allowedStages: ['evidence', 'scoring'] },
      { id: 'grok-4.3', label: 'Grok 4.3', provider: 'xai', role: 'secondary', enabled: true, capabilities: ['grounding'], allowedStages: ['extraction', 'evidence', 'scoring', 'seeding'] },
    ];
    const result = getModelsForStage(seedingModels, 'seeding');
    expect(result.map((m) => m.id)).toEqual(['gpt-5.5', 'grok-4.3']);
  });
});
