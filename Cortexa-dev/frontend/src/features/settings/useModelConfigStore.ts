import { useEffect, useMemo, useRef, useState } from 'react';
import { fetchModels, getModelsForStage } from './modelsRepository';
import { fetchConfig, saveConfig } from './configRepository';
import type { ModelConfigState, ModelDto, PipelineStage, SeedingMode } from './settingsTypes';

const DEFAULTS = {
  extractionModel: 'gpt-5.5',
  primaryEvidenceModel: 'gpt-5.4',
  scoringModel: 'gpt-5.5',
  seedingModel: 'gpt-5.5',
  seedingMode: 'legacy' as SeedingMode,
};

export const STAGE_ORDER: PipelineStage[] = ['extraction', 'scoring', 'evidence', 'seeding'];

export const STAGE_LABELS: Record<PipelineStage, string> = {
  extraction: 'Extraction',
  scoring: 'Scoring',
  evidence: 'Evidence',
  seeding: 'Seeding',
};

interface ModelSlot {
  stage: PipelineStage;
  label: string;
  id: string;
}

function validateModelIds(extractionModel: string, primaryEvidenceModel: string, scoringModel: string, seedingModel: string, models: ModelDto[]): string | null {
  const slots: ModelSlot[] = [
    { stage: 'extraction', label: STAGE_LABELS.extraction, id: extractionModel },
    { stage: 'scoring', label: STAGE_LABELS.scoring, id: scoringModel },
    { stage: 'evidence', label: STAGE_LABELS.evidence, id: primaryEvidenceModel },
    { stage: 'seeding', label: STAGE_LABELS.seeding, id: seedingModel },
  ];
  const invalid = slots.filter((s) => !getModelsForStage(models, s.stage).some((m) => m.id === s.id));
  if (invalid.length === 0) return null;

  const names = invalid.map((s) => s.label).join(', ');
  const verb = invalid.length > 1 ? 'are' : 'is';
  return `The selected model for ${names} ${verb} not offered for that stage. Choose an option from the list.`;
}

function getZeroOptionStages(models: ModelDto[]): PipelineStage[] {
  return STAGE_ORDER.filter((stage) => getModelsForStage(models, stage).length === 0);
}

