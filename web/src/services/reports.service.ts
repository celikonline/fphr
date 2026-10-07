/**
 * Reporting module (roadmap #70): mirrors `ReportsController` (`/v1/reports`). Report executions
 * can finish synchronously or be queued for background generation; consumers poll their execution
 * and download the persisted output when it succeeds.
 *
 * The catalog mirrors Kolay İK's Raporlar menu: every report carries a menu category
 * (İdari / Çalışan / İzinler / Finans) and the filters it accepts (Birimler, Yıl, Ay, tarih aralığı,
 * anahtar) - the page draws the parameter form from `parameters`. Display name/description for each
 * key are looked up client-side in the "reports" i18n namespace (`reports:catalog.<key>.label`) - the
 * backend only ever sends the stable key.
 */
import axios from "axios";
import { apiClient } from "@senseik/api-client";
import type { PagedQueryParams, PagedResult } from "@/types";

const REPORTS_BASE = "/v1/reports";

export type ReportExecutionStatus = "queued" | "running" | "succeeded" | "failed";

/** `ReportCategories` (Reporting.Contracts); menu order is this array's order. */
export const REPORT_CATEGORY_KEYS = ["administrative", "employee", "leave", "finance", "time"] as const;
export type ReportCategoryKey = (typeof REPORT_CATEGORY_KEYS)[number];

/** `ReportParameterKind` (camelCase enum JSON). */
export type ReportParameterKind = "orgUnits" | "year" | "month" | "dateRange" | "boolean" | "select";

export interface ReportParameter {
  key: string;
  kind: ReportParameterKind;
  isRequired: boolean;
  /** Allowed values of a `select` parameter (omitted by the API for other kinds); labels: `reports:params.{key}Options.{value}`. */
  options?: string[] | null;
}

/** Wire keys shared with `ReportParameterKeys`; a `dateRange` parameter is sent as `from` + `to`. */
export const REPORT_PARAMETER_KEYS = {
  orgUnitIds: "orgUnitIds",
  year: "year",
  month: "month",
  from: "from",
  to: "to",
  includeTerminated: "includeTerminated",
  status: "status",
  format: "format",
} as const;

export interface ReportCatalogItem {
  key: string;
  categoryKey: string;
  parameters: ReportParameter[];
  /** Omitted by older APIs; default is synchronous generation. */
  executionMode?: "sync" | "async";
}

export interface ReportExecution {
  id: string;
  reportKey: string;
  status: ReportExecutionStatus;
  requestedAt: string;
  /** Omitted by the API (WhenWritingNull) until the execution finishes - treat missing as null. */
  completedAt?: string | null;
  resultFileName?: string | null;
  errorMessage?: string | null;
}

/** `ExportColumnType` (Shared.Contracts.Export), camelCase enum JSON. */
export type ReportColumnType = "text" | "number" | "date" | "dateTime" | "boolean";

export interface ReportColumn {
  header: string;
  type: ReportColumnType;
}

export interface ReportSheet {
  index: number;
  name: string;
}

/** Grid cell: text, number, boolean, ISO date ("yyyy-MM-dd") / date-time string, or null for a blank cell. */
export type ReportCell = string | number | boolean | null;

/** `ReportDataDto`: every sheet, the requested sheet's columns and one searched/sorted/paged slice of its rows. */
export interface ReportData {
  sheets: ReportSheet[];
  sheetIndex: number;
  columns: ReportColumn[];
  rows: PagedResult<ReportCell[]>;
}

export interface ReportDataParams {
  sheet?: number;
  page?: number;
  pageSize?: number;
  q?: string;
  /** Column index, `-` prefix for descending ("-2"). */
  sort?: string;
}

/** Problem `code` of `GET …/data` when the file cannot be shown as a grid (only downloaded). */
export const REPORT_PREVIEW_UNAVAILABLE_CODE = "report_execution.preview_not_available";

export interface DownloadedReportFile {
  blob: Blob;
  fileName: string;
}

/** Reports the caller may generate (every registered provider whose permission, if any, they hold), category order. */
export async function getReportCatalog(): Promise<ReportCatalogItem[]> {
  const { data } = await apiClient.get<ReportCatalogItem[]>(`${REPORTS_BASE}/catalog`);
  return data;
}

