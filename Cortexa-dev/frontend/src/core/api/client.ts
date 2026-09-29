import axios, { AxiosInstance, AxiosRequestConfig, InternalAxiosRequestConfig } from 'axios';
import type { ApiResponse } from './envelope';
import { clearTokens, setAccessToken } from '../auth/tokenStore';
import { batchDeleteTimeoutMs } from '../config/env';

interface RetryableConfig extends InternalAxiosRequestConfig {
  _isRetry?: boolean;
}

export class ApiClient {
  private readonly http: AxiosInstance;
  private readonly tokenProvider: () => string | undefined;

  constructor(baseUrl: string, tokenProvider: () => string | undefined) {
    this.tokenProvider = tokenProvider;
    this.http = axios.create({ baseURL: baseUrl, withCredentials: true });
    this.attachRequestInterceptor();
    this.attachResponseInterceptor();
  }

  private attachRequestInterceptor(): void {
    this.http.interceptors.request.use((config: InternalAxiosRequestConfig) => {
      const token = this.tokenProvider();
      if (token) {
        config.headers.set('Authorization', `Bearer ${token}`);
      }
      config.headers.set('X-Correlation-Id', crypto.randomUUID());
      return config;
    });
  }

  private attachResponseInterceptor(): void {
    this.http.interceptors.response.use(
      (response) => response,
      async (error: unknown) => {
        const axiosError = error as { response?: { status: number }; config?: RetryableConfig };
        const config = axiosError.config;

        if (axiosError.response?.status === 401 && config && !config._isRetry) {
          config._isRetry = true;
          try {
            const refreshResponse = await this.http.post<ApiResponse<{ access_token: string; expires_at: string }>>(
              '/auth/refresh', undefined, { _isRetry: true } as RetryableConfig
            );
            const { data } = refreshResponse.data;
            if (!data?.access_token) {
              clearTokens();
              window.location.href = '/login';
              return new Promise(() => {});
            }
            setAccessToken(data.access_token, data.expires_at);
            return this.http(config as AxiosRequestConfig);
          } catch {
            clearTokens();
            window.location.href = '/login';
            // Swallow — navigation is in progress; caller must not receive this.
            return new Promise(() => {});
          }
        }

        return Promise.reject(error);
      }
    );
  }

  async get<T>(path: string): Promise<ApiResponse<T>> {
    const response = await this.http.get<ApiResponse<T>>(path);
    return response.data;
  }

  // For endpoints that return a bare body instead of the standard ApiResponse
  // envelope (e.g. model-router /models, which internal services also read raw).
  async getRaw<T>(path: string): Promise<T> {
    const response = await this.http.get<T>(path);
    return response.data;
  }

  // For binary payloads (e.g. the secure PDF-stream document endpoint) that
  // return a raw content-type body instead of the JSON ApiResponse envelope.
  async getBlob(path: string, signal?: AbortSignal): Promise<Blob> {
    const response = await this.http.get<ArrayBuffer>(path, { responseType: 'arraybuffer', signal });
    const contentType = response.headers['content-type'];
    return new Blob([response.data], { type: typeof contentType === 'string' ? contentType : 'application/octet-stream' });
  }

  async post<T>(path: string, body: unknown): Promise<ApiResponse<T>> {
    const response = await this.http.post<ApiResponse<T>>(path, body);
    return response.data;
  }

  async postForm<T>(path: string, form: FormData): Promise<ApiResponse<T>> {
    const response = await this.http.post<ApiResponse<T>>(path, form);
    return response.data;
  }

  async delete<T>(path: string): Promise<ApiResponse<T>> {
    // Batch delete cascades through Service Bus drain (60s budget) + per-store purges
    // + Cosmos saga cleanup. Allow generous timeout to cover bounded backend cascade.
    const response = await this.http.delete<ApiResponse<T>>(path, {
      timeout: batchDeleteTimeoutMs,
    });
    return response.data;
  }

  async patch<T>(path: string, body: unknown): Promise<ApiResponse<T>> {
    const response = await this.http.patch<ApiResponse<T>>(path, body);
    return response.data;
  }

  async put<T>(path: string, body: unknown): Promise<ApiResponse<T>> {
    const response = await this.http.put<ApiResponse<T>>(path, body);
    return response.data;
  }
}
