export type ModelCapability = 'reasoning' | 'grounding' | 'dual-eligible';

export type PipelineStage = 'extraction' | 'evidence' | 'scoring' | 'seeding';

export type SeedingMode = 'legacy' | 'deep';

export interface ModelDto {
  id: string;
  label: string;
  provider: string;
  role: 'primary' | 'secondary';
  enabled: boolean;
  capabilities: ModelCapability[];
  allowedStages: string[];
}

export interface ModelsResponseDto {
  models: ModelDto[];
  dualModeAvailable: boolean;
  defaults: {
    single: string;
    dual: {
      primary: string;
      secondary: string | null;
    };
  };
}

// Wire contract for GET/PUT /config. The job-orchestrator serializes and binds
// all HTTP JSON under JsonNamingPolicy.SnakeCaseLower, so these keys MUST stay
// lowercase snake_case to match the server's ConfigResponse/ConfigRequest.
export interface ModelConfigDto {
  extraction_model: string;
  primary_evidence_model: string;
  scoring_model: string;
  seeding_model: string;
  seeding_mode?: SeedingMode;
}

export interface ModelConfigRequest {
  extraction_model: string;
  primary_evidence_model: string;
  scoring_model: string;
  seeding_model: string;
  seeding_mode: SeedingMode;
}

export interface SaveErrorInfo {
  message: string;
  correlationId?: string;
}

export interface ModelConfigState {
  extractionModel: string;
  primaryEvidenceModel: string;
  scoringModel: string;
  seedingModel: string;
  seedingMode: SeedingMode;
  savedSnapshot: ModelConfigSnapshot | null;
  models: ModelDto[];
  loading: boolean;
  saving: boolean;
  error: string | null;
  saveSuccess: boolean;
  validationError: string | null;
  saveError: SaveErrorInfo | null;
}

export interface ModelConfigSnapshot {
  extractionModel: string;
  primaryEvidenceModel: string;
  scoringModel: string;
  seedingModel: string;
  seedingMode: SeedingMode;
}
