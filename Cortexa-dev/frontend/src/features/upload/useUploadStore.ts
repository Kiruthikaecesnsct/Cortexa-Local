import { useCallback, useReducer, type Dispatch } from 'react';
import { maxSavedRepositoriesPerBatch } from '../../core/config/env';
import {
  mergeSavedRepositories,
  savedRepositoryKey,
  withSavedRepositoryFiles,
  type SavedRepositorySelection,
} from './savedRepositories';
import { createBatch, resumeBatch } from './uploadRepository';
import type {
  AnalysisEngine,
  BatchFieldErrors,
  CreateBatchData,
  CreateBatchRequest,
  GitParams,
  SelectedFile,
  SeedCorpusDomain,
  UploadError,
} from './uploadTypes';
import { checkBatchCaps, validateBatchFields, validateFile } from './uploadValidation';

export type UploadStatus = 'idle' | 'uploading' | 'success' | 'error';

export interface UploadState {
  files: SelectedFile[];
  isDragActive: boolean;
  batchName: string;
  gitParams: GitParams | null;
  gitModalOpen: boolean;
  savedRepositories: SavedRepositorySelection[];
  engine: AnalysisEngine;
  seedCorpusDomain: SeedCorpusDomain;
  fieldErrors: BatchFieldErrors;
  zoneError?: string;
  status: UploadStatus;
  successData: CreateBatchData | null;
  submitError: UploadError | null;
  pendingBatchId?: string;
}

type UploadAction =
  | { type: 'SET_BATCH_NAME'; value: string }
  | { type: 'SET_ENGINE'; value: AnalysisEngine }
  | { type: 'SET_SEED_CORPUS_DOMAIN'; value: SeedCorpusDomain }
  | { type: 'OPEN_GIT_MODAL' }
  | { type: 'CLOSE_GIT_MODAL' }
  | { type: 'SET_GIT_PARAMS'; payload: GitParams | null }
  | { type: 'ADD_SAVED_REPOSITORIES'; repositories: SavedRepositorySelection[] }
  | { type: 'REMOVE_SAVED_REPOSITORY'; key: string }
  | { type: 'SET_SAVED_REPOSITORY_FILES'; key: string; files: string[] | undefined }
  | { type: 'SET_DRAG_ACTIVE'; value: boolean }
  | { type: 'ADD_FILES'; files: SelectedFile[]; zoneError?: string }
  | { type: 'REMOVE_FILE'; id: string }
  | { type: 'CLEAR_SUBMIT_ERROR' }
  | { type: 'SUBMIT_START' }
  | { type: 'SUBMIT_SUCCESS'; data: CreateBatchData }
  | { type: 'SUBMIT_ERROR'; error: UploadError }
  | { type: 'SET_FIELD_ERRORS'; errors: BatchFieldErrors };

const initialState: UploadState = {
  files: [],
  isDragActive: false,
  batchName: '',
  gitParams: null,
  gitModalOpen: false,
  savedRepositories: [],
  engine: 'harvesting',
  seedCorpusDomain: 'ml_ai',
  fieldErrors: {},
  status: 'idle',
  successData: null,
  submitError: null,
};

function filesReducer(state: UploadState, action: UploadAction): UploadState {
  switch (action.type) {
    case 'SET_DRAG_ACTIVE':
      return { ...state, isDragActive: action.value };
    case 'ADD_FILES':
      return { ...state, files: [...state.files, ...action.files], zoneError: action.zoneError };
    case 'REMOVE_FILE':
      return { ...state, files: state.files.filter((f) => f.id !== action.id), zoneError: undefined };
    default:
      return state;
  }
}

function clearFieldError(errors: BatchFieldErrors, key: keyof BatchFieldErrors): BatchFieldErrors {
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  const { [key]: _removed, ...rest } = errors;
  return rest;
}

function configReducer(state: UploadState, action: UploadAction): UploadState {
  switch (action.type) {
    case 'SET_BATCH_NAME':
      return { ...state, batchName: action.value, fieldErrors: clearFieldError(state.fieldErrors, 'batchName') };
    case 'SET_ENGINE':
      return { ...state, engine: action.value };
    case 'SET_SEED_CORPUS_DOMAIN':
      return { ...state, seedCorpusDomain: action.value };
    default:
      return state;
  }
}

