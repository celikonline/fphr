import axios from "axios";

/** Shape of the backend's ProblemDetails responses (localized `detail`, machine `code`). */
export interface ApiProblem {
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
  errors?: Record<string, string[]>;
  args?: Record<string, unknown>;
}

export function getApiErrorStatus(error: unknown): number | undefined {
  return axios.isAxiosError(error) ? error.response?.status : undefined;
}

export function getApiProblem(error: unknown): ApiProblem | undefined {
  if (!axios.isAxiosError(error)) return undefined;
  const data = error.response?.data;
  return data && typeof data === "object" ? (data as ApiProblem) : undefined;
}

/** Localized server message when present, otherwise the given fallback. */
export function getApiErrorMessage(error: unknown, fallback: string): string {
  return getApiProblem(error)?.detail || fallback;
}
