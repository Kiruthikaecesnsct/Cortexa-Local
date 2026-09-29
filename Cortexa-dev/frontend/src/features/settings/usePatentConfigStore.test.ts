import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { usePatentConfigStore } from './usePatentConfigStore';
import * as patentConfigRepository from './patentConfigRepository';
import type { PatentConfigDto } from './patentApiTypes';

vi.mock('./patentConfigRepository');

const mockFetchPatentConfig = vi.mocked(patentConfigRepository.fetchPatentConfig);
const mockSavePatentConfig = vi.mocked(patentConfigRepository.savePatentConfig);
const mockWritePatentSecret = vi.mocked(patentConfigRepository.writePatentSecret);
const mockTestPatentConnection = vi.mocked(patentConfigRepository.testPatentConnection);

type StoreHook = ReturnType<typeof renderHook<ReturnType<typeof usePatentConfigStore>, unknown>>['result'];

async function renderLoadedStore(): Promise<StoreHook> {
  const { result } = renderHook(() => usePatentConfigStore());
  await waitFor(() => expect(result.current.state.loading).toBe(false));
  return result;
}

const configFixture: PatentConfigDto = {
  uspto_enabled: true,
  epo_enabled: true,
  lens_enabled: false,
  uspto_credential_status: 'set',
  epo_credential_status: { consumer: 'set', oauth: 'not_set' },
  lens_credential_status: 'not_set',
  config_version: 7,
  updated_at: '2026-07-13T10:00:00Z',
  updated_by: 'alpha@cortexa.dev',
};