export function useModelConfigStore() {
  const [state, setState] = useState<ModelConfigState>({
    extractionModel: DEFAULTS.extractionModel,
    primaryEvidenceModel: DEFAULTS.primaryEvidenceModel,
    scoringModel: DEFAULTS.scoringModel,
    seedingModel: DEFAULTS.seedingModel,
    seedingMode: DEFAULTS.seedingMode,
    savedSnapshot: null,
    models: [],
    loading: true,
    saving: false,
    error: null,
    saveSuccess: false,
    validationError: null,
    saveError: null,
  });

  const initialLoadRef = useRef(false);

  const load = async () => {
    setState((prev) => ({ ...prev, loading: true, error: null }));

    const [modelsResult, configResult] = await Promise.all([fetchModels(), fetchConfig()]);

    if (!modelsResult.ok) {
      setState((prev) => ({
        ...prev,
        loading: false,
        error: modelsResult.error.message,
      }));
      return;
    }

    if (!configResult.ok) {
      setState((prev) => ({
        ...prev,
        loading: false,
        error: configResult.error.message,
      }));
      return;
    }

    const cfg = configResult.data;
    const seedingMode = cfg.seeding_mode ?? 'legacy';
    const snapshot = {
      extractionModel: cfg.extraction_model,
      primaryEvidenceModel: cfg.primary_evidence_model,
      scoringModel: cfg.scoring_model,
      seedingModel: cfg.seeding_model,
      seedingMode,
    };

    setState({
      extractionModel: cfg.extraction_model,
      primaryEvidenceModel: cfg.primary_evidence_model,
      scoringModel: cfg.scoring_model,
      seedingModel: cfg.seeding_model,
      seedingMode,
      savedSnapshot: snapshot,
      models: modelsResult.data.models,
      loading: false,
      saving: false,
      error: null,
      saveSuccess: false,
      validationError: null,
      saveError: null,
    });
  };

  const setExtractionModel = (id: string) => {
    setState((prev) => ({ ...prev, extractionModel: id, saveSuccess: false, validationError: null, saveError: null }));
  };

  const setPrimaryEvidenceModel = (id: string) => {
    setState((prev) => ({ ...prev, primaryEvidenceModel: id, saveSuccess: false, validationError: null, saveError: null }));
  };

  const setScoringModel = (id: string) => {
    setState((prev) => ({ ...prev, scoringModel: id, saveSuccess: false, validationError: null, saveError: null }));
  };

  const setSeedingModel = (id: string) => {
    setState((prev) => ({ ...prev, seedingModel: id, saveSuccess: false, validationError: null, saveError: null }));
  };

  const setSeedingMode = (mode: SeedingMode) => {
    setState((prev) => ({ ...prev, seedingMode: mode, saveSuccess: false, validationError: null, saveError: null }));
  };

  const retry = () => {
    load();
  };

  const cancel = () => {
    if (state.savedSnapshot) {
      setState((prev) => ({
        ...prev,
        extractionModel: prev.savedSnapshot!.extractionModel,
        primaryEvidenceModel: prev.savedSnapshot!.primaryEvidenceModel,
        scoringModel: prev.savedSnapshot!.scoringModel,
        seedingModel: prev.savedSnapshot!.seedingModel,
        seedingMode: prev.savedSnapshot!.seedingMode,
        saveSuccess: false,
        validationError: null,
        saveError: null,
      }));
    }
  };

  const save = async (): Promise<boolean> => {
    if (getZeroOptionStages(state.models).length > 0) {
      return false;
    }

    const validationError = validateModelIds(state.extractionModel, state.primaryEvidenceModel, state.scoringModel, state.seedingModel, state.models);
    if (validationError) {
      setState((prev) => ({ ...prev, validationError, saveError: null }));
      return false;
    }

    setState((prev) => ({ ...prev, saving: true, saveSuccess: false, validationError: null, saveError: null }));

    const result = await saveConfig({
      extraction_model: state.extractionModel,
      primary_evidence_model: state.primaryEvidenceModel,
      scoring_model: state.scoringModel,
      seeding_model: state.seedingModel,
      seeding_mode: state.seedingMode,
    });

    if (!result.ok) {
      setState((prev) => ({
        ...prev,
        saving: false,
        saveError: { message: result.error.message, correlationId: result.error.correlationId },
      }));
      return false;
    }

    const snapshot = {
      extractionModel: state.extractionModel,
      primaryEvidenceModel: state.primaryEvidenceModel,
      scoringModel: state.scoringModel,
      seedingModel: state.seedingModel,
      seedingMode: state.seedingMode,
    };

    setState((prev) => ({
      ...prev,
      saving: false,
      saveSuccess: true,
      savedSnapshot: snapshot,
      saveError: null,
    }));

    return true;
  };

  useEffect(() => {
    if (!initialLoadRef.current) {
      initialLoadRef.current = true;
      load();
    }
  }, []);

  const isDirty = state.savedSnapshot
    ? state.extractionModel !== state.savedSnapshot.extractionModel ||
      state.primaryEvidenceModel !== state.savedSnapshot.primaryEvidenceModel ||
      state.scoringModel !== state.savedSnapshot.scoringModel ||
      state.seedingModel !== state.savedSnapshot.seedingModel ||
      state.seedingMode !== state.savedSnapshot.seedingMode
    : false;

  const extractionOptions = useMemo(() => getModelsForStage(state.models, 'extraction'), [state.models]);
  const scoringOptions = useMemo(() => getModelsForStage(state.models, 'scoring'), [state.models]);
  const evidenceOptions = useMemo(() => getModelsForStage(state.models, 'evidence'), [state.models]);
  const seedingOptions = useMemo(() => getModelsForStage(state.models, 'seeding'), [state.models]);
  const zeroOptionStages = useMemo(() => getZeroOptionStages(state.models), [state.models]);

  return {
    state,
    isDirty,
    extractionOptions,
    scoringOptions,
    evidenceOptions,
    seedingOptions,
    zeroOptionStages,
    setExtractionModel,
    setPrimaryEvidenceModel,
    setScoringModel,
    setSeedingModel,
    setSeedingMode,
    save,
    cancel,
    retry,
  };
}