function gitModalReducer(state: UploadState, action: UploadAction): UploadState {
  switch (action.type) {
    case 'OPEN_GIT_MODAL':
      return { ...state, gitModalOpen: true };
    case 'CLOSE_GIT_MODAL':
      return { ...state, gitModalOpen: false };
    case 'SET_GIT_PARAMS':
      return { ...state, gitParams: action.payload };
    default:
      return state;
  }
}

function savedRepositoriesReducer(state: UploadState, action: UploadAction): UploadState {
  switch (action.type) {
    case 'ADD_SAVED_REPOSITORIES': {
      const merged = mergeSavedRepositories(state.savedRepositories, action.repositories);
      return merged === state.savedRepositories ? state : { ...state, savedRepositories: merged };
    }
    case 'REMOVE_SAVED_REPOSITORY':
      return { ...state, savedRepositories: state.savedRepositories.filter((r) => savedRepositoryKey(r) !== action.key) };
    case 'SET_SAVED_REPOSITORY_FILES':
      return { ...state, savedRepositories: withSavedRepositoryFiles(state.savedRepositories, action.key, action.files) };
    default:
      return state;
  }
}

function statusReducer(state: UploadState, action: UploadAction): UploadState {
  switch (action.type) {
    case 'CLEAR_SUBMIT_ERROR':
      return { ...state, submitError: null };
    case 'SUBMIT_START':
      return { ...state, status: 'uploading', submitError: null };
    case 'SUBMIT_SUCCESS':
      return { ...state, status: 'success', successData: action.data, submitError: null, pendingBatchId: undefined };
    case 'SUBMIT_ERROR':
      return { ...state, status: 'error', submitError: action.error, pendingBatchId: action.error.pendingBatchId };
    case 'SET_FIELD_ERRORS':
      return { ...state, fieldErrors: action.errors };
    default:
      return state;
  }
}

function uploadReducer(state: UploadState, action: UploadAction): UploadState {
  const afterFiles = filesReducer(state, action);
  if (afterFiles !== state) return afterFiles;
  const afterConfig = configReducer(state, action);
  if (afterConfig !== state) return afterConfig;
  const afterGitModal = gitModalReducer(state, action);
  if (afterGitModal !== state) return afterGitModal;
  const afterSaved = savedRepositoriesReducer(state, action);
  if (afterSaved !== state) return afterSaved;
  return statusReducer(state, action);
}

function makeSelectedFiles(incoming: File[]): SelectedFile[] {
  return incoming.map((file) => {
    const rejectionReason = validateFile(file);
    return {
      id: `${file.name}-${file.size}-${file.lastModified}-${Math.random().toString(36).slice(2)}`,
      file,
      valid: !rejectionReason,
      rejectionReason,
    };
  });
}

function buildZoneError(files: SelectedFile[]): string | undefined {
  const rejectedCount = files.filter((f) => !f.valid).length;
  if (rejectedCount === 0) {
    return undefined;
  }
  return `${rejectedCount} file${rejectedCount === 1 ? '' : 's'} were rejected. Remove them or replace with PDF/DOCX under the size cap to continue.`;
}

function generateBatchName(): string {
  const now = new Date();
  const date = now.toISOString().slice(0, 10);
  const ts = now.getTime().toString().slice(-6);
  return `Batch ${date} ${ts}`;
}

async function handleResumeFlow(
  pendingBatchId: string,
  dispatch: Dispatch<UploadAction>
): Promise<void> {
  dispatch({ type: 'SUBMIT_START' });
  const result = await resumeBatch(pendingBatchId);
  if (result.ok) {
    dispatch({ type: 'SUBMIT_SUCCESS', data: result.data });
  } else {
    dispatch({ type: 'SUBMIT_ERROR', error: result.error });
  }
}

function isSubmitBlocked(
  fieldErrors: BatchFieldErrors,
  filesOk: boolean,
  hasAnyInput: boolean
): boolean {
  return Object.keys(fieldErrors).length > 0 || !filesOk || !hasAnyInput;
}

async function handleFreshFlow(request: CreateBatchRequest, dispatch: Dispatch<UploadAction>): Promise<void> {
  dispatch({ type: 'SUBMIT_START' });
  const result = await createBatch({ ...request, batchName: request.batchName.trim() || generateBatchName() });
  if (result.ok) {
    dispatch({ type: 'SUBMIT_SUCCESS', data: result.data });
  } else {
    dispatch({ type: 'SUBMIT_ERROR', error: result.error });
  }
}

