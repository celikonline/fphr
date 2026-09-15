/**
 * Data Transfer Service (Veri aktarım merkezi, roadmap Faz 0 items #7, #8).
 *
 * Mirrors the `Sense.Modules.DataTransfer` REST API (`/v1/datatransfer`) described in the Faz 0 contract (P4):
 * import templates, template download, import jobs (upload → mapping → run → result) and per-row outcomes.
 * File endpoints are fetched as blobs; their error bodies are re-parsed so `getApiErrorMessage` keeps working.
 */
import axios from "axios";
import { apiClient } from "@senseik/api-client";
import type { PagedResult } from "@/types";

export type ImportColumnDataType = "Text" | "Date" | "Email" | "Phone" | "NationalId" | "Lookup";
export type ImportColumnRequirement = "Always" | "OnInsert" | "Optional";
export type ImportMode = "Upsert" | "UpdateOnly";
export type ImportFailurePolicy = "SkipInvalidRows" | "AllOrNothing";
export type ImportJobStatus = "Uploaded" | "Mapped" | "Completed" | "CompletedWithErrors" | "Failed";
export type ImportRowOutcome = "Inserted" | "Updated" | "Skipped" | "Error";

export const IMPORT_ROW_OUTCOMES: ImportRowOutcome[] = ["Inserted", "Updated", "Skipped", "Error"];
export const FINISHED_JOB_STATUSES: ImportJobStatus[] = ["Completed", "CompletedWithErrors", "Failed"];

export const IMPORT_MAX_FILE_BYTES = 10 * 1024 * 1024;
export const IMPORT_ALLOWED_EXTENSIONS = [".xlsx", ".csv"];

export const DataTransferPermissions = {
  View: "datatransfer.import.view",
  Run: "datatransfer.import.run",
  TemplateDownload: "datatransfer.template.download",
  ResultDownload: "datatransfer.result.download",
} as const;

export interface ImportColumn {
  key: string;
  header: string;
  dataType: ImportColumnDataType | string;
  requirement: ImportColumnRequirement | string;
  isMatchKey: boolean;
  options: string[] | null;
}

export interface ImportTemplate {
  code: string;
  name: string;
  description: string;
  supportedModes: string[];
  columns: ImportColumn[];
}

export interface SourceColumn {
  index: number;
  header: string;
  samples: string[];
  suggestedTargetKey: string | null;
  autoMatched: boolean;
}

export interface ColumnMapping {
  sourceIndex: number;
  /** null = "Yok say" */
  targetKey: string | null;
}

export interface ImportJob {
  id: string;
  templateCode: string;
  fileName: string;
  status: ImportJobStatus;
  mode: ImportMode;
  failurePolicy: ImportFailurePolicy;
  totalRows: number;
  insertedCount: number;
  updatedCount: number;
  skippedCount: number;
  errorCount: number;
  sourceColumns: SourceColumn[];
  mapping: ColumnMapping[];
  createdAt: string;
  finishedAt: string | null;
}

export interface ImportJobListItem {
  id: string;
  templateCode: string;
  fileName: string;
  status: ImportJobStatus;
  mode: ImportMode;
  totalRows: number;
  insertedCount: number;
  updatedCount: number;
  skippedCount: number;
  errorCount: number;
  createdAt: string;
  requestedByName: string | null;
}

export interface RowError {
  column: string | null;
  code: string;
  message: string;
}

export interface ImportJobRow {
  rowNumber: number;
  outcome: ImportRowOutcome;
  matchKey: string | null;
  errors: RowError[];
}

export interface SaveMappingInput {
  mapping: ColumnMapping[];
  mode: ImportMode;
  failurePolicy: ImportFailurePolicy;
}

export interface DownloadedFile {
  blob: Blob;
  fileName: string;
}

const BASE = "/v1/datatransfer";

/** True when the column must be mapped before the job can continue (required or match key). */
export function isBlockingColumn(column: ImportColumn): boolean {
  return column.isMatchKey || column.requirement === "Always";
}

export async function listImportTemplates(lang: string): Promise<ImportTemplate[]> {
  const { data } = await apiClient.get<ImportTemplate[]>(`${BASE}/templates`, { params: { lang } });
  return data;
}

export async function downloadImportTemplate(code: string, lang: string): Promise<DownloadedFile> {
  return downloadFile(`${BASE}/templates/${encodeURIComponent(code)}/download`, { lang }, `${code}.xlsx`);
}

export async function uploadImportFile(templateCode: string, file: File): Promise<ImportJob> {
  const form = new FormData();
  form.append("templateCode", templateCode);
  form.append("file", file);
  // The shared client defaults to JSON, which would make axios serialize the FormData.
  const { data } = await apiClient.post<ImportJob>(`${BASE}/import-jobs`, form, {
    headers: { "Content-Type": "multipart/form-data" },
  });
  return data;
}

export async function listImportJobs(query: { page?: number; pageSize?: number } = {}): Promise<PagedResult<ImportJobListItem>> {
  const { data } = await apiClient.get<PagedResult<ImportJobListItem>>(`${BASE}/import-jobs`, { params: query });
  return data;
}

export async function getImportJob(id: string): Promise<ImportJob> {
  const { data } = await apiClient.get<ImportJob>(`${BASE}/import-jobs/${id}`);
  return data;
}

export async function saveImportJobMapping(id: string, input: SaveMappingInput): Promise<ImportJob> {
  const { data } = await apiClient.put<ImportJob>(`${BASE}/import-jobs/${id}/mapping`, input);
  return data;
}

export async function runImportJob(id: string): Promise<ImportJob> {
  const { data } = await apiClient.post<ImportJob>(`${BASE}/import-jobs/${id}/run`);
  return data;
}

export async function listImportJobRows(
  id: string,
  query: { outcome?: ImportRowOutcome; page?: number; pageSize?: number } = {}
): Promise<PagedResult<ImportJobRow>> {
  const { data } = await apiClient.get<PagedResult<ImportJobRow>>(`${BASE}/import-jobs/${id}/rows`, { params: query });
  return data;
}

export async function downloadImportJobResult(id: string, fallbackName: string, lang: string): Promise<DownloadedFile> {
  return downloadFile(`${BASE}/import-jobs/${id}/result`, { lang }, fallbackName);
}

/** Saves a downloaded blob through a temporary object URL + anchor. */
export function saveDownloadedFile({ blob, fileName }: DownloadedFile): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}

async function downloadFile(url: string, params: Record<string, string>, fallbackName: string): Promise<DownloadedFile> {
  try {
    const response = await apiClient.get<Blob>(url, { params, responseType: "blob" });
    const disposition = response.headers["content-disposition"];
    return {
      blob: response.data,
      fileName: parseContentDispositionFileName(typeof disposition === "string" ? disposition : undefined) ?? fallbackName,
    };
  } catch (error) {
    throw await unwrapBlobError(error);
  }
}

export function parseContentDispositionFileName(header: string | undefined): string | undefined {
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
