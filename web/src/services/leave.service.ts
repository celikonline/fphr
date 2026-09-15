/**
 * Leave module: mirrors `LeaveControllers.cs` (`api/v1/leave/rule-sets`, `/leave/employees/{employeeId}`, `/leave/me`).
 * Reads: rule sets need leave.rule_sets.read, an employee's balances/ledger leave.balances.read, their requests
 * leave.requests.read; the `/me` variants need leave.my.balances / leave.my.requests. Null fields may be omitted by the API.
 */
import { apiClient } from "@senseik/api-client";

const LEAVE_BASE = "/v1/leave";

/** Mirrors `LeaveTypeCode`. */
export type LeaveTypeCode =
  | "custom"
  | "annual"
  | "excuse"
  | "sick"
  | "jobSearch"
  | "marriage"
  | "bereavement"
  | "paternity"
  | "maternity"
  | "postnatalUnpaid"
  | "paidMilitaryService"
  | "unpaid"
  | "fosterCare"
  | "remoteWork"
  | "birthday";
export type LeaveLimitType = "unlimited" | "perRequest" | "perYear";
export type NegativeBalanceMode = "none" | "unlimited" | "limited";
export type AccrualFrequency = "yearly" | "monthly" | "daily";
export type AccrualTiming = "endOfPeriod" | "startOfPeriod";
export type AccrualBasis = "hireDate" | "calendarYear";
export type CarryOverMode = "reset" | "unlimited" | "limited";
export type LeaveUnit = "fullDay" | "halfDay" | "hourly";
export type LeaveLedgerEntryType = "openingBalance" | "accrual" | "carryOver" | "usage" | "adjustment" | "extraGrant";
export type LeaveLedgerSource = "system" | "manual" | "import" | "request" | "overtimeConversion";
export type LeaveRequestStatus = "pending" | "approved" | "rejected" | "withdrawn" | "cancelled";
export type LeaveRequestSource = "selfService" | "onBehalf" | "import" | "attendance" | "resubmission";

/** `LeaveBalance` projection of one PerYear type (days). */
export interface LeaveBalance {
  leaveTypeId: string;
  currentPeriodFrom: string;
  currentPeriodTo: string;
  nextAccrualDate: string;
  totalAccrued: number;
  totalUsed: number;
  extraGrants: number;
  adjustments: number;
  carriedOverIntoPeriod: number;
  lastPeriodAccrued: number;
  currentPeriodUsed: number;
  futureApprovedInPeriod: number;
  nextPeriodApproved: number;
  currentBalance: number;
  availableBalance: number;
  negativeBalanceLimit?: number | null;
}

/** `LeaveBalanceDto`: `leaveTypeName` may be a resource key for seeded types (see `leaveName`). */
export interface LeaveBalanceItem {
  leaveTypeName: string;
  leaveTypeCode: LeaveTypeCode;
  balance: LeaveBalance;
}

export interface EmployeeLeaveBalances {
  employeeId: string;
  ruleSetId: string;
  hireDate: string;
  leaveAccrualStartDate: string;
  balances: LeaveBalanceItem[];
}

export interface LeaveLedgerEntry {
  id: string;
  leaveTypeId: string;
  type: LeaveLedgerEntryType;
  amount: number;
  effectiveDate: string;
  source: LeaveLedgerSource;
  sourceRefId?: string | null;
  reason?: string | null;
  recordedAt: string;
  recordedByUserId?: string | null;
}

export interface LeaveRequest {
  id: string;
  employeeId: string;
  leaveTypeId: string;
  leaveTypeCode: LeaveTypeCode;
  startDate: string;
  endDate: string;
  calculatedDays: number;
  description?: string | null;
  status: LeaveRequestStatus;
  source: LeaveRequestSource;
  approvalRequestId?: string | null;
  revisionNo: number;
  submittedAt: string;
  decidedAt?: string | null;
}

