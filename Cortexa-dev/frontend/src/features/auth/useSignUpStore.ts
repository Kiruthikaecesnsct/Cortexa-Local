import { useReducer, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import type { AuthError } from '../../core/auth/authTypes';
import { useToast } from '../../shared/ds/Toast';
import { registerAccount } from './authRepository';

export type SignUpStatus = 'idle' | 'submitting' | 'success';

export interface SignUpFieldErrors {
  fullName?: string;
  email?: string;
  password?: string;
  confirmPassword?: string;
}

interface SignUpState {
  fullName: string;
  email: string;
  password: string;
  confirmPassword: string;
  status: SignUpStatus;
  fieldErrors: SignUpFieldErrors;
  authError: AuthError | null;
}

type SignUpAction =
  | { type: 'SET_FULL_NAME'; value: string }
  | { type: 'SET_EMAIL'; value: string }
  | { type: 'SET_PASSWORD'; value: string }
  | { type: 'SET_CONFIRM_PASSWORD'; value: string }
  | { type: 'CLEAR_AUTH_ERROR' }
  | { type: 'SUBMIT_START' }
  | { type: 'SUBMIT_SUCCESS' }
  | { type: 'SUBMIT_NETWORK_ERROR'; error: AuthError }
  | { type: 'SET_FIELD_ERRORS'; errors: SignUpFieldErrors };

const initialState: SignUpState = {
  fullName: '',
  email: '',
  password: '',
  confirmPassword: '',
  status: 'idle',
  fieldErrors: {},
  authError: null,
};

type ActionHandler = (state: SignUpState, action: SignUpAction) => SignUpState;

const handlers: Partial<Record<SignUpAction['type'], ActionHandler>> = {
  SET_FULL_NAME: (s, a) => ({ ...s, fullName: (a as Extract<SignUpAction, { type: 'SET_FULL_NAME' }>).value, fieldErrors: { ...s.fieldErrors, fullName: undefined } }),
  SET_EMAIL: (s, a) => ({ ...s, email: (a as Extract<SignUpAction, { type: 'SET_EMAIL' }>).value, fieldErrors: { ...s.fieldErrors, email: undefined } }),
  SET_PASSWORD: (s, a) => ({ ...s, password: (a as Extract<SignUpAction, { type: 'SET_PASSWORD' }>).value, fieldErrors: { ...s.fieldErrors, password: undefined } }),
  SET_CONFIRM_PASSWORD: (s, a) => ({ ...s, confirmPassword: (a as Extract<SignUpAction, { type: 'SET_CONFIRM_PASSWORD' }>).value, fieldErrors: { ...s.fieldErrors, confirmPassword: undefined } }),
  CLEAR_AUTH_ERROR: (s) => ({ ...s, authError: null }),
  SUBMIT_START: (s) => ({ ...s, status: 'submitting', authError: null, fieldErrors: {} }),
  SUBMIT_SUCCESS: (s) => ({ ...s, status: 'success' }),
  SUBMIT_NETWORK_ERROR: (s, a) => ({ ...s, status: 'idle', authError: (a as Extract<SignUpAction, { type: 'SUBMIT_NETWORK_ERROR' }>).error }),
  SET_FIELD_ERRORS: (s, a) => ({ ...s, fieldErrors: (a as Extract<SignUpAction, { type: 'SET_FIELD_ERRORS' }>).errors, authError: null }),
};

function signUpReducer(state: SignUpState, action: SignUpAction): SignUpState {
  return handlers[action.type]?.(state, action) ?? state;
}

const EMAIL_REGEX = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const PASSWORD_REGEX = /^(?=.*[A-Z])(?=.*[0-9]).{8,}$/;

function validateFullName(fullName: string): string | undefined {
  if (!fullName.trim()) return 'Full name is required.';
  return undefined;
}

function validateEmail(email: string): string | undefined {
  if (!email.trim()) return 'Email is required.';
  if (!EMAIL_REGEX.test(email.trim())) return 'Enter a valid email address.';
  return undefined;
}

function validatePassword(password: string): string | undefined {
  if (!PASSWORD_REGEX.test(password)) return 'Use 8+ characters with an uppercase letter and a number.';
  return undefined;
}

function validateConfirmPassword(password: string, confirmPassword: string): string | undefined {
  if (confirmPassword !== password) return "Passwords don't match.";
  return undefined;
}

function validateFields(
  fullName: string,
  email: string,
  password: string,
  confirmPassword: string,
): SignUpFieldErrors {
  const candidates = {
    fullName: validateFullName(fullName),
    email: validateEmail(email),
    password: validatePassword(password),
    confirmPassword: validateConfirmPassword(password, confirmPassword),
  };
  return Object.fromEntries(
    Object.entries(candidates).filter(([, v]) => v !== undefined),
  ) as SignUpFieldErrors;
}

export function useSignUpStore() {
  const [state, dispatch] = useReducer(signUpReducer, initialState);
  const navigate = useNavigate();
  const { show: showToast } = useToast();

  const setFullName = useCallback((value: string) => dispatch({ type: 'SET_FULL_NAME', value }), []);
  const setEmail = useCallback((value: string) => dispatch({ type: 'SET_EMAIL', value }), []);
  const setPassword = useCallback((value: string) => dispatch({ type: 'SET_PASSWORD', value }), []);
  const setConfirmPassword = useCallback((value: string) => dispatch({ type: 'SET_CONFIRM_PASSWORD', value }), []);
  const clearAuthError = useCallback(() => dispatch({ type: 'CLEAR_AUTH_ERROR' }), []);

  const submit = useCallback(async () => {
    const { fullName, email, password, confirmPassword } = state;
    const fieldErrors = validateFields(fullName, email, password, confirmPassword);
    if (Object.keys(fieldErrors).length > 0) {
      dispatch({ type: 'SET_FIELD_ERRORS', errors: fieldErrors });
      return;
    }

    dispatch({ type: 'SUBMIT_START' });

    const result = await registerAccount({ fullName: fullName.trim(), email: email.trim(), password });

    if (result.ok) {
      dispatch({ type: 'SUBMIT_SUCCESS' });
      showToast({
        variant: 'success',
        title: 'Account created successfully',
        message: 'You can now sign in with your credentials.',
      });
      navigate('/login', { replace: true });
    } else {
      dispatch({ type: 'SUBMIT_NETWORK_ERROR', error: result.error });
      showToast({
        variant: 'error',
        title: 'Sign-up failed',
        message: result.error.message,
      });
    }
  }, [state, showToast, navigate]);

  return {
    ...state,
    setFullName,
    setEmail,
    setPassword,
    setConfirmPassword,
    clearAuthError,
    submit,
  };
}
