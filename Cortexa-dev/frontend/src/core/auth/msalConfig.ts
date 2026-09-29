import type { Configuration } from '@azure/msal-browser';
import { entraTenantId, entraClientId, entraRedirectUri, entraApiScope } from '../config/env';

export const entraConfigured = Boolean(entraTenantId && entraClientId);

export const msalConfig: Configuration = {
  auth: {
    clientId: entraClientId || 'not-configured',
    authority: `https://login.microsoftonline.com/${entraTenantId || 'common'}`,
    redirectUri: entraRedirectUri || (typeof window !== 'undefined' ? window.location.origin : '/'),
    postLogoutRedirectUri: typeof window !== 'undefined' ? window.location.origin : '/',
    navigateToLoginRequestUrl: false,
  },
  cache: {
    cacheLocation: 'sessionStorage',
    storeAuthStateInCookie: false,
  },
};

export const loginScopes: string[] = entraApiScope
  ? [entraApiScope]
  : entraClientId
    ? [`api://${entraClientId}/access_as_user`]
    : [];