describe('usePatentConfigStore', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchPatentConfig.mockResolvedValue({ ok: true, data: configFixture });
  });

  it('maps the snake_case wire response onto camelCase store state', async () => {
    const result = await renderLoadedStore();

    expect(result.current.state.usptoEnabled).toBe(true);
    expect(result.current.state.epoEnabled).toBe(true);
    expect(result.current.state.lensEnabled).toBe(false);
    expect(result.current.state.usptoCredentialStatus).toBe('set');
    expect(result.current.state.epoCredentialStatus).toEqual({ consumer: 'set', oauth: 'not_set' });
    expect(result.current.state.lensCredentialStatus).toBe('not_set');
    expect(result.current.state.configVersion).toBe(7);
  });

  it('surfaces a load failure without leaving the store stuck loading', async () => {
    mockFetchPatentConfig.mockResolvedValue({ ok: false, error: { message: 'Unable to connect to configuration service.' } });

    const result = await renderLoadedStore();

    expect(result.current.state.error).toBe('Unable to connect to configuration service.');
  });

  it('is not dirty until an enable flag changes, and dirty after', async () => {
    const result = await renderLoadedStore();
    expect(result.current.isDirty).toBe(false);

    await act(async () => {
      result.current.setEnabled('lens', true);
    });

    expect(result.current.isDirty).toBe(true);
    expect(result.current.state.lensEnabled).toBe(true);
  });

  it('cancel reverts only the enable flags to the last saved snapshot', async () => {
    const result = await renderLoadedStore();

    await act(async () => {
      result.current.setEnabled('uspto', false);
      result.current.setEnabled('lens', true);
    });
    expect(result.current.isDirty).toBe(true);

    await act(async () => {
      result.current.cancel();
    });

    expect(result.current.isDirty).toBe(false);
    expect(result.current.state.usptoEnabled).toBe(true);
    expect(result.current.state.lensEnabled).toBe(false);
  });

  it('save PUTs only the three enable flags and clears the dirty flag on success', async () => {
    const result = await renderLoadedStore();
    await act(async () => {
      result.current.setEnabled('lens', true);
    });

    mockSavePatentConfig.mockResolvedValue({ ok: true, data: { ...configFixture, lens_enabled: true, config_version: 8 } });

    let saved: boolean | undefined;
    await act(async () => {
      saved = await result.current.save();
    });

    expect(saved).toBe(true);
    expect(mockSavePatentConfig).toHaveBeenCalledWith({ uspto_enabled: true, epo_enabled: true, lens_enabled: true });
    expect(result.current.isDirty).toBe(false);
    expect(result.current.state.configVersion).toBe(8);
    expect(result.current.state.saveSuccess).toBe(true);
  });

  it('keeps the previous flags and surfaces the error when save fails', async () => {
    const result = await renderLoadedStore();
    await act(async () => {
      result.current.setEnabled('lens', true);
    });

    mockSavePatentConfig.mockResolvedValue({ ok: false, error: { message: 'Version conflict.', correlationId: 'corr-1' } });

    let saved: boolean | undefined;
    await act(async () => {
      saved = await result.current.save();
    });

    expect(saved).toBe(false);
    expect(result.current.state.saveError).toEqual({ message: 'Version conflict.', correlationId: 'corr-1' });
    expect(result.current.state.lensEnabled).toBe(true);
  });

  it('submitSecret updates credential status from the response on success', async () => {
    const result = await renderLoadedStore();
    mockWritePatentSecret.mockResolvedValue({
      ok: true,
      data: { ...configFixture, lens_credential_status: 'set', config_version: 8 },
    });

    let writeResult: Awaited<ReturnType<typeof result.current.submitSecret>> | undefined;
    await act(async () => {
      writeResult = await result.current.submitSecret('lens', { api_key: 'secret-value' });
    });

    expect(writeResult?.ok).toBe(true);
    expect(mockWritePatentSecret).toHaveBeenCalledWith('lens', { api_key: 'secret-value' });
    expect(result.current.state.lensCredentialStatus).toBe('set');
    expect(result.current.state.configVersion).toBe(8);
  });

  it('submitSecret leaves credential status untouched on failure', async () => {
    const result = await renderLoadedStore();
    mockWritePatentSecret.mockResolvedValue({ ok: false, error: { message: 'Unable to store the credential.' } });

    let writeResult: Awaited<ReturnType<typeof result.current.submitSecret>> | undefined;
    await act(async () => {
      writeResult = await result.current.submitSecret('uspto', { api_key: 'bad-value' });
    });

    expect(writeResult?.ok).toBe(false);
    expect(result.current.state.usptoCredentialStatus).toBe('set');
  });

  it('testConnection transitions idle -> testing -> success with a timestamp', async () => {
    const result = await renderLoadedStore();
    expect(result.current.state.testResults.uspto.status).toBe('idle');

    let resolveProbe: (value: Awaited<ReturnType<typeof patentConfigRepository.testPatentConnection>>) => void;
    mockTestPatentConnection.mockReturnValue(new Promise((resolve) => (resolveProbe = resolve)));

    let pending: Promise<void>;
    act(() => {
      pending = result.current.testConnection('uspto');
    });
    expect(result.current.state.testResults.uspto.status).toBe('testing');

    await act(async () => {
      resolveProbe({ ok: true, data: { success: true } });
      await pending;
    });

    expect(result.current.state.testResults.uspto.status).toBe('success');
    expect(result.current.state.testResults.uspto.testedAt).toBeDefined();
  });

  it('testConnection records a failure reason when the probe reports success: false', async () => {
    const result = await renderLoadedStore();
    mockTestPatentConnection.mockResolvedValue({ ok: true, data: { success: false, failure_reason: '401 Unauthorized' } });

    await act(async () => {
      await result.current.testConnection('epo');
    });

    expect(result.current.state.testResults.epo.status).toBe('failure');
    expect(result.current.state.testResults.epo.message).toBe('401 Unauthorized');
  });

  it('testConnection surfaces a network/repository error as a failure with correlation id', async () => {
    const result = await renderLoadedStore();
    mockTestPatentConnection.mockResolvedValue({ ok: false, error: { message: 'Unable to connect to configuration service.', correlationId: 'corr-2' } });

    await act(async () => {
      await result.current.testConnection('lens');
    });

    expect(result.current.state.testResults.lens).toEqual({
      status: 'failure',
      message: 'Unable to connect to configuration service.',
      correlationId: 'corr-2',
    });
  });

  it('passes the supplied credential through to testConnection without persisting it', async () => {
    const result = await renderLoadedStore();
    mockTestPatentConnection.mockResolvedValue({ ok: true, data: { success: true } });

    await act(async () => {
      await result.current.testConnection('uspto', { api_key: 'typed-but-not-saved' });
    });

    expect(mockTestPatentConnection).toHaveBeenCalledWith('uspto', { api_key: 'typed-but-not-saved' });
    expect(mockWritePatentSecret).not.toHaveBeenCalled();
  });
});
