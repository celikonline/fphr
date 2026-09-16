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

/**
 * Mirrors `EmployeeFilter` in Sense.Modules.Employee.Contracts (Faz 0 contract P1).
 * Used by "Tümünü seç" bulk operations together with {@link EmployeeSelection}.
 */
export interface EmployeeFilter {
  q?: string | null;
  orgUnitId?: string | null;
  includeSubUnits?: boolean;
  status?: string | null;
  hasUserAccount?: boolean | null;
  managerEmployeeId?: string | null;
  employmentType?: string | null;
  contractType?: string | null;
  /** "Eksik bilgi": true lists only incomplete records, false only complete ones. */
  missingInfo?: boolean | null;
}

/**
 * Mirrors `EmployeeSelection`: either explicit `employeeIds`, or `filter` (with `employeeIds` null) meaning
 * every employee matching the filter within the caller's data scope, minus `excludedEmployeeIds`.
 */
export interface EmployeeSelection {
  employeeIds: string[] | null;
  filter: EmployeeFilter | null;
  excludedEmployeeIds: string[] | null;
}

// Enum values are serialized camelCase (JsonStringEnumConverter(JsonNamingPolicy.CamelCase)).
export type BulkItemOutcome = "succeeded" | "skipped" | "failed";

export interface BulkItemResult {
  employeeId: string;
  outcome: BulkItemOutcome;
  code: string | null;
}

/** Mirrors `BulkOperationResult` in Sense.Shared.Contracts.Bulk. */
export interface BulkOperationResult {
  requested: number;
  succeeded: number;
  skipped: number;
  failed: number;
  items: BulkItemResult[];
}

/** Mirrors `AccessSettingsDto` (Identity): tenant-wide visibility switches for the Employee access type. */
export interface AccessSettings {
  showOrgInfo: boolean;
  showUpcomingLeaves: boolean;
  showUpcomingBirthdays: boolean;
  showEventCalendar: boolean;
  showFiles: boolean;
  showAssets: boolean;
  showWorkPhoneNumbers: boolean;
  showOrgChart: boolean;
  showCustomFields: boolean;
  showCalendarMenu: boolean;
  showAllLeavesInCalendar: boolean;
}

export type AccessSettingKey = keyof AccessSettings;

/** `features` block of `GET /v1/identity/me`: access values are already EFFECTIVE for the caller. */
export interface MeFeatures {
  access: AccessSettings;
  accessSettingsBypass: boolean;
}
