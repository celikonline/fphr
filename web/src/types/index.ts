/**
 * Global TypeScript types and interfaces for the web application
 */

export interface ApiResponse<T> {
  data: T;
  message?: string;
  errors?: string[];
}

export interface PaginatedResponse<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

/**
 * Mirrors the backend's `PagedResult<T>` (Sense.Shared.Contracts.Paging), as serialized by
 * System.Text.Json's camelCase policy: { items, page, pageSize, totalCount, totalPages, hasNext, hasPrevious }.
 * Used by services that call the real grid-standard list endpoints (?page&pageSize&sort&q&filters).
 */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNext: boolean;
  hasPrevious: boolean;
}

/** Mirrors the backend's `PagedQuery` grid-standard list request. */
export interface PagedQueryParams {
  page?: number;
  pageSize?: number;
  sort?: string;
  q?: string;
}

export interface ApiError {
  message: string;
  statusCode: number;
  errors?: Record<string, string[]>;
}

export type RequestStatus = "idle" | "loading" | "success" | "error";