export function useUploadStore() {
  const [state, dispatch] = useReducer(uploadReducer, initialState);

  const setBatchName = useCallback((value: string) => dispatch({ type: 'SET_BATCH_NAME', value }), []);
  const setEngine = useCallback((value: AnalysisEngine) => dispatch({ type: 'SET_ENGINE', value }), []);
  const setSeedCorpusDomain = useCallback((value: SeedCorpusDomain) => dispatch({ type: 'SET_SEED_CORPUS_DOMAIN', value }), []);
  const openGitModal = useCallback(() => dispatch({ type: 'OPEN_GIT_MODAL' }), []);
  const closeGitModal = useCallback(() => dispatch({ type: 'CLOSE_GIT_MODAL' }), []);
  const setGitParams = useCallback((payload: GitParams | null) => dispatch({ type: 'SET_GIT_PARAMS', payload }), []);
  const setDragActive = useCallback((value: boolean) => dispatch({ type: 'SET_DRAG_ACTIVE', value }), []);
  const clearSubmitError = useCallback(() => dispatch({ type: 'CLEAR_SUBMIT_ERROR' }), []);

  const addFiles = useCallback((incoming: File[]) => {
    const selected = makeSelectedFiles(incoming);
    dispatch({ type: 'ADD_FILES', files: selected, zoneError: buildZoneError(selected) });
  }, []);

  const removeFile = useCallback((id: string) => dispatch({ type: 'REMOVE_FILE', id }), []);

  const addSavedRepositories = useCallback(
    (repositories: SavedRepositorySelection[]) => dispatch({ type: 'ADD_SAVED_REPOSITORIES', repositories }),
    []
  );
  const removeSavedRepository = useCallback((key: string) => dispatch({ type: 'REMOVE_SAVED_REPOSITORY', key }), []);
  const setSavedRepositoryFiles = useCallback(
    (key: string, files: string[] | undefined) => dispatch({ type: 'SET_SAVED_REPOSITORY_FILES', key, files }),
    []
  );

  const validFiles = state.files.filter((f) => f.valid).map((f) => f.file);
  const filesOk = state.files.every((f) => f.valid);
  const hasValidFiles = validFiles.length > 0;
  const hasValidRepo = state.gitParams != null;
  const hasAnyInput = hasValidFiles || hasValidRepo || state.savedRepositories.length > 0;
  const caps = checkBatchCaps(validFiles);
  const withinSavedCap = state.savedRepositories.length <= maxSavedRepositoriesPerBatch;
  const savedRepositoriesHaveFiles = state.savedRepositories.every((r) => r.selectedFiles === undefined || r.selectedFiles.length > 0);
  const noFieldErrors = Object.keys(state.fieldErrors).length === 0;
  const canSubmit =
    hasAnyInput &&
    filesOk &&
    caps.withinCount &&
    caps.withinSize &&
    withinSavedCap &&
    savedRepositoriesHaveFiles &&
    noFieldErrors &&
    state.status !== 'uploading';

  const submit = useCallback(async () => {
    if (state.pendingBatchId) {
      await handleResumeFlow(state.pendingBatchId, dispatch);
      return;
    }
    const fieldErrors = validateBatchFields(state.batchName);
    if (isSubmitBlocked(fieldErrors, filesOk, hasAnyInput)) {
      dispatch({ type: 'SET_FIELD_ERRORS', errors: fieldErrors });
      return;
    }
    await handleFreshFlow(
      {
        files: validFiles,
        batchName: state.batchName,
        gitParams: state.gitParams ?? undefined,
        savedRepositories: state.savedRepositories,
        engine: state.engine,
        seedCorpusDomain: state.seedCorpusDomain,
      },
      dispatch
    );
  }, [
    state.batchName,
    state.gitParams,
    state.savedRepositories,
    state.pendingBatchId,
    state.engine,
    state.seedCorpusDomain,
    filesOk,
    hasAnyInput,
    validFiles,
  ]);

  return {
    ...state,
    canSubmit,
    totalSizeBytes: caps.totalSize,
    setBatchName,
    setEngine,
    setSeedCorpusDomain,
    openGitModal,
    closeGitModal,
    setGitParams,
    setDragActive,
    addFiles,
    removeFile,
    addSavedRepositories,
    removeSavedRepository,
    setSavedRepositoryFiles,
    withinSavedCap,
    savedRepositoriesHaveFiles,
    clearSubmitError,
    submit,
  };
}
