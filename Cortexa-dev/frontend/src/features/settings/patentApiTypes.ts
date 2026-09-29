export type PatentSourceId = 'uspto' | 'epo' | 'lens';

export type CredentialStatusValue = 'set' | 'not_set';

export interface EpoCredentialStatusDto {
  consumer: CredentialStatusValue;
  oauth: CredentialStatusValue;
}

// Wire shape mirrors PatentConfigEndpoints.cs (PatentConfigResponse), serialized
// with JsonNamingPolicy.SnakeCaseLower. The raw credential value is never
// returned — only a set/not_set status per source.
export interface PatentConfigDto {
  uspto_enabled: boolean;
  epo_enabled: boolean;
  lens_enabled: boolean;
  uspto_credential_status: CredentialStatusValue;
  epo_credential_status: EpoCredentialStatusDto;
  lens_credential_status: CredentialStatusValue;
  config_version: number;
  updated_at: string;
  updated_by: string;
}

export interface PatentConfigRequest {
  uspto_enabled: boolean;
  epo_enabled: boolean;
  lens_enabled: boolean;
}

export interface PatentSecretRequest {
  api_key?: string;
  consumer_key?: string;
  oauth_secret?: string;
}

export interface PatentProbeResponseDto {
  success: boolean;
  failure_reason?: string;
}

export interface PatentApiError {
  message: string;
  correlationId?: string;
}

export type TestResultStatus = 'idle' | 'testing' | 'success' | 'failure';

export interface SourceTestResult {
  status: TestResultStatus;
  message?: string;
  correlationId?: string;
  testedAt?: string;
}
