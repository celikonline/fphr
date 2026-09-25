/** E3: external payslip PDF/zip bulk upload (`POST api/v1/documents/payslip-imports`, permission `payroll.payslip.import`). */
import { apiClient } from "@senseik/api-client";

const PAYSLIP_IMPORTS_BASE = "/v1/documents/payslip-imports";

/** Native C# enum, camelCase JsonStringEnumConverter. `terminated` = ayrılmış çalışan. */
export type PayslipImportFileStatus = "imported" | "skipped" | "unmatched" | "terminated" | "duplicate" | "failed";

export interface PayslipImportFileResult {
  fileName: string;
  status: PayslipImportFileStatus;
  /** Stable reason code (not_pdf, invalid_file_name, unknown_registration, ...); missing when imported. */
  reason?: string;
  employeeId?: string;
  /** `YYYY-MM`. */
  period?: string;
  documentId?: string;
}

export interface PayslipImportResult {
  total: number;
  imported: number;
  files: PayslipImportFileResult[];
}

/** Uploads PDFs and/or zips (`<sicil>_<YYYY-MM>.pdf`) as multipart `files`; unmatched files are reported, not errors. */
export async function importPayslipFiles(files: File[]): Promise<PayslipImportResult> {
  const form = new FormData();
  for (const file of files) form.append("files", file, file.name);
  const { data } = await apiClient.post<PayslipImportResult>(PAYSLIP_IMPORTS_BASE, form, {
    headers: { "Content-Type": "multipart/form-data" },
  });
  return data;
}
