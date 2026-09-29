import { describe, it, expect, vi, beforeEach } from 'vitest';
import axios from 'axios';
import { rehydrateSession } from './bootstrapSession';
import * as tokenStore from './tokenStore';

vi.mock('axios');
vi.mock('./tokenStore');

const mockedAxios = vi.mocked(axios, true);
const mockedSetAccessToken = vi.mocked(tokenStore.setAccessToken);

const MOCK_ACCESS_TOKEN = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9';
const MOCK_EXPIRES_AT = '2026-12-31T23:59:59Z';

describe('rehydrateSession', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('calls setAccessToken and returns true on successful refresh', async () => {
    const mockResponse = {
      data: {
        success: true,
        data: {
          access_token: MOCK_ACCESS_TOKEN,
          expires_at: MOCK_EXPIRES_AT,
        },
        correlation_id: 'abc',
      },
    };
    mockedAxios.post.mockResolvedValue(mockResponse);

    const result = await rehydrateSession();

    expect(mockedAxios.post).toHaveBeenCalledWith(
      expect.stringContaining('/auth/refresh'),
      undefined,
      { withCredentials: true }
    );
    expect(mockedSetAccessToken).toHaveBeenCalledWith(MOCK_ACCESS_TOKEN, MOCK_EXPIRES_AT);
    expect(result).toBe(true);
  });

  it('returns false when response data is null', async () => {
    const mockResponse = {
      data: {
        success: true,
        data: null,
        correlation_id: 'abc',
      },
    };
    mockedAxios.post.mockResolvedValue(mockResponse);

    const result = await rehydrateSession();

    expect(mockedSetAccessToken).not.toHaveBeenCalled();
    expect(result).toBe(false);
  });

  it('returns false when access_token is missing', async () => {
    const mockResponse = {
      data: {
        success: true,
        data: {
          expires_at: MOCK_EXPIRES_AT,
        },
        correlation_id: 'abc',
      },
    };
    mockedAxios.post.mockResolvedValue(mockResponse);

    const result = await rehydrateSession();

    expect(mockedSetAccessToken).not.toHaveBeenCalled();
    expect(result).toBe(false);
  });

  it('returns false on network error', async () => {
    mockedAxios.post.mockRejectedValue(new Error('Network failure'));

    const result = await rehydrateSession();

    expect(mockedSetAccessToken).not.toHaveBeenCalled();
    expect(result).toBe(false);
  });

  it('returns false on 401 error', async () => {
    const error = {
      response: {
        status: 401,
        data: { success: false, error: 'Unauthorized' },
      },
    };
    mockedAxios.post.mockRejectedValue(error);

    const result = await rehydrateSession();

    expect(mockedSetAccessToken).not.toHaveBeenCalled();
    expect(result).toBe(false);
  });
});