/** `LeaveTypeSettings` (only the fields the web reads are typed strictly). */
export interface LeaveTypeSettings {
  code: LeaveTypeCode;
  name: string;
  description?: string | null;
  isPaid: boolean;
  limitType: LeaveLimitType;
  perRequestMaxDays?: number | null;
  annualDays?: number | null;
  ageExceptionEnabled: boolean;
  ageExceptionMinDays?: number | null;
  negativeBalance: NegativeBalanceMode;
  negativeBalanceLimitDays?: number | null;
  accrualFrequency: AccrualFrequency;
  accrualTiming: AccrualTiming;
  accrualBasis: AccrualBasis;
  carryOverMode: CarryOverMode;
  carryOverMaxDays?: number | null;
  seniorityTiers?: unknown[] | null;
  minConsecutiveDays?: number | null;
  maxConsecutiveDays?: number | null;
  unit: LeaveUnit;
  requiresDescription: boolean;
  requiresSubstitute: boolean;
  requiresDocument: boolean;
  waitingPeriodMonths: number;
  countsNonWorkingDays: boolean;
  countsTowardAccrualService: boolean;
  showTypeNameOnCalendar: boolean;
  isSensitive: boolean;
  allowSelfServiceRequest: boolean;
  isDisabled: boolean;
  isStatutory: boolean;
}

export interface LeaveType {
  id: string;
  sortOrder: number;
  settings: LeaveTypeSettings;
}

export interface LeaveRuleSet {
  id: string;
  name: string;
  isDefault: boolean;
  isSystemSeed: boolean;
  unpaidLeaveShiftsAccrual: boolean;
  assignedEmployeeCount: number;
  types: LeaveType[];
}

/** `LeaveRequestBody` (submit and pending edit). */
export interface LeaveRequestInput {
  leaveTypeId: string;
  startDate: string;
  endDate: string;
  description: string | null;
}

export async function listLeaveRuleSets(): Promise<LeaveRuleSet[]> {
  const { data } = await apiClient.get<LeaveRuleSet[]>(`${LEAVE_BASE}/rule-sets`);
  return data;
}

export async function getLeaveRuleSet(id: string): Promise<LeaveRuleSet> {
  const { data } = await apiClient.get<LeaveRuleSet>(`${LEAVE_BASE}/rule-sets/${id}`);
  return data;
}

export async function cloneLeaveRuleSet(id: string, name: string): Promise<string> {
  const { data } = await apiClient.post<string>(`${LEAVE_BASE}/rule-sets/${id}/clone`, { name });
  return data;
}

export async function getEmployeeLeaveBalances(employeeId: string): Promise<EmployeeLeaveBalances> {
  const { data } = await apiClient.get<EmployeeLeaveBalances>(`${LEAVE_BASE}/employees/${employeeId}/balances`);
  return data;
}

export async function getEmployeeLeaveRequests(employeeId: string): Promise<LeaveRequest[]> {
  const { data } = await apiClient.get<LeaveRequest[]>(`${LEAVE_BASE}/employees/${employeeId}/requests`);
  return data;
}

export async function getEmployeeLeaveLedger(employeeId: string, leaveTypeId?: string): Promise<LeaveLedgerEntry[]> {
  const { data } = await apiClient.get<LeaveLedgerEntry[]>(`${LEAVE_BASE}/employees/${employeeId}/ledger`, {
    params: leaveTypeId ? { leaveTypeId } : undefined,
  });
  return data;
}

export async function getMyLeaveBalances(): Promise<EmployeeLeaveBalances> {
  const { data } = await apiClient.get<EmployeeLeaveBalances>(`${LEAVE_BASE}/me/balances`);
  return data;
}

export async function getMyLeaveRequests(): Promise<LeaveRequest[]> {
  const { data } = await apiClient.get<LeaveRequest[]>(`${LEAVE_BASE}/me/requests`);
  return data;
}

export async function submitLeaveRequest(input: LeaveRequestInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${LEAVE_BASE}/me/requests`, input);
  return data;
}

export async function updatePendingLeaveRequest(id: string, input: LeaveRequestInput): Promise<void> {
  await apiClient.put(`${LEAVE_BASE}/me/requests/${id}`, input);
}

export async function withdrawLeaveRequest(id: string): Promise<void> {
  await apiClient.post(`${LEAVE_BASE}/me/requests/${id}/withdraw`);
}

/** `AdjustLeaveBalanceRequest` ("Bakiye düzeltmesi"): signed days, mandatory reason. */
export interface LeaveAdjustmentInput {
  leaveTypeId: string;
  days: number;
  effectiveDate: string;
  reason: string;
}

export async function adjustLeaveBalance(employeeId: string, input: LeaveAdjustmentInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${LEAVE_BASE}/employees/${employeeId}/adjustments`, input);
  return data;
}

export async function recalculateLeaveBalances(employeeId: string): Promise<void> {
  await apiClient.post(`${LEAVE_BASE}/employees/${employeeId}/recalculate`);
}
