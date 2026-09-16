/**
 * Reporting module (roadmap #70): mirrors `ReportsController` (`/v1/reports`). R0: generation is
 * synchronous - `requestReport` returns the finished (Succeeded/Failed) execution in the same
 * response, no polling loop needed today - but the Queued/Running/Succeeded/Failed shape is kept
 * as-is so a later move onto a background worker doesn't change this service's surface.
 * Display name/description for each catalog key are looked up client-side in the "reports" i18n
 * namespace (`reports:catalog.<key>.label`) - the backend only ever sends the stable key.
 */
import axios from "axios";
import { apiClient } from "@senseik/api-client";
import type { PagedQueryParams, PagedResult } from "@/types";

const REPORTS_BASE = "/v1/reports";

export type ReportExecutionStatus = "queued" | "running" | "succeeded" | "failed";

export interface ReportCatalogItem {
  key: string;
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

export interface DownloadedReportFile {
  blob: Blob;
  fileName: string;
}

/** Reports the caller may generate (every registered provider whose permission, if any, they hold). */
export async function getReportCatalog(): Promise<ReportCatalogItem[]> {
  const { data } = await apiClient.get<ReportCatalogItem[]>(`${REPORTS_BASE}/catalog`);
  return data;
}

/** Requests a report; the response's status already reflects the (synchronous, R0) outcome. */
export async function requestReport(reportKey: string, parameters?: Record<string, string>): Promise<ReportExecution> {
  const { data } = await apiClient.post<ReportExecution>(`${REPORTS_BASE}/executions`, { reportKey, parameters });
  return data;
}

/** The caller's own report execution history, newest first. */
export async function listReportExecutions(params: PagedQueryParams = {}): Promise<PagedResult<ReportExecution>> {
  const { data } = await apiClient.get<PagedResult<ReportExecution>>(`${REPORTS_BASE}/executions`, { params });
  return data;
}

/** Downloads a succeeded execution's generated file (server file name via Content-Disposition, else fallbackName). */
export async function downloadReportExecution(id: string, fallbackName: string): Promise<DownloadedReportFile> {
  try {
    const response = await apiClient.get<Blob>(`${REPORTS_BASE}/executions/${id}/download`, { responseType: "blob" });
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
