import { useReducer, useCallback } from 'react';
import { setAccessToken } from '../../core/auth/tokenStore';
import type { AuthError } from '../../core/auth/authTypes';
import { loginWithCredentials } from './authRepository';

export type LoginStatus = 'idle' | 'submitting' | 'success';

export interface FieldErrors {
  email?: string;
  password?: string;
}

export interface LoginState {
  email: string;
  password: string;
  rememberMe: boolean;
  passwordVisible: boolean;
  status: LoginStatus;
  fieldErrors: FieldErrors;
  authError: AuthError | null;
  ssoNotice: boolean;
}

type LoginAction =
  | { type: 'SET_EMAIL'; value: string }
  | { type: 'SET_PASSWORD'; value: string }
  | { type: 'SET_REMEMBER_ME'; value: boolean }
  | { type: 'TOGGLE_PASSWORD_VISIBLE' }
  | { type: 'CLEAR_AUTH_ERROR' }
  | { type: 'SUBMIT_START' }
  | { type: 'SUBMIT_SUCCESS' }
  | { type: 'SUBMIT_CREDENTIAL_ERROR'; error: AuthError }
  | { type: 'SUBMIT_NETWORK_ERROR'; error: AuthError; preservePassword: string }
  | { type: 'SET_FIELD_ERRORS'; errors: FieldErrors }
  | { type: 'SHOW_SSO_NOTICE' };

const initialState: LoginState = {
  email: '',
  password: '',
  rememberMe: false,
  passwordVisible: false,
  status: 'idle',
  fieldErrors: {},
  authError: null,
  ssoNotice: false,
};

function loginReducer(state: LoginState, action: LoginAction): LoginState {
  switch (action.type) {
    case 'SET_EMAIL':
      return { ...state, email: action.value, fieldErrors: { ...state.fieldErrors, email: undefined }, ssoNotice: false };
    case 'SET_PASSWORD':
      return { ...state, password: action.value, fieldErrors: { ...state.fieldErrors, password: undefined }, ssoNotice: false };
    case 'SET_REMEMBER_ME':
      return { ...state, rememberMe: action.value };
    case 'TOGGLE_PASSWORD_VISIBLE':
      return { ...state, passwordVisible: !state.passwordVisible };
    case 'CLEAR_AUTH_ERROR':
      return { ...state, authError: null };
    case 'SUBMIT_START':
      return { ...state, status: 'submitting', authError: null, fieldErrors: {}, ssoNotice: false };
    case 'SUBMIT_SUCCESS':
      return { ...state, status: 'success', password: '' };
    case 'SUBMIT_CREDENTIAL_ERROR':
      return { ...state, status: 'idle', authError: action.error, password: '' };
    case 'SUBMIT_NETWORK_ERROR':
      return { ...state, status: 'idle', authError: action.error, password: action.preservePassword };
    case 'SET_FIELD_ERRORS':
      return { ...state, fieldErrors: action.errors, authError: null };
    case 'SHOW_SSO_NOTICE':
      return { ...state, status: 'idle', ssoNotice: true, authError: null };
    default:
      return state;
  }
}

function validateFields(email: string, password: string): FieldErrors {
  const errors: FieldErrors = {};
  if (!email.trim()) {
    errors.email = 'Work email is required.';
  } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) {
    errors.email = 'Enter a valid email address.';
  }
  if (!password) {
    errors.password = 'Password is required.';
  }
  return errors;
}

export function useLoginStore() {
  const [state, dispatch] = useReducer(loginReducer, initialState);

  const setEmail = useCallback((value: string) => dispatch({ type: 'SET_EMAIL', value }), []);
  const setPassword = useCallback((value: string) => dispatch({ type: 'SET_PASSWORD', value }), []);
  const setRememberMe = useCallback((value: boolean) => dispatch({ type: 'SET_REMEMBER_ME', value }), []);
  const togglePasswordVisible = useCallback(() => dispatch({ type: 'TOGGLE_PASSWORD_VISIBLE' }), []);
  const clearAuthError = useCallback(() => dispatch({ type: 'CLEAR_AUTH_ERROR' }), []);

  const submit = useCallback(async () => {
    const { email, password, rememberMe } = state;
    const fieldErrors = validateFields(email, password);
    if (Object.keys(fieldErrors).length > 0) {
      dispatch({ type: 'SET_FIELD_ERRORS', errors: fieldErrors });
      return;
    }

    dispatch({ type: 'SUBMIT_START' });

    const result = await loginWithCredentials({ email: email.trim(), password, rememberMe });

    if (result.ok) {
      setAccessToken(result.data.access_token, result.data.expires_at);
      dispatch({ type: 'SUBMIT_SUCCESS' });
    } else if (result.error.kind === 'credential') {
      dispatch({ type: 'SUBMIT_CREDENTIAL_ERROR', error: result.error });
    } else {
      dispatch({ type: 'SUBMIT_NETWORK_ERROR', error: result.error, preservePassword: password });
    }
  }, [state]);

  const submitEntra = useCallback(async () => {
    dispatch({ type: 'SHOW_SSO_NOTICE' });
  }, []);

  return {
    ...state,
    setEmail,
    setPassword,
    setRememberMe,
    togglePasswordVisible,
    clearAuthError,
    submit,
    submitEntra,
  };
}
