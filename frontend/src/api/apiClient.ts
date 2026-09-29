export class ApiError extends Error {
  public status: number;
  public data: any;

  constructor(status: number, message: string, data?: any) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.data = data;
  }
}

export interface RequestOptions extends Omit<RequestInit, 'body'> {
  params?: Record<string, string | number | boolean | undefined>;
  body?: any;
}

export const API_BASE_URL = '/api';

export async function apiClient<T>(endpoint: string, options: RequestOptions = {}): Promise<T> {
  const { params, body, headers, ...customConfig } = options;

  let queryString = '';
  if (params) {
    const searchParams = new URLSearchParams();
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null) {
        searchParams.append(key, String(value));
      }
    });
    const paramStr = searchParams.toString();
    if (paramStr) {
      queryString = `?${paramStr}`;
    }
  }

  const url = `${API_BASE_URL}${endpoint.startsWith('/') ? endpoint : `/${endpoint}`}${queryString}`;

  const config: RequestInit = {
    method: body ? 'POST' : 'GET',
    credentials: 'include', // Crucial for ASP.NET Core cookie session auth
    headers: {
      'Content-Type': 'application/json',
      ...headers,
    },
    ...customConfig,
  };

  if (body) {
    config.body = typeof body === 'string' ? body : JSON.stringify(body);
  }

  try {
    const response = await fetch(url, config);

    if (!response.ok) {
      let errorMessage = `HTTP error ${response.status}: ${response.statusText}`;
      let errorData: any = null;

      try {
        errorData = await response.json();
        if (errorData?.message) {
          errorMessage = errorData.message;
        }
      } catch {
        // Response was not JSON
      }

      if (response.status === 401) {
        // Emit custom event for global handling if needed
        window.dispatchEvent(new CustomEvent('kestrel:unauthorized'));
      }

      throw new ApiError(response.status, errorMessage, errorData);
    }

    // Handle 204 No Content
    if (response.status === 204) {
      return {} as T;
    }

    return (await response.json()) as T;
  } catch (error) {
    if (error instanceof ApiError) {
      throw error;
    }
    throw new ApiError(500, error instanceof Error ? error.message : 'Network request failed');
  }
}

export const api = {
  get: <T>(endpoint: string, options?: RequestOptions) =>
    apiClient<T>(endpoint, { ...options, method: 'GET' }),

  post: <T>(endpoint: string, body?: any, options?: RequestOptions) =>
    apiClient<T>(endpoint, { ...options, method: 'POST', body }),

  put: <T>(endpoint: string, body?: any, options?: RequestOptions) =>
    apiClient<T>(endpoint, { ...options, method: 'PUT', body }),

  delete: <T>(endpoint: string, options?: RequestOptions) =>
    apiClient<T>(endpoint, { ...options, method: 'DELETE' }),

  /**
   * POST that returns a binary blob (e.g., file download).
   */
  blob: async (endpoint: string, body?: any): Promise<{ blob: Blob; fileName: string }> => {
    const url = `${API_BASE_URL}${endpoint.startsWith('/') ? endpoint : `/${endpoint}`}`;
    const config: RequestInit = {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: typeof body === 'string' ? body : JSON.stringify(body),
    };
    try {
      const response = await fetch(url, config);
      if (!response.ok) {
        throw new ApiError(response.status, `Export error: ${response.status}`);
      }
      const blob = await response.blob();
      const disposition = response.headers.get('content-disposition');
      const fileName = disposition?.includes('filename=')
        ? disposition.split('filename=')[1].replace(/"/g, '')
        : 'export';
      return { blob, fileName };
    } catch (error) {
      if (error instanceof ApiError) throw error;
      throw new ApiError(500, error instanceof Error ? error.message : 'Export failed');
    }
  },
};
