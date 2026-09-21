/**
 * Payroll module (Faz 7 tur 3-4, read-only web slice): mirrors `PayrollPeriodsController.cs`
 * (`api/v1/payroll/periods`). Period open/run/recalculate-dirty stay backend-only for this slice - the web pages
 * only list periods/payslip lines and their status; see `pages/payroll/periods.tsx` and `period-detail.tsx`.
 *
 * Contract-first note (Faz 7 tur 1, `payroll` sözleşmesi): `GET /periods` (tur 3) and `GET /periods/{id}`
 * (tur 1, this file) aren't in the backend's TALIMAT task list yet - only the `/export` xlsx endpoint (BE madde 4)
 * is. Shapes below are our best guess from the `Payslip` domain entity (`GrossAmount`/`NetAmount`, no stored
 * SGK/tax/stamp-duty breakdown - those only exist inside the calculation trace's `StepsJson`, which is why the
 * on-screen table only shows gross/net while the Excel export can show the full breakdown) and
 * `IEmployeeDirectory.GetContactsAsync` (First/LastName, no employee number) - see HAFIZA-fe.md tur 1.
 */
import axios from "axios";
import { apiClient } from "@senseik/api-client";

const PAYROLL_BASE = "/v1/payroll";

export type PayrollPeriodStatus = "open" | "approved" | "paid" | "locked";

export interface PayrollPeriod {
  id: string;
  year: number;
  month: number;
  status: PayrollPeriodStatus;
}

/**
 * One employee's payslip line within a period (gross/net only - see the contract-first note above).
 * `workedDays`/`calendarDaysInMonth` (Faz 7 tur 2 dinamik ek, BE md.6): read from the payslip's persisted
 * calculation trace (`PayslipCalculationTrace`) - undefined/missing when no trace exists yet, never `0`.
 */
export interface PayslipLine {
  employeeId: string;
  employeeName: string;
  grossAmount: number;
  netAmount: number;
  workedDays?: number | null;
  calendarDaysInMonth?: number | null;
}

export interface PayrollPeriodDetail extends PayrollPeriod {
  payslips: PayslipLine[];
}

export interface DownloadedPayrollFile {
  blob: Blob;
  fileName: string;
}

/**
 * One SGK teşvik kanunu kodu (5510, 6111, 7103, 27103 vb.) active for at least one employee, with how many employees
 * carry it (senseik-010/F-03: `IncentivesController.GetSummary`). Read-only - used by the Organizasyon/Şirket profil
 * ekranı's "Aktif teşvik türleri" summary; the writable value stays on the employee's Bordro parametreleri.
 */
export interface IncentiveSummaryItem {
  code: string;
  employeeCount: number;
}

/** Tenant-wide "aktif teşvik türleri" summary as of today (`payroll.profile.manage`). */
export async function getActiveIncentiveSummary(): Promise<IncentiveSummaryItem[]> {
  const { data } = await apiClient.get<IncentiveSummaryItem[]>(`${PAYROLL_BASE}/incentives/summary`);
  return data;
}

/** Tenant-wide payroll period list (`payroll.period.recalculate`, the module's only gate today - no separate view key yet). */
export async function listPayrollPeriods(): Promise<PayrollPeriod[]> {
  const { data } = await apiClient.get<PayrollPeriod[]>(`${PAYROLL_BASE}/periods`);
  return data;
}

/** One period's header + its payslip lines (dönem detayı, salt okunur). 404 on another tenant's/unknown period. Optional costCenterId filter reduces payslips to matching cost center only (DTO shape unchanged). */
export async function getPayrollPeriod(id: string, costCenterId?: string): Promise<PayrollPeriodDetail> {
  const params = costCenterId ? { costCenterId } : undefined;
  const { data } = await apiClient.get<PayrollPeriodDetail>(`${PAYROLL_BASE}/periods/${id}`, { params });
  return data;
}

/** Downloads the period's payroll summary workbook (BE madde 4, `IReportExporter`); server file name via Content-Disposition, else fallbackName. */
export async function downloadPayrollPeriodExport(id: string, fallbackName: string): Promise<DownloadedPayrollFile> {
  try {
    const response = await apiClient.get<Blob>(`${PAYROLL_BASE}/periods/${id}/export`, { responseType: "blob" });
    const disposition = response.headers["content-disposition"];
    return {
      blob: response.data,
      fileName: parseContentDispositionFileName(typeof disposition === "string" ? disposition : undefined) ?? fallbackName,
    };
  } catch (error) {
    throw await unwrapBlobError(error);
  }
}

