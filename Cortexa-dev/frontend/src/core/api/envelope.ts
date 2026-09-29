export interface ApiResponse<T> {
  success: boolean;
  data?: T;
  error_code?: string;
  message?: string;
  correlation_id: string;
  details?: Record<string, unknown>;
}
