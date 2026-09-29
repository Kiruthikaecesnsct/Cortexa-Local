import { ApiClient } from '../../core/api/client';
import type { ApiResponse } from '../../core/api/envelope';
import type { TokenPair, LoginCredentials, EntraLoginRequest, AuthError, RegisterRequest, RegisterResult } from '../../core/auth/authTypes';
import { clearTokens, getAccessToken } from '../../core/auth/tokenStore';
import { apiBaseUrl } from '../../core/config/env';

type AuthResult = { ok: true; data: TokenPair } | { ok: false; error: AuthError };

const client = new ApiClient(apiBaseUrl, getAccessToken);

function toAuthError(raw: ApiResponse<TokenPair>, fallbackCorrelationId?: string): AuthError {
  const correlationId = raw.correlation_id ?? fallbackCorrelationId;
  if (raw.error_code === 'INVALID_CREDENTIALS') {
    return { kind: 'credential', message: 'Incorrect email or password.', correlationId };
  }
  return {
    kind: 'network',
    message: raw.message ?? 'An unexpected error occurred.',
    correlationId,
  };
}

function networkError(correlationId?: string): AuthError {
  return {
    kind: 'network',
    message: "Can’t reach Cortexa right now. The sign-in service didn’t respond.",
    correlationId,
  };
}

export async function loginWithCredentials(credentials: LoginCredentials): Promise<AuthResult> {
  try {
    const response = await client.post<TokenPair>('/auth/login', {
      email: credentials.email,
      password: credentials.password,
      remember_me: credentials.rememberMe,
    });
    if (response.success && response.data) {
      return { ok: true, data: response.data };
    }
    return { ok: false, error: toAuthError(response) };
  } catch (err) {
    const correlationId = extractCorrelationId(err);
    return { ok: false, error: networkError(correlationId) };
  }
}

export async function loginWithEntra(request: EntraLoginRequest): Promise<AuthResult> {
  try {
    const response = await client.post<TokenPair>('/auth/entra', request);
    if (response.success && response.data) {
      return { ok: true, data: response.data };
    }
    return { ok: false, error: toAuthError(response) };
  } catch (err) {
    const correlationId = extractCorrelationId(err);
    return { ok: false, error: networkError(correlationId) };
  }
}

export async function logout(): Promise<void> {
  try {
    await client.post<void>('/auth/logout', undefined);
  } finally {
    clearTokens();
  }
}

export async function registerAccount(
  request: RegisterRequest,
): Promise<{ ok: true; data: RegisterResult } | { ok: false; error: AuthError }> {
  try {
    const response = await client.post<{ user_id: string; email: string; display_name: string }>('/auth/register', {
      email: request.email,
      password: request.password,
      display_name: request.fullName,
    });
    if (response.success && response.data) {
      return { ok: true, data: { registered: true, email: response.data.email } };
    }
    return { ok: false, error: toRegisterError(response) };
  } catch (err) {
    const correlationId = extractCorrelationId(err);
    return { ok: false, error: networkError(correlationId) };
  }
}

function toRegisterError(raw: ApiResponse<unknown>, fallbackCorrelationId?: string): AuthError {
  const correlationId = raw.correlation_id ?? fallbackCorrelationId;
  if (raw.error_code === 'DUPLICATE_EMAIL') {
    return { kind: 'validation', message: 'That email is already registered. Try signing in.', correlationId };
  }
  if (raw.error_code && raw.error_code.includes('VALIDATION')) {
    return { kind: 'validation', message: raw.message ?? 'Please check your details and try again.', correlationId };
  }
  return {
    kind: 'network',
    message: raw.message ?? 'An unexpected error occurred.',
    correlationId,
  };
}

function extractCorrelationId(err: unknown): string | undefined {
  if (
    err !== null &&
    typeof err === 'object' &&
    'response' in err &&
    err.response !== null &&
    typeof err.response === 'object' &&
    'data' in err.response
  ) {
    const data = (err.response as { data?: ApiResponse<unknown> }).data;
    return data?.correlation_id;
  }
  return undefined;
}