/** Saves a downloaded blob through a temporary object URL + anchor (mirrors reports.service.ts/data-transfer.service.ts). */
export function saveDownloadedPayrollFile({ blob, fileName }: DownloadedPayrollFile): void {
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

/** `EmployeePayslipDto`: one payslip of an employee with its period (employee card "Bordro" tab). */
export interface EmployeePayslip {
  payslipId: string;
  periodId: string;
  year: number;
  month: number;
  periodStatus: PayrollPeriodStatus;
  grossAmount: number;
  netAmount: number;
  employerCost: number;
}

/** Every payslip of the employee, newest period first (`payroll.trace.view`). */
export async function listEmployeePayslips(employeeId: string): Promise<EmployeePayslip[]> {
  const { data } = await apiClient.get<EmployeePayslip[]>(`${PAYROLL_BASE}/employees/${employeeId}/payslips`);
  return data;
}

/** Downloads one payslip as the plain-text ücret pusulası (İş K. m.37); server file name via Content-Disposition, else fallbackName. */
export async function downloadPayslipDocument(payslipId: string, fallbackName: string): Promise<DownloadedPayrollFile> {
  try {
    const response = await apiClient.get<Blob>(`${PAYROLL_BASE}/payslips/${payslipId}/document`, { responseType: "blob" });
    const disposition = response.headers["content-disposition"];
    return {
      blob: response.data,
      fileName: parseContentDispositionFileName(typeof disposition === "string" ? disposition : undefined) ?? fallbackName,
    };
  } catch (error) {
    throw await unwrapBlobError(error);
  }
}

/** `EmployeePayrollProfileDto` (camelCase enum JSON); the dated payroll settings the monthly run reads. */
export interface EmployeePayrollProfile {
  id: string;
  employeeId: string;
  validFrom: string;
  validTo?: string | null;
  wageBasis: "monthly" | "daily" | "weekly" | "hourly" | "yearly";
  sgkStatus: "normal" | "retired" | "intern" | "apprentice" | "foreign";
  incentiveLawCode?: string | null;
  mealBenefitVariant: "none" | "cash" | "inKind" | "mealCardOnly" | "mealAndMarketCard";
  mealDailyAmount: number;
  transportVariant: "none" | "cash" | "card" | "shuttle";
  paymentMethod: "bank" | "cash";
  sickDaysReducePay: boolean;
  externalPayrollCode?: string | null;
  notes?: string | null;
}

/** The profile version in effect today (`payroll.profile.manage`), or null when the employee has none (404). */
export async function getEmployeePayrollProfile(employeeId: string): Promise<EmployeePayrollProfile | null> {
  const { data } = await apiClient.get<EmployeePayrollProfile>(`${PAYROLL_BASE}/employees/${employeeId}/profile`, {
    validateStatus: (status) => status === 200 || status === 404,
  });
  return data && "wageBasis" in data ? data : null;
}

/** `GarnishmentOrderDto`: an icra/nafaka deduction order. */
export interface GarnishmentOrder {
  id: string;
  employeeId: string;
  type: "icra" | "nafaka";
  caseReference: string;
  monthlyInstallmentAmount: number;
  remainingBalance: number;
  startDate: string;
  closedDate?: string | null;
  isClosed: boolean;
}

/** The employee's garnishment orders including closed ones, newest first (`payroll.trace.view`). */
export async function listGarnishmentOrders(employeeId: string): Promise<GarnishmentOrder[]> {
  const { data } = await apiClient.get<GarnishmentOrder[]>(`${PAYROLL_BASE}/employees/${employeeId}/garnishment-orders`);
  return data;
}

/** Approves the period (Open → Approved); the backend refuses while changed payslips are not recalculated (`payroll.period.approve`). */
export async function approvePayrollPeriod(id: string): Promise<void> {
  await apiClient.post(`${PAYROLL_BASE}/periods/${id}/approve`);
}

/** Marks the approved period as paid (Approved → Paid) (`payroll.period.approve`). */
export async function markPayrollPeriodPaid(id: string): Promise<void> {
  await apiClient.post(`${PAYROLL_BASE}/periods/${id}/mark-paid`);
}

/** Locks the paid period (Paid → Locked) (`payroll.period.lock`). */
export async function lockPayrollPeriod(id: string): Promise<void> {
  await apiClient.post(`${PAYROLL_BASE}/periods/${id}/lock`);
}

/** Reopens an approved, paid or locked period; the written reason is audited (`payroll.period.reopen`). */
export async function reopenPayrollPeriod(id: string, reason: string): Promise<void> {
  await apiClient.post(`${PAYROLL_BASE}/periods/${id}/reopen`, { reason });
}

export type ClosingStage = "preparation" | "calculation" | "legal" | "archive";

/** `ClosingCheckDto`: one item of a period's closing checklist. */
export interface ClosingCheck {
  code: string;
  stage: ClosingStage;
  kind: "automatic" | "manual";
  severity: "blocker" | "warning";
  status: "pending" | "passed" | "failed" | "waived";
  detail?: string | null;
  waiverReason?: string | null;
  decidedAt?: string | null;
  isSatisfied: boolean;
}

/** The period's closing checklist (`payroll.trace.view`); a never-opened checklist comes back as the template, all pending. */
export async function getClosingChecklist(periodId: string): Promise<ClosingCheck[]> {
  const { data } = await apiClient.get<ClosingCheck[]>(`${PAYROLL_BASE}/periods/${periodId}/closing-checks`);
  return data;
}

/** Opens the checklist if needed and re-runs its automatic rules (`payroll.period.approve`). */
export async function runClosingChecks(periodId: string): Promise<ClosingCheck[]> {
  const { data } = await apiClient.post<ClosingCheck[]>(`${PAYROLL_BASE}/periods/${periodId}/closing-checks/run`);
  return data;
}

/** Ticks a manual item off (`payroll.period.approve`). */
export async function completeClosingCheck(periodId: string, code: string, note?: string): Promise<void> {
  await apiClient.post(`${PAYROLL_BASE}/periods/${periodId}/closing-checks/${code}/complete`, { note: note ?? null });
}

/** Waives an item with a written reason (`payroll.period.approve`). */
export async function waiveClosingCheck(periodId: string, code: string, reason: string): Promise<void> {
  await apiClient.post(`${PAYROLL_BASE}/periods/${periodId}/closing-checks/${code}/waive`, { reason });
}
