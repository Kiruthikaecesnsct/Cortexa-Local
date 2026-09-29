import { describe, it, expect } from 'vitest';
import {
  validateFileType,
  validateFileSize,
  validateFile,
  checkBatchCaps,
  validateBatchName,
  validateRepoUrl,
  validateBatchFields,
  formatBytes,
} from '../uploadValidation';

function makeFile(name: string, size: number, type: string): File {
  const blob = new Blob([new Uint8Array(Math.min(size, 1024))], { type });
  const file = new File([blob], name, { type });
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

describe('validateFileType', () => {
  it('accepts a pdf file', () => {
    const file = makeFile('paper.pdf', 1000, 'application/pdf');
    expect(validateFileType(file)).toBeUndefined();
  });

  it('accepts a docx file', () => {
    const file = makeFile(
      'notes.docx',
      1000,
      'application/vnd.openxmlformats-officedocument.wordprocessingml.document'
    );
    expect(validateFileType(file)).toBeUndefined();
  });

  it('rejects an unsupported type', () => {
    const file = makeFile('dataset.csv', 1000, 'text/csv');
    expect(validateFileType(file)).toMatch(/unsupported type/i);
  });
});

describe('validateFileSize', () => {
  it('accepts a file under the per-file cap', () => {
    const file = makeFile('small.pdf', 1024, 'application/pdf');
    expect(validateFileSize(file)).toBeUndefined();
  });

  it('rejects a file over the per-file cap', () => {
    const file = makeFile('huge.pdf', 64 * 1024 * 1024, 'application/pdf');
    expect(validateFileSize(file)).toMatch(/too large/i);
  });
});

describe('validateFile', () => {
  it('reports type error before size error', () => {
    const file = makeFile('huge.csv', 64 * 1024 * 1024, 'text/csv');
    expect(validateFile(file)).toMatch(/unsupported type/i);
  });
});

describe('checkBatchCaps', () => {
  it('reports within caps for a small batch', () => {
    const files = [makeFile('a.pdf', 1024, 'application/pdf'), makeFile('b.pdf', 2048, 'application/pdf')];
    const result = checkBatchCaps(files);
    expect(result.withinCount).toBe(true);
    expect(result.withinSize).toBe(true);
    expect(result.totalSize).toBe(3072);
  });

  it('reports over count cap when more than 25 files', () => {
    const files = Array.from({ length: 26 }, (_, i) => makeFile(`f${i}.pdf`, 100, 'application/pdf'));
    const result = checkBatchCaps(files);
    expect(result.withinCount).toBe(false);
  });

  it('reports over size cap when total exceeds 200 MB', () => {
    const files = [makeFile('big.pdf', 201 * 1024 * 1024, 'application/pdf')];
    const result = checkBatchCaps(files);
    expect(result.withinSize).toBe(false);
  });
});

describe('validateBatchName', () => {
  it('accepts empty name as the field is optional', () => {
    expect(validateBatchName('')).toBeUndefined();
  });

  it('rejects names shorter than 3 characters', () => {
    expect(validateBatchName('ab')).toMatch(/at least 3/i);
  });

  it('rejects names longer than 80 characters', () => {
    expect(validateBatchName('a'.repeat(81))).toMatch(/80 characters/i);
  });

  it('rejects names with disallowed characters', () => {
    expect(validateBatchName('batch@name!')).toMatch(/letters, numbers/i);
  });

  it('accepts a valid name', () => {
    expect(validateBatchName('Q3 thesis archive - robotics')).toBeUndefined();
  });
});

describe('validateRepoUrl', () => {
  it('accepts an empty value (optional field)', () => {
    expect(validateRepoUrl('')).toBeUndefined();
  });

  it('accepts a valid github https url', () => {
    expect(validateRepoUrl('https://github.com/org/repo')).toBeUndefined();
  });

  it('accepts a valid azure devops https url', () => {
    expect(validateRepoUrl('https://dev.azure.com/org/project/_git/repo')).toBeUndefined();
  });

  it('rejects a non-https url', () => {
    expect(validateRepoUrl('github.com/org/repo')).toMatch(/valid https/i);
  });

  it('rejects an unsupported host', () => {
    expect(validateRepoUrl('https://gitlab.com/org/repo')).toMatch(/valid https/i);
  });
});

describe('validateBatchFields', () => {
  it('returns batchName error when name is too short', () => {
    const errors = validateBatchFields('ab');
    expect(errors.batchName).toBeTruthy();
  });

  it('returns no errors for a valid batch name', () => {
    const errors = validateBatchFields('Valid batch name');
    expect(errors).toEqual({});
  });
});

describe('formatBytes', () => {
  it('formats megabytes', () => {
    expect(formatBytes(5 * 1024 * 1024)).toBe('5.0 MB');
  });

  it('formats kilobytes', () => {
    expect(formatBytes(2048)).toBe('2 KB');
  });

  it('formats bytes', () => {
    expect(formatBytes(500)).toBe('500 B');
  });
});
