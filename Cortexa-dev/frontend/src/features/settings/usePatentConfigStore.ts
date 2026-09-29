import { useEffect, useRef, useState } from 'react';
import { fetchPatentConfig, savePatentConfig, testPatentConnection, writePatentSecret } from './patentConfigRepository';
import type { WriteSecretResult } from './patentConfigRepository';
import type {
  CredentialStatusValue,
  EpoCredentialStatusDto,
  PatentConfigDto,
  PatentSecretRequest,
  PatentSourceId,
  SourceTestResult,
} from './patentApiTypes';
import type { SaveErrorInfo } from './settingsTypes';

interface EnableFlags {
  usptoEnabled: boolean;
  epoEnabled: boolean;
  lensEnabled: boolean;
}

interface PatentConfigState extends EnableFlags {
  savedSnapshot: EnableFlags | null;
  usptoCredentialStatus: CredentialStatusValue;
  epoCredentialStatus: EpoCredentialStatusDto;
  lensCredentialStatus: CredentialStatusValue;
  configVersion: number;
  updatedAt: string;
  updatedBy: string;
  loading: boolean;
  saving: boolean;
  error: string | null;
  saveSuccess: boolean;
  saveError: SaveErrorInfo | null;
  testResults: Record<PatentSourceId, SourceTestResult>;
}

const ENABLE_KEY: Record<PatentSourceId, keyof EnableFlags> = {
  uspto: 'usptoEnabled',
  epo: 'epoEnabled',
  lens: 'lensEnabled',
};

const IDLE_TEST_RESULTS: Record<PatentSourceId, SourceTestResult> = {
  uspto: { status: 'idle' },
  epo: { status: 'idle' },
  lens: { status: 'idle' },
};

const INITIAL_STATE: PatentConfigState = {
  usptoEnabled: false,
  epoEnabled: false,
  lensEnabled: false,
  savedSnapshot: null,
  usptoCredentialStatus: 'not_set',
  epoCredentialStatus: { consumer: 'not_set', oauth: 'not_set' },
  lensCredentialStatus: 'not_set',
  configVersion: 0,
  updatedAt: '',
  updatedBy: '',
  loading: true,
  saving: false,
  error: null,
  saveSuccess: false,
  saveError: null,
  testResults: IDLE_TEST_RESULTS,
};

function extractFlags(dto: PatentConfigDto): EnableFlags {
  return { usptoEnabled: dto.uspto_enabled, epoEnabled: dto.epo_enabled, lensEnabled: dto.lens_enabled };
}

function extractCredentials(dto: PatentConfigDto) {
  return {
    usptoCredentialStatus: dto.uspto_credential_status,
    epoCredentialStatus: dto.epo_credential_status,
    lensCredentialStatus: dto.lens_credential_status,
    configVersion: dto.config_version,
    updatedAt: dto.updated_at,
    updatedBy: dto.updated_by,
  };
}

function toTestResult(
  result: Awaited<ReturnType<typeof testPatentConnection>>
): SourceTestResult {
  if (!result.ok) {
    return { status: 'failure', message: result.error.message, correlationId: result.error.correlationId };
  }
  return {
    status: result.data.success ? 'success' : 'failure',
    message: result.data.failure_reason,
    testedAt: new Date().toISOString(),
  };
}

export function usePatentConfigStore() {
  const [state, setState] = useState<PatentConfigState>(INITIAL_STATE);
  const initialLoadRef = useRef(false);

  const load = async () => {
    setState((prev) => ({ ...prev, loading: true, error: null }));

    const result = await fetchPatentConfig();
    if (!result.ok) {
      setState((prev) => ({ ...prev, loading: false, error: result.error.message }));
      return;
    }

    const flags = extractFlags(result.data);
    setState({
      ...flags,
      savedSnapshot: flags,
      ...extractCredentials(result.data),
      loading: false,
      saving: false,
      error: null,
      saveSuccess: false,
      saveError: null,
      testResults: IDLE_TEST_RESULTS,
    });
  };

  const retry = () => {
    load();
  };

  const setEnabled = (source: PatentSourceId, enabled: boolean) => {
    setState((prev) => ({ ...prev, [ENABLE_KEY[source]]: enabled, saveSuccess: false, saveError: null }));
  };

  const cancel = () => {
    if (!state.savedSnapshot) return;
    const snapshot = state.savedSnapshot;
    setState((prev) => ({ ...prev, ...snapshot, saveSuccess: false, saveError: null }));
  };

  const save = async (): Promise<boolean> => {
    setState((prev) => ({ ...prev, saving: true, saveSuccess: false, saveError: null }));

    const result = await savePatentConfig({
      uspto_enabled: state.usptoEnabled,
      epo_enabled: state.epoEnabled,
      lens_enabled: state.lensEnabled,
    });

    if (!result.ok) {
      setState((prev) => ({
        ...prev,
        saving: false,
        saveError: { message: result.error.message, correlationId: result.error.correlationId },
      }));
      return false;
    }

    const flags = extractFlags(result.data);
    setState((prev) => ({
      ...prev,
      ...flags,
      ...extractCredentials(result.data),
      savedSnapshot: flags,
      saving: false,
      saveSuccess: true,
      saveError: null,
    }));
    return true;
  };

  const submitSecret = async (source: PatentSourceId, payload: PatentSecretRequest): Promise<WriteSecretResult> => {
    const result = await writePatentSecret(source, payload);
    if (result.ok) {
      setState((prev) => ({ ...prev, ...extractCredentials(result.data) }));
    }
    return result;
  };

  const testConnection = async (source: PatentSourceId, payload?: PatentSecretRequest): Promise<void> => {
    setState((prev) => ({ ...prev, testResults: { ...prev.testResults, [source]: { status: 'testing' } } }));
    const result = await testPatentConnection(source, payload);
    const next = toTestResult(result);
    setState((prev) => ({ ...prev, testResults: { ...prev.testResults, [source]: next } }));
  };

  useEffect(() => {
    if (!initialLoadRef.current) {
      initialLoadRef.current = true;
      load();
    }
  }, []);

  const isDirty = state.savedSnapshot
    ? state.usptoEnabled !== state.savedSnapshot.usptoEnabled ||
      state.epoEnabled !== state.savedSnapshot.epoEnabled ||
      state.lensEnabled !== state.savedSnapshot.lensEnabled
    : false;

  return {
    state,
    isDirty,
    setEnabled,
    save,
    cancel,
    retry,
    submitSecret,
    testConnection,
  };
}
