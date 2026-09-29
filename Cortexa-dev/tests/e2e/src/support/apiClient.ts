import { request, APIRequestContext } from '@playwright/test';

export interface LoginResponse {
  data: {
    access_token: string;
    refresh_token?: string;
  };
}

export interface CreateBatchResponse {
  batch_id: string;
}

export interface BatchStatus {
  batch_id: string;
  status: string;
  status_label?: string;
  engine?: string;
  created_at?: string;
  updated_at?: string;
}

export interface BatchResult {
  batch_id: string;
  candidates?: unknown[];
  opportunities?: unknown[];
}

export class ApiClient {
  private context: APIRequestContext | null = null;
  private token: string | null = null;

  constructor(private baseUrl: string) {}

  async init(): Promise<void> {
    this.context = await request.newContext({
      baseURL: this.baseUrl,
      timeout: 60000,
    });
  }

  async login(email: string, password: string): Promise<string> {
    if (!this.context) throw new Error('ApiClient not initialized');

    const response = await this.context.post('/auth/login', {
      data: { email, password },
    });

    if (!response.ok()) {
      throw new Error(`Login failed: ${response.status()} ${await response.text()}`);
    }

    const body: LoginResponse = await response.json();
    this.token = body.data.access_token;
    return this.token;
  }

  async createBatch(file: Buffer, filename: string): Promise<string> {
    if (!this.context || !this.token) throw new Error('Not authenticated');

    const response = await this.context.post('/api/batches', {
      headers: { Authorization: `Bearer ${this.token}` },
      multipart: {
        file: {
          name: filename,
          mimeType: 'application/pdf',
          buffer: file,
        },
        engine: 'harvesting',
      },
    });

    if (!response.ok()) {
      throw new Error(`Create batch failed: ${response.status()} ${await response.text()}`);
    }

    const body: CreateBatchResponse = await response.json();
    return body.batch_id;
  }

  async startBatch(batchId: string): Promise<void> {
    if (!this.context || !this.token) throw new Error('Not authenticated');

    const response = await this.context.post(`/api/batches/${batchId}/start`, {
      headers: { Authorization: `Bearer ${this.token}` },
    });

    if (!response.ok()) {
      throw new Error(`Start batch failed: ${response.status()} ${await response.text()}`);
    }
  }

  async getStatus(batchId: string): Promise<BatchStatus> {
    if (!this.context || !this.token) throw new Error('Not authenticated');

    const response = await this.context.get(`/api/batches/${batchId}`, {
      headers: { Authorization: `Bearer ${this.token}` },
    });

    if (!response.ok()) {
      throw new Error(`Get status failed: ${response.status()} ${await response.text()}`);
    }

    return response.json();
  }

  async getResults(batchId: string): Promise<BatchResult> {
    if (!this.context || !this.token) throw new Error('Not authenticated');

    const response = await this.context.get(`/api/batches/${batchId}/results`, {
      headers: { Authorization: `Bearer ${this.token}` },
    });

    if (!response.ok()) {
      throw new Error(`Get results failed: ${response.status()} ${await response.text()}`);
    }

    return response.json();
  }

  async listBatches(): Promise<BatchStatus[]> {
    if (!this.context || !this.token) throw new Error('Not authenticated');

    const response = await this.context.get('/api/batches', {
      headers: { Authorization: `Bearer ${this.token}` },
    });

    if (!response.ok()) {
      throw new Error(`List batches failed: ${response.status()} ${await response.text()}`);
    }

    const body = await response.json();
    return Array.isArray(body) ? body : body.data || [];
  }

  async dispose(): Promise<void> {
    await this.context?.dispose();
  }
}
