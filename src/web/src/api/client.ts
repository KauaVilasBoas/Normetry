// The only way the app reaches the backend: the core API, on this origin under /api (the dev
// server forwards it). Never the AI service directly; chat goes through the core's /ai/* gateway.
export const API_BASE_URL = "/api";

export class ApiError extends Error {
  readonly status: number;

  constructor(status: number) {
    super(`The API answered ${status}.`);
    this.status = status;
  }
}

export interface ApiClientOptions {
  getAccessToken: () => Promise<string | undefined>;
  /** Refreshes the session once; resolves to the new access token, or null when that failed. */
  renewSession: () => Promise<string | null>;
  /** Called when the API still answers 401 after one renewal: the session-expired flow. */
  onSessionExpired: () => void;
  baseUrl?: string;
}

export interface RequestOptions {
  method?: "GET" | "POST" | "DELETE";
  /** Sent as JSON. */
  json?: unknown;
  accept?: string;
  signal?: AbortSignal;
}

export type ApiClient = ReturnType<typeof createApiClient>;

export function createApiClient({
  getAccessToken,
  renewSession,
  onSessionExpired,
  baseUrl = API_BASE_URL,
}: ApiClientOptions) {
  const send = (
    path: string,
    token: string | undefined,
    { method = "GET", json, accept = "application/json", signal }: RequestOptions,
  ) =>
    fetch(`${baseUrl}${path}`, {
      method,
      signal,
      body: json === undefined ? undefined : JSON.stringify(json),
      headers: {
        Accept: accept,
        ...(json === undefined ? {} : { "Content-Type": "application/json" }),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
    });

  /** Any status but 401 comes back as the response; the caller reads it (or its stream). */
  async function request(path: string, options: RequestOptions = {}): Promise<Response> {
    let response = await send(path, await getAccessToken(), options);
    if (response.status === 401) {
      const token = await renewSession();
      if (token) response = await send(path, token, options);
      if (!token || response.status === 401) {
        onSessionExpired();
        throw new ApiError(401);
      }
    }
    return response;
  }

  return {
    request,
    async get<T>(path: string): Promise<T> {
      const response = await request(path);
      if (!response.ok) throw new ApiError(response.status);
      return (await response.json()) as T;
    },
  };
}
