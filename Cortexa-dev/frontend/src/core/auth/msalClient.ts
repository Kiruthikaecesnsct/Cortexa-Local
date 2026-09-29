import { PublicClientApplication } from '@azure/msal-browser';
import { msalConfig, loginScopes, entraConfigured } from './msalConfig';
import { entraClientId } from '../config/env';

const pca = new PublicClientApplication(msalConfig);

export async function initializeMsal(): Promise<void> {
  if (!entraConfigured) {
    return;
  }
  await pca.initialize();
}

export async function loginWithEntra(): Promise<string> {
  if (!entraClientId) {
    throw new Error('[Cortexa] Entra ID is not configured. Set VITE_ENTRA_TENANT_ID and VITE_ENTRA_CLIENT_ID.');
  }
  const loginResponse = await pca.loginPopup({ scopes: loginScopes });
  try {
    const tokenResponse = await pca.acquireTokenSilent({
      scopes: loginScopes,
      account: loginResponse.account,
    });
    return tokenResponse.accessToken;
  } catch {
    const tokenResponse = await pca.acquireTokenPopup({
      scopes: loginScopes,
      account: loginResponse.account,
    });
    return tokenResponse.accessToken;
  }
}
