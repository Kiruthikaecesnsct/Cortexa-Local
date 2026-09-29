import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { ReactNode } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { ToastProvider } from '../../../shared/ds/Toast';
import { useSignUpStore } from '../useSignUpStore';

vi.mock('../authRepository', () => ({
  registerAccount: vi.fn().mockResolvedValue({ ok: true, data: { registered: true, email: 'user@example.com' } }),
}));

import { registerAccount } from '../authRepository';

const mockRegisterAccount = registerAccount as ReturnType<typeof vi.fn>;

function wrapper({ children }: { children: ReactNode }) {
  return (
    <MemoryRouter>
      <ToastProvider>{children}</ToastProvider>
    </MemoryRouter>
  );
}

const VALID_FULL_NAME = 'Jane Smith';
const VALID_EMAIL = 'jane@example.com';
const VALID_PASSWORD = 'Secure1pass';
const FULL_NAME_REQUIRED = 'Full name is required.';
const EMAIL_REQUIRED = 'Email is required.';
const EMAIL_INVALID = 'Enter a valid email address.';
const PASSWORD_WEAK = 'Use 8+ characters with an uppercase letter and a number.';
const PASSWORDS_MISMATCH = "Passwords don't match.";

describe('useSignUpStore', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('initialises with idle status and empty fields', () => {
    const { result } = renderHook(() => useSignUpStore(), { wrapper });

    expect(result.current.status).toBe('idle');
    expect(result.current.fullName).toBe('');
    expect(result.current.email).toBe('');
    expect(result.current.password).toBe('');
    expect(result.current.confirmPassword).toBe('');
    expect(result.current.fieldErrors).toEqual({});
    expect(result.current.authError).toBeNull();
  });

  describe('fullName validation', () => {
    it('sets fullName required error when fullName is empty', async () => {
      const { result } = renderHook(() => useSignUpStore(), { wrapper });

      act(() => {
        result.current.setEmail(VALID_EMAIL);
        result.current.setPassword(VALID_PASSWORD);
        result.current.setConfirmPassword(VALID_PASSWORD);
      });

      await act(async () => {
        await result.current.submit();
      });

      expect(result.current.fieldErrors.fullName).toBe(FULL_NAME_REQUIRED);
      expect(result.current.status).toBe('idle');
      expect(mockRegisterAccount).not.toHaveBeenCalled();
    });

    it('sets fullName required error when fullName is only whitespace', async () => {
      const { result } = renderHook(() => useSignUpStore(), { wrapper });

      act(() => {
        result.current.setFullName('   ');
        result.current.setEmail(VALID_EMAIL);
        result.current.setPassword(VALID_PASSWORD);
        result.current.setConfirmPassword(VALID_PASSWORD);
      });

      await act(async () => {
        await result.current.submit();
      });

      expect(result.current.fieldErrors.fullName).toBe(FULL_NAME_REQUIRED);
      expect(mockRegisterAccount).not.toHaveBeenCalled();
    });

    it('clears fullName field error when fullName is updated', async () => {
      const { result } = renderHook(() => useSignUpStore(), { wrapper });

      await act(async () => {
        await result.current.submit();
      });

      expect(result.current.fieldErrors.fullName).toBeTruthy();

      act(() => {
        result.current.setFullName(VALID_FULL_NAME);
      });

      expect(result.current.fieldErrors.fullName).toBeUndefined();
    });
  });

  describe('email validation', () => {
    it('sets email required error when email is empty', async () => {
      const { result } = renderHook(() => useSignUpStore(), { wrapper });

      act(() => {
        result.current.setFullName(VALID_FULL_NAME);
        result.current.setPassword(VALID_PASSWORD);
        result.current.setConfirmPassword(VALID_PASSWORD);
      });

      await act(async () => {
        await result.current.submit();
      });

      expect(result.current.fieldErrors.email).toBe(EMAIL_REQUIRED);
      expect(mockRegisterAccount).not.toHaveBeenCalled();
    });

    it('sets email format error when email has no @ symbol', async () => {
      const { result } = renderHook(() => useSignUpStore(), { wrapper });

      act(() => {
        result.current.setFullName(VALID_FULL_NAME);
        result.current.setEmail('notanemail');
        result.current.setPassword(VALID_PASSWORD);
        result.current.setConfirmPassword(VALID_PASSWORD);
      });

      await act(async () => {
        await result.current.submit();
      });

      expect(result.current.fieldErrors.email).toBe(EMAIL_INVALID);
      expect(mockRegisterAccount).not.toHaveBeenCalled();
    });

    it('sets email format error when email is missing domain', async () => {
      const { result } = renderHook(() => useSignUpStore(), { wrapper });

      act(() => {
        result.current.setFullName(VALID_FULL_NAME);
        result.current.setEmail('user@');
        result.current.setPassword(VALID_PASSWORD);
        result.current.setConfirmPassword(VALID_PASSWORD);
      });

      await act(async () => {
        await result.current.submit();
      });

      expect(result.current.fieldErrors.email).toBe(EMAIL_INVALID);
      expect(mockRegisterAccount).not.toHaveBeenCalled();
    });

    it('clears email field error when email is updated', async () => {
      const { result } = renderHook(() => useSignUpStore(), { wrapper });

      act(() => {
        result.current.setFullName(VALID_FULL_NAME);
        result.current.setPassword(VALID_PASSWORD);
        result.current.setConfirmPassword(VALID_PASSWORD);
      });

      await act(async () => {
        await result.current.submit();
      });

      expect(result.current.fieldErrors.email).toBeTruthy();

      act(() => {
        result.current.setEmail(VALID_EMAIL);
      });

      expect(result.current.fieldErrors.email).toBeUndefined();
    });
  });

  describe('password validation', () => {
    it('sets password weak error when password is empty', async () => {
      const { result } = renderHook(() => useSignUpStore(), { wrapper });

      act(() => {
        result.current.setFullName(VALID_FULL_NAME);
        result.current.setEmail(VALID_EMAIL);
        result.current.setConfirmPassword('');
      });

      await act(async () => {
        await result.current.submit();
      });

      expect(result.current.fieldErrors.password).toBe(PASSWORD_WEAK);
      expect(mockRegisterAccount).not.toHaveBeenCalled();
    });

    it('sets password weak error when password is fewer than 8 characters', async () => {
      const { result } = renderHook(() => useSignUpStore(), { wrapper });

      act(() => {
        result.current.setFullName(VALID_FULL_NAME);
        result.current.setEmail(VALID_EMAIL);
        result.current.setPassword('Ab1');
        result.current.setConfirmPassword('Ab1');
      });

      await act(async () => {
        await result.current.submit();
      });

      expect(result.current.fieldErrors.password).toBe(PASSWORD_WEAK);
      expect(mockRegisterAccount).not.toHaveBeenCalled();
    });

    it('sets password weak error when password has no uppercase letter', async () => {
      const { result } = renderHook(() => useSignUpStore(), { wrapper });

      act(() => {
        result.current.setFullName(VALID_FULL_NAME);
        result.current.setEmail(VALID_EMAIL);
        result.current.setPassword('nouppercase1');
        result.current.setConfirmPassword('nouppercase1');
      });

      await act(async () => {
        await result.current.submit();
      });

      expect(result.current.fieldErrors.password).toBe(PASSWORD_WEAK);
      expect(mockRegisterAccount).not.toHaveBeenCalled();
    });

    it('sets password weak error when password has no digit', async () => {
      const { result } = renderHook(() => useSignUpStore(), { wrapper });

      act(() => {
        result.current.setFullName(VALID_FULL_NAME);
        result.current.setEmail(VALID_EMAIL);
        result.current.setPassword('NoDigitHere');
        result.current.setConfirmPassword('NoDigitHere');
      });

      await act(async () => {
        await result.current.submit();
      });

      expect(result.current.fieldErrors.password).toBe(PASSWORD_WEAK);
      expect(mockRegisterAccount).not.toHaveBeenCalled();
    });

    it('clears password field error when password is updated', async () => {
      const { result } = renderHook(() => useSignUpStore(), { wrapper });

      act(() => {
        result.current.setFullName(VALID_FULL_NAME);
        result.current.setEmail(VALID_EMAIL);
        result.current.setPassword('weak');
        result.current.setConfirmPassword('weak');
      });

      await act(async () => {
        await result.current.submit();
      });

      expect(result.current.fieldErrors.password).toBeTruthy();

      act(() => {
        result.current.setPassword(VALID_PASSWORD);
      });

      expect(result.current.fieldErrors.password).toBeUndefined();
    });
  });

  describe('confirmPassword validation', () => {
    it('sets confirmPassword mismatch error when passwords differ', async () => {
      const { result } = renderHook(() => useSignUpStore(), { wrapper });

      act(() => {
        result.current.setFullName(VALID_FULL_NAME);
        result.current.setEmail(VALID_EMAIL);
        result.current.setPassword(VALID_PASSWORD);
        result.current.setConfirmPassword('DifferentPass1');
      });

      await act(async () => {
        await result.current.submit();
      });

      expect(result.current.fieldErrors.confirmPassword).toBe(PASSWORDS_MISMATCH);
      expect(mockRegisterAccount).not.toHaveBeenCalled();
    });

    it('clears confirmPassword field error when confirmPassword is updated', async () => {
      const { result } = renderHook(() => useSignUpStore(), { wrapper });

      act(() => {
        result.current.setFullName(VALID_FULL_NAME);
        result.current.setEmail(VALID_EMAIL);
        result.current.setPassword(VALID_PASSWORD);
        result.current.setConfirmPassword('WrongPass1');
      });

      await act(async () => {
        await result.current.submit();
      });

      expect(result.current.fieldErrors.confirmPassword).toBeTruthy();

      act(() => {
        result.current.setConfirmPassword(VALID_PASSWORD);
      });

      expect(result.current.fieldErrors.confirmPassword).toBeUndefined();
    });
  });

  describe('successful submission', () => {
    it('calls registerAccount and navigates to login when all fields are valid', async () => {
      mockRegisterAccount.mockResolvedValueOnce({
        ok: true,
        data: { registered: true, email: VALID_EMAIL },
      });

      const { result } = renderHook(() => useSignUpStore(), { wrapper });

      act(() => {
        result.current.setFullName(VALID_FULL_NAME);
        result.current.setEmail(VALID_EMAIL);
        result.current.setPassword(VALID_PASSWORD);
        result.current.setConfirmPassword(VALID_PASSWORD);
      });

      await act(async () => {
        await result.current.submit();
      });

      expect(result.current.fieldErrors).toEqual({});
      expect(result.current.status).toBe('success');
      expect(mockRegisterAccount).toHaveBeenCalledOnce();
      expect(mockRegisterAccount).toHaveBeenCalledWith({
        fullName: VALID_FULL_NAME,
        email: VALID_EMAIL,
        password: VALID_PASSWORD,
      });
    });

    it('trims fullName and email before calling registerAccount', async () => {
      mockRegisterAccount.mockResolvedValueOnce({
        ok: true,
        data: { registered: true, email: VALID_EMAIL },
      });

      const { result } = renderHook(() => useSignUpStore(), { wrapper });

      act(() => {
        result.current.setFullName('  Jane Smith  ');
        result.current.setEmail('  jane@example.com  ');
        result.current.setPassword(VALID_PASSWORD);
        result.current.setConfirmPassword(VALID_PASSWORD);
      });

      await act(async () => {
        await result.current.submit();
      });

      expect(mockRegisterAccount).toHaveBeenCalledWith({
        fullName: 'Jane Smith',
        email: 'jane@example.com',
        password: VALID_PASSWORD,
      });
    });

    it('sets submitting status while registerAccount is pending', async () => {
      let resolveRegister!: (value: unknown) => void;
      mockRegisterAccount.mockReturnValueOnce(
        new Promise((resolve) => {
          resolveRegister = resolve;
        }),
      );

      const { result } = renderHook(() => useSignUpStore(), { wrapper });

      act(() => {
        result.current.setFullName(VALID_FULL_NAME);
        result.current.setEmail(VALID_EMAIL);
        result.current.setPassword(VALID_PASSWORD);
        result.current.setConfirmPassword(VALID_PASSWORD);
      });

      act(() => {
        result.current.submit();
      });

      expect(result.current.status).toBe('submitting');

      await act(async () => {
        resolveRegister({ ok: true, data: { registered: true, email: VALID_EMAIL } });
      });

      expect(result.current.status).toBe('success');
    });
  });

  describe('network error handling', () => {
    it('sets authError and returns to idle status on registerAccount failure', async () => {
      mockRegisterAccount.mockResolvedValueOnce({
        ok: false,
        error: { kind: 'network', message: 'Registration service unavailable.', correlationId: 'err-42' },
      });

      const { result } = renderHook(() => useSignUpStore(), { wrapper });

      act(() => {
        result.current.setFullName(VALID_FULL_NAME);
        result.current.setEmail(VALID_EMAIL);
        result.current.setPassword(VALID_PASSWORD);
        result.current.setConfirmPassword(VALID_PASSWORD);
      });

      await act(async () => {
        await result.current.submit();
      });

      expect(result.current.status).toBe('idle');
      expect(result.current.authError?.kind).toBe('network');
      expect(result.current.authError?.message).toBe('Registration service unavailable.');
    });

    it('clears authError via clearAuthError', async () => {
      mockRegisterAccount.mockResolvedValueOnce({
        ok: false,
        error: { kind: 'network', message: 'Service error.' },
      });

      const { result } = renderHook(() => useSignUpStore(), { wrapper });

      act(() => {
        result.current.setFullName(VALID_FULL_NAME);
        result.current.setEmail(VALID_EMAIL);
        result.current.setPassword(VALID_PASSWORD);
        result.current.setConfirmPassword(VALID_PASSWORD);
      });

      await act(async () => {
        await result.current.submit();
      });

      expect(result.current.authError).not.toBeNull();

      act(() => {
        result.current.clearAuthError();
      });

      expect(result.current.authError).toBeNull();
    });
  });
});