/**
 * Requests a report. `notify: false` skips the "report ready" e-mail.
 */
export async function requestReport(
  reportKey: string,
  parameters?: Record<string, string>,
  options?: { notify?: boolean },
): Promise<ReportExecution> {
  const { data } = await apiClient.post<ReportExecution>(`${REPORTS_BASE}/executions`, { reportKey, parameters, notify: options?.notify });
  return data;
}

/** The caller's own report execution history, newest first. */
export async function listReportExecutions(params: PagedQueryParams = {}): Promise<PagedResult<ReportExecution>> {
  const { data } = await apiClient.get<PagedResult<ReportExecution>>(`${REPORTS_BASE}/executions`, { params });
  return data;
}

/** One of the caller's own executions (used when arriving from the "report ready" e-mail link). */
export async function getReportExecution(id: string): Promise<ReportExecution> {
  const { data } = await apiClient.get<ReportExecution>(`${REPORTS_BASE}/executions/${id}`);
  return data;
}

/** One page of a succeeded execution's workbook as grid data. */
export async function getReportExecutionData(id: string, params: ReportDataParams = {}): Promise<ReportData> {
  const { data } = await apiClient.get<ReportData>(`${REPORTS_BASE}/executions/${id}/data`, { params });
  return data;
}

/** Downloads a succeeded execution's generated file or a PDF rendered from that same snapshot. */
export async function downloadReportExecution(
  id: string,
  fallbackName: string,
  format: "xlsx" | "pdf" = "xlsx",
): Promise<DownloadedReportFile> {
  try {
    const response = await apiClient.get<Blob>(`${REPORTS_BASE}/executions/${id}/download`, {
      ...(format === "pdf" ? { params: { format } } : {}),
      responseType: "blob",
    });
    const disposition = response.headers["content-disposition"];
    return {
      blob: response.data,
      fileName: parseContentDispositionFileName(typeof disposition === "string" ? disposition : undefined) ?? fallbackName,
    };
  } catch (error) {
    throw await unwrapBlobError(error);
  }
}

/** Saves a downloaded blob through a temporary object URL + anchor (mirrors data-transfer.service.ts). */
export function saveDownloadedReportFile({ blob, fileName }: DownloadedReportFile): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}

/** Groups catalog items by category in menu order; categories without a visible report are omitted. */
export function groupReportCatalog(items: ReportCatalogItem[]): { categoryKey: string; items: ReportCatalogItem[] }[] {
  const known = new Map<string, ReportCatalogItem[]>(REPORT_CATEGORY_KEYS.map((key) => [key, []]));
  const unknown = new Map<string, ReportCatalogItem[]>();
  for (const item of items) {
    const bucket = known.get(item.categoryKey) ?? unknown.get(item.categoryKey);
    if (bucket) {
      bucket.push(item);
    } else {
      unknown.set(item.categoryKey, [item]);
    }
  }
  return [...known.entries(), ...unknown.entries()]
    .filter(([, list]) => list.length > 0)
    .map(([categoryKey, list]) => ({ categoryKey, items: list }));
}

function parseContentDispositionFileName(header: string | undefined): string | undefined {
  if (!header) return undefined;
  const encoded = /filename\*\s*=\s*(?:UTF-8|utf-8)''([^;]+)/.exec(header);
  if (encoded) {
    try {
      return decodeURIComponent(encoded[1].trim().replace(/^"|"$/g, ""));
    } catch {
      // fall through to the plain filename parameter
    }
  }
  const plain = /filename\s*=\s*("([^"]*)"|[^;]+)/.exec(header);
  const value = plain ? (plain[2] ?? plain[1]).trim() : undefined;
  return value || undefined;
}

/** A blob request's ProblemDetails body arrives as a Blob; parse it back to JSON for error helpers. */
async function unwrapBlobError(error: unknown): Promise<unknown> {
  if (axios.isAxiosError(error) && error.response?.data instanceof Blob) {
    try {
      const text = await error.response.data.text();
      error.response.data = text ? JSON.parse(text) : undefined;
    } catch {
      error.response.data = undefined;
    }
  }
  return error;
}
