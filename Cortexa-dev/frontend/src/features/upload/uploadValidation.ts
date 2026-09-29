import { maxBatchFiles, maxBatchSizeBytes, maxFileSizeBytes } from '../../core/config/env';
import type { BatchFieldErrors } from './uploadTypes';

const ACCEPTED_EXTENSIONS = ['.pdf', '.docx'];
const ACCEPTED_MIME_TYPES = [
  'application/pdf',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
];

const BATCH_NAME_PATTERN = /^[A-Za-z0-9 _.-]+$/;
const GIT_HOST_PATTERN = /^https:\/\/(github\.com|dev\.azure\.com)\//i;

export function formatBytes(bytes: number): string {
  if (bytes >= 1024 * 1024) {
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
  if (bytes >= 1024) {
    return `${(bytes / 1024).toFixed(0)} KB`;
  }
  return `${bytes} B`;
}

function hasAcceptedExtension(fileName: string): boolean {
  const lower = fileName.toLowerCase();
  return ACCEPTED_EXTENSIONS.some((ext) => lower.endsWith(ext));
}

function hasAcceptedMimeType(mimeType: string): boolean {
  return mimeType === '' || ACCEPTED_MIME_TYPES.includes(mimeType);
}

export function validateFileType(file: File): string | undefined {
  if (!hasAcceptedExtension(file.name) || !hasAcceptedMimeType(file.type)) {
    return 'Unsupported type — only PDF and DOCX are allowed';
  }
  return undefined;
}

export function validateFileSize(file: File): string | undefined {
  if (file.size > maxFileSizeBytes) {
    return `Too large — ${formatBytes(file.size)} exceeds the ${formatBytes(maxFileSizeBytes)} per-file cap`;
  }
  return undefined;
}

export function validateFile(file: File): string | undefined {
  return validateFileType(file) ?? validateFileSize(file);
}

export interface BatchCapResult {
  withinCount: boolean;
  withinSize: boolean;
  totalSize: number;
}

export function checkBatchCaps(files: File[]): BatchCapResult {
  const totalSize = files.reduce((sum, file) => sum + file.size, 0);
  return {
    withinCount: files.length <= maxBatchFiles,
    withinSize: totalSize <= maxBatchSizeBytes,
    totalSize,
  };
}

export function validateBatchName(name: string): string | undefined {
  const trimmed = name.trim();
  if (trimmed.length === 0) {
    return undefined;
  }
  if (trimmed.length < 3) {
    return 'Batch name must be at least 3 characters.';
  }
  if (trimmed.length > 80) {
    return 'Batch name must be 80 characters or fewer.';
  }
  if (!BATCH_NAME_PATTERN.test(trimmed)) {
    return 'Use only letters, numbers, spaces, and - _ .';
  }
  return undefined;
}

export function validateRepoUrl(url: string): string | undefined {
  const trimmed = url.trim();
  if (trimmed.length === 0) {
    return undefined;
  }
  if (!GIT_HOST_PATTERN.test(trimmed)) {
    return 'Enter a valid https:// GitHub or Azure DevOps URL.';
  }
  return undefined;
}

export function validateBatchFields(batchName: string): BatchFieldErrors {
  const errors: BatchFieldErrors = {};
  const batchNameError = validateBatchName(batchName);
  if (batchNameError) {
    errors.batchName = batchNameError;
  }
  return errors;
}

export function validateGitParams(params: { repoUrl: string; provider: string }): BatchFieldErrors {
  const errors: BatchFieldErrors = {};
  if (!params.repoUrl.trim()) {
    errors.repoUrl = 'Repository URL is required.';
  } else {
    const urlError = validateRepoUrl(params.repoUrl);
    if (urlError) errors.repoUrl = urlError;
  }
  if (!params.provider) errors.gitProvider = 'Provider is required.';
  return errors;
}
