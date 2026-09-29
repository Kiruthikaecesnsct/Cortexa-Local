import axios from 'axios';
import type { ApiResponse } from '../api/envelope';
import { apiBaseUrl } from '../config/env';
import { setAccessToken } from './tokenStore';

interface RefreshResponseData {
  access_token: string;
  expires_at: string;
}

export async function rehydrateSession(): Promise<boolean> {
  try {
    const response = await axios.post<ApiResponse<RefreshResponseData>>(
      `${apiBaseUrl}/auth/refresh`,
      undefined,
      { withCredentials: true }
    );

    const data = response.data.data;
    if (!data?.access_token) {
      return false;
    }

    setAccessToken(data.access_token, data.expires_at);
    return true;
  } catch {
    return false;
  }
}
