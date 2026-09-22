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

/** `ComparisonLineDto`: one employee's gross/net against the previous month. */
export interface ComparisonLine {
  employeeId: string;
  employeeName: string;
  kind: "both" | "new" | "left";
  currentGross?: number | null;
  previousGross?: number | null;
  currentNet?: number | null;
  previousNet?: number | null;
  grossChangePercent?: number | null;
  netChangePercent?: number | null;
  exceedsThreshold: boolean;
}

export interface ComparisonTotals {
  headcount: number;
  gross: number;
  net: number;
  employerCost: number;
}

/** `PeriodComparisonDto`: the period against the previous calendar month; rows past the threshold come first. */
export interface PeriodComparisonResult {
  periodId: string;
  previousPeriodId?: string | null;
  previousYear: number;
  previousMonth: number;
  thresholdPercent: number;
  current: ComparisonTotals;
  previous: ComparisonTotals;
  exceedingCount: number;
  lines: ComparisonLine[];
}

/** Month-over-month comparison of a period (`payroll.trace.view`); the deviation threshold defaults to 5%. */
export async function getPeriodComparison(periodId: string, thresholdPercent?: number): Promise<PeriodComparisonResult> {
  const { data } = await apiClient.get<PeriodComparisonResult>(`${PAYROLL_BASE}/periods/${periodId}/comparison`, {
    params: thresholdPercent === undefined ? undefined : { thresholdPercent },
  });
  return data;
}

/** Downloads the bank payment list (generic CSV) of an approved, paid or locked period; it contains IBANs (`payroll.bankfile.export`). */
export async function downloadPayrollBankFile(id: string, fallbackName: string): Promise<DownloadedPayrollFile> {
  try {
    const response = await apiClient.get<Blob>(`${PAYROLL_BASE}/periods/${id}/bank-file`, { responseType: "blob" });
    const disposition = response.headers["content-disposition"];
    return {
      blob: response.data,
      fileName: parseContentDispositionFileName(typeof disposition === "string" ? disposition : undefined) ?? fallbackName,
    };
  } catch (error) {
    throw await unwrapBlobError(error);
  }
}

/** One employee named in a `BankReceiptMatchSummary` bucket. */
export interface BankReceiptSummaryLine {
  employeeId: string;
  employeeName: string;
  amount: number;
}

/** An employee whose receipt amount differs from what payroll expected. */
export interface BankReceiptMismatchLine {
  employeeId: string;
  employeeName: string;
  expectedAmount: number;
  receiptAmount: number;
}

/** Dekont eşleştirme sonucu: only `confirmedCount` payslips actually changed (marked paid-confirmed); the rest is reported for review. */
export interface BankReceiptMatchSummary {
  confirmedCount: number;
  amountMismatches: BankReceiptMismatchLine[];
  rejected: BankReceiptSummaryLine[];
  unmatched: BankReceiptSummaryLine[];
  unrecognizedRowCount: number;
}

/** Matches an uploaded bank return file (generic CSV: IBAN, amount, optional status) against the period's expected payments (`payroll.bankfile.export`). */
export async function matchPayrollBankReceipts(periodId: string, csvContent: string): Promise<BankReceiptMatchSummary> {
  const { data } = await apiClient.post<BankReceiptMatchSummary>(`${PAYROLL_BASE}/periods/${periodId}/receipts/match`, { csvContent });
  return data;
}

/** Downloads the MUHSGK / SGK premium data set of a period as CSV for reconciliation (`payroll.declarations.export`); not the official declaration format. */
export async function downloadPayrollDeclarationSummary(id: string, fallbackName: string): Promise<DownloadedPayrollFile> {
  try {
    const response = await apiClient.get<Blob>(`${PAYROLL_BASE}/periods/${id}/declaration-summary`, { responseType: "blob" });
    const disposition = response.headers["content-disposition"];
    return {
      blob: response.data,
      fileName: parseContentDispositionFileName(typeof disposition === "string" ? disposition : undefined) ?? fallbackName,
    };
  } catch (error) {
    throw await unwrapBlobError(error);
  }
}

export type PayrollAdjustmentKind = "bonus" | "commission" | "performance" | "other";

/** `PayrollAdjustmentDto`: a one-off gross earning (prim, komisyon...) of the period, with the employee's name. */
export interface PayrollAdjustment {
  id: string;
  employeeId: string;
  employeeName: string;
  kind: PayrollAdjustmentKind;
  label: string;
  grossAmount: number;
  /** True when the amount is a net the employee keeps (grossed up in payroll). Missing = gross. */
  isNet?: boolean;
}

export interface PayrollAdjustmentInput {
  employeeId: string;
  kind: PayrollAdjustmentKind;
  label: string;
  grossAmount: number;
  isNet?: boolean;
}

/** The period's one-off taxable earnings, all employees (`payroll.trace.view`). */
export async function listPayrollAdjustments(periodId: string): Promise<PayrollAdjustment[]> {
  const { data } = await apiClient.get<PayrollAdjustment[]>(`${PAYROLL_BASE}/periods/${periodId}/adjustments`);
  return data;
}

/** Adds a gross one-off earning in an open period (`payroll.adjustment.manage`); returns its id. */
export async function addPayrollAdjustment(periodId: string, input: PayrollAdjustmentInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${PAYROLL_BASE}/periods/${periodId}/adjustments`, input);
  return data;
}

/** Removes an earning while its period is still open (`payroll.adjustment.manage`). */
export async function removePayrollAdjustment(adjustmentId: string): Promise<void> {
  await apiClient.delete(`${PAYROLL_BASE}/adjustments/${adjustmentId}`);
}

/** `PayrollPreviewLineDto`: one employee's figures in a deneme bordrosu. */
export interface PayrollPreviewLine {
  employeeId: string;
  employeeName: string;
  gross: number;
  net: number;
  employerCost: number;
}

/** `PayrollPreviewDto`: a full calculation of the open period that stores nothing. */
export interface PayrollPreview {
  calculatedCount: number;
  skippedCount: number;
  totalGross: number;
  totalNet: number;
  totalEmployerCost: number;
  lines: PayrollPreviewLine[];
  skipped: { employeeId: string; reason: string }[];
  warnings: { employeeId: string; code: string; details?: string }[];
}

/** Deneme bordrosu: calculates the open period without saving payslips (`payroll.period.recalculate`). */
export async function previewPayrollPeriod(periodId: string): Promise<PayrollPreview> {
  const { data } = await apiClient.get<PayrollPreview>(`${PAYROLL_BASE}/periods/${periodId}/preview`);
  return data;
}
