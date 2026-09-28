import axios from "axios";

/**
 * Shape of the backend's ProblemDetails responses (RFC 9457): `detail` is already localized in the
 * server's negotiated culture (see `GlobalExceptionHandler`), `code` is the stable machine code
 * (e.g. "leave.errors.invalid_range"). Mirrors `web/src/lib/api-error.ts`.
 */
export interface ApiProblem {
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
  errors?: Record<string, string[]>;
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
