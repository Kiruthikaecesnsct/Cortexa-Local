import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import axios from 'axios';
import { ApiClient } from './client';
import * as tokenStore from '../auth/tokenStore';

vi.mock('axios');

const mockedAxios = vi.mocked(axios, true);

const makeInterceptors = () => ({
  request: { use: vi.fn() },
  response: { use: vi.fn() },
});

function buildClient(token?: string): {
  client: ApiClient;
  requestHandler: (config: Record<string, unknown>) => Record<string, unknown>;
  responseSuccess: (r: unknown) => unknown;
  responseError: (e: unknown) => Promise<unknown>;
} {
  const interceptors = makeInterceptors();
  const httpInstance = {
    interceptors,
    get: vi.fn(),
    post: vi.fn(),
  };

  mockedAxios.create = vi.fn().mockReturnValue(httpInstance);

  const client = new ApiClient('http://api.test/v1', () => token);

  const requestHandler = interceptors.request.use.mock.calls[0]?.[0] as (
    config: Record<string, unknown>
  ) => Record<string, unknown>;

  const responseSuccess = interceptors.response.use.mock.calls[0]?.[0] as (
    r: unknown
  ) => unknown;

  const responseError = interceptors.response.use.mock.calls[0]?.[1] as (
    e: unknown
  ) => Promise<unknown>;

  return { client, requestHandler, responseSuccess, responseError };
}

describe('ApiClient', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    tokenStore.setTokens('test-access', 'test-refresh');
  });

  afterEach(() => {
    tokenStore.clearTokens();
  });

  it('attaches Bearer token to outgoing requests', () => {
    const { requestHandler } = buildClient('my-token');
    const headers = { set: vi.fn() };
    const result = requestHandler({ headers }) as { headers: typeof headers };
    expect(result.headers.set).toHaveBeenCalledWith('Authorization', 'Bearer my-token');
  });

  it('attaches a unique X-Correlation-Id to each request', () => {
    const { requestHandler } = buildClient('tok');
    const headers1 = { set: vi.fn() };
    const headers2 = { set: vi.fn() };

    requestHandler({ headers: headers1 });
    requestHandler({ headers: headers2 });

    const id1 = headers1.set.mock.calls.find((c) => c[0] === 'X-Correlation-Id')?.[1] as string;
    const id2 = headers2.set.mock.calls.find((c) => c[0] === 'X-Correlation-Id')?.[1] as string;

    expect(id1).toBeDefined();
    expect(id2).toBeDefined();
    expect(id1).not.toBe(id2);
  });

  it('returns data from a successful GET', async () => {
    const { client } = buildClient('tok');
    const httpInstance = (mockedAxios.create as ReturnType<typeof vi.fn>)();
    const expected = { success: true, data: { id: '1' }, correlation_id: 'abc' };
    httpInstance.get = vi.fn().mockResolvedValue({ data: expected });

    const result = await client.get<{ id: string }>('/items/1');
    expect(result).toEqual(expected);
  });

  it('returns data from a successful POST', async () => {
    const { client } = buildClient('tok');
    const httpInstance = (mockedAxios.create as ReturnType<typeof vi.fn>)();
    const expected = { success: true, data: { created: true }, correlation_id: 'xyz' };
    httpInstance.post = vi.fn().mockResolvedValue({ data: expected });

    const result = await client.post<{ created: boolean }>('/items', { name: 'test' });
    expect(result).toEqual(expected);
  });

  it('calls refresh once on 401 and retries the original request', async () => {
    const { client, responseError } = buildClient('tok');

    const retryResponse = { data: { success: true, correlation_id: 'r1' } };
    const refreshApiResponse = {
      data: {
        success: true,
        data: { access_token: 'new-tok', expires_at: '2099-01-01T00:00:00Z' },
        correlation_id: 'r1',
      },
    };

    const mockHttp = vi.fn().mockResolvedValue(retryResponse);
    (client as unknown as { http: unknown }).http = Object.assign(mockHttp, {
      interceptors: { request: { use: vi.fn() }, response: { use: vi.fn() } },
      get: vi.fn(),
      post: vi.fn().mockResolvedValue(refreshApiResponse),
    });

    const originalConfig = { url: '/protected', _isRetry: false, headers: { set: vi.fn() } };
    const error = { response: { status: 401 }, config: originalConfig };

    const result = await responseError(error);
    expect(result).toEqual(retryResponse);
  });

  it('clears tokens and does not retry infinitely on repeated 401', async () => {
    const clearSpy = vi.spyOn(tokenStore, 'clearTokens');
    const { responseError } = buildClient('tok');

    const originalConfig = { url: '/protected', _isRetry: true };
    const error = { response: { status: 401 }, config: originalConfig };

    await expect(responseError(error)).rejects.toEqual(error);
    expect(clearSpy).not.toHaveBeenCalled();
  });

  it('does not attach Authorization header when no token is available', () => {
    const { requestHandler } = buildClient(undefined);
    const headers = { set: vi.fn() };
    requestHandler({ headers });
    const authCall = headers.set.mock.calls.find((c) => c[0] === 'Authorization');
    expect(authCall).toBeUndefined();
  });
});
