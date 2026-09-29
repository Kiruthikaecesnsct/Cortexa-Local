import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useLoginStore } from '../useLoginStore';

vi.mock('../authRepository', () => ({
  loginWithCredentials: vi.fn(),
  loginWithEntra: vi.fn(),
}));

vi.mock('../../../core/auth/msalClient', () => ({
  loginWithEntra: vi.fn(),
}));

vi.mock('../../../core/auth/msalConfig', () => ({
  entraConfigured: true,
}));

vi.mock('../../../core/auth/tokenStore', () => ({
  setAccessToken: vi.fn(),
  getAccessToken: vi.fn(),
}));

import { loginWithCredentials, loginWithEntra as loginWithEntraRepo } from '../authRepository';
import { loginWithEntra as msalLoginWithEntra } from '../../../core/auth/msalClient';
import { setAccessToken } from '../../../core/auth/tokenStore';

const mockLogin = loginWithCredentials as ReturnType<typeof vi.fn>;
const mockLoginEntraRepo = loginWithEntraRepo as ReturnType<typeof vi.fn>;
const mockMsalLogin = msalLoginWithEntra as ReturnType<typeof vi.fn>;
const mockSetAccessToken = setAccessToken as ReturnType<typeof vi.fn>;

describe('useLoginStore', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('initialises with idle status and empty fields', () => {
    const { result } = renderHook(() => useLoginStore());
    expect(result.current.status).toBe('idle');
    expect(result.current.email).toBe('');
    expect(result.current.password).toBe('');
    expect(result.current.authError).toBeNull();
  });

  it('sets field errors without hitting the API when email is empty', async () => {
    const { result } = renderHook(() => useLoginStore());

    await act(async () => {
      await result.current.submit();
    });

    expect(result.current.fieldErrors.email).toBeTruthy();
    expect(result.current.status).toBe('idle');
    expect(mockLogin).not.toHaveBeenCalled();
  });

  it('sets email format error for invalid email', async () => {
    const { result } = renderHook(() => useLoginStore());

    act(() => {
      result.current.setEmail('notanemail');
      result.current.setPassword('secret');
    });

    await act(async () => {
      await result.current.submit();
    });

    expect(result.current.fieldErrors.email).toMatch(/valid email/i);
  });

  it('sets password required error when password is empty', async () => {
    const { result } = renderHook(() => useLoginStore());

    act(() => {
      result.current.setEmail('user@company.com');
    });

    await act(async () => {
      await result.current.submit();
    });

    expect(result.current.fieldErrors.password).toMatch(/required/i);
  });

  it('transitions to success and stores token on valid credentials', async () => {
    mockLogin.mockResolvedValueOnce({
      ok: true,
      data: { access_token: 'acc', expires_at: '2026-12-31T00:00:00Z' },
    });

    const { result } = renderHook(() => useLoginStore());

    act(() => {
      result.current.setEmail('user@company.com');
      result.current.setPassword('Password1');
    });

    await act(async () => {
      await result.current.submit();
    });

    expect(result.current.status).toBe('success');
    expect(mockSetAccessToken).toHaveBeenCalledWith('acc', '2026-12-31T00:00:00Z');
    expect(result.current.password).toBe('');
  });

  it('sets credential authError and clears password on 401', async () => {
    mockLogin.mockResolvedValueOnce({
      ok: false,
      error: { kind: 'credential', message: 'Incorrect email or password.' },
    });

    const { result } = renderHook(() => useLoginStore());

    act(() => {
      result.current.setEmail('user@company.com');
      result.current.setPassword('wrongpass');
    });

    await act(async () => {
      await result.current.submit();
    });

    expect(result.current.status).toBe('idle');
    expect(result.current.authError?.kind).toBe('credential');
    expect(result.current.password).toBe('');
  });

  it('sets network authError and preserves password on service error', async () => {
    mockLogin.mockResolvedValueOnce({
      ok: false,
      error: { kind: 'network', message: "Can't reach Cortexa.", correlationId: 'abc-123' },
    });

    const { result } = renderHook(() => useLoginStore());

    act(() => {
      result.current.setEmail('user@company.com');
      result.current.setPassword('mypassword');
    });

    await act(async () => {
      await result.current.submit();
    });

    expect(result.current.authError?.kind).toBe('network');
    expect(result.current.authError?.correlationId).toBe('abc-123');
    expect(result.current.password).toBe('mypassword');
  });

  it('toggles password visibility', () => {
    const { result } = renderHook(() => useLoginStore());
    expect(result.current.passwordVisible).toBe(false);

    act(() => {
      result.current.togglePasswordVisible();
    });

    expect(result.current.passwordVisible).toBe(true);
  });

  it('clears email field error when email is updated', async () => {
    const { result } = renderHook(() => useLoginStore());

    act(() => {
      result.current.setPassword('pass');
    });

    await act(async () => {
      await result.current.submit();
    });

    expect(result.current.fieldErrors.email).toBeTruthy();

    act(() => {
      result.current.setEmail('valid@email.com');
    });

    expect(result.current.fieldErrors.email).toBeUndefined();
  });

  it('shows SSO Phase-2 notice when submitEntra is called', async () => {
    const { result } = renderHook(() => useLoginStore());

    await act(async () => {
      await result.current.submitEntra();
    });

    expect(result.current.ssoNotice).toBe(true);
    expect(result.current.status).toBe('idle');
    expect(mockMsalLogin).not.toHaveBeenCalled();
    expect(mockLoginEntraRepo).not.toHaveBeenCalled();
    expect(mockSetAccessToken).not.toHaveBeenCalled();
  });
});
