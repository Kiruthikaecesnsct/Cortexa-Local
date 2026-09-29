const store = new Map<string, string>();

const ACCESS_KEY = 'access';
const REFRESH_KEY = 'refresh';
const EXPIRY_KEY = 'expires_at';

export function getAccessToken(): string | undefined {
  return store.get(ACCESS_KEY);
}

export function getRefreshToken(): string | undefined {
  return store.get(REFRESH_KEY);
}

export function getTokenExpiry(): string | undefined {
  return store.get(EXPIRY_KEY);
}

export function setAccessToken(access: string, expiresAt?: string): void {
  store.set(ACCESS_KEY, access);
  if (expiresAt !== undefined) {
    store.set(EXPIRY_KEY, expiresAt);
  } else {
    store.delete(EXPIRY_KEY);
  }
}

export function setTokens(access: string, refresh?: string): void {
  store.set(ACCESS_KEY, access);
  if (refresh !== undefined) {
    store.set(REFRESH_KEY, refresh);
  } else {
    store.delete(REFRESH_KEY);
  }
}

export function clearTokens(): void {
  store.delete(ACCESS_KEY);
  store.delete(REFRESH_KEY);
  store.delete(EXPIRY_KEY);
}
