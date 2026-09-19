/**
 * Leave module: mirrors `LeaveControllers.cs` (`api/v1/leave/rule-sets`, `/leave/employees/{employeeId}`, `/leave/me`).
 * Reads: rule sets need leave.rule_sets.read, an employee's balances/ledger leave.balances.read, their requests
 * leave.requests.read; the `/me` variants need leave.my.balances / leave.my.requests. Null fields may be omitted by the API.
 * Also the leave calendar (`/leave/calendar`) and overtime (`/leave/me/overtimes`, `/leave/employees/{employeeId}/overtime*`).
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
/** Mirrors `HalfDayPeriod` (`LeaveUnit.HalfDay` requests only). */
export type LeaveHalfDayPeriod = "morning" | "afternoon";
export type LeaveLedgerEntryType = "openingBalance" | "accrual" | "carryOver" | "usage" | "adjustment" | "extraGrant" | "reversal";
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
  /** Type name from the employee's rule set, already localized by the API; missing when the type is no longer in it. */
  leaveTypeName?: string | null;
  /** Rejected own request this one resubmits ("Düzenleyip yeniden gönder"). */
  previousRequestId?: string | null;
  cancelledAt?: string | null;
  cancellationReason?: string | null;
  /** Set for `LeaveUnit.Hourly` requests ("HH:mm:ss"); null for FullDay/HalfDay. */
  startTime?: string | null;
  endTime?: string | null;
  /** Set for `LeaveUnit.HalfDay` requests; null for FullDay/Hourly. */
  halfDayPeriod?: LeaveHalfDayPeriod | null;
}

/** `LeaveRequestDetailDto` approval step: the approver name is resolved by the API; a role step without a person has `fallbackRoleCode`. */
export interface LeaveApprovalStep {
  stepNumber: number;
  approverEmployeeId?: string | null;
  approverName?: string | null;
  fallbackRoleCode?: string | null;
  isRequired: boolean;
  status: string;
  /** Decision note left by the approver; null until the step is decided or the approver left none. */
  comment?: string | null;
}

export interface LeaveRequestApproval {
  status: string;
  currentStepNumber?: number | null;
  steps: LeaveApprovalStep[];
}

/** `LeaveRequestDetailDto` (`GET /leave/me/requests/{id}`, `/leave/employees/{employeeId}/requests/{id}`); `approval` is missing without a workflow. */
export interface LeaveRequestDetail extends LeaveRequest {
  employeeName: string;
  approval?: LeaveRequestApproval | null;
  /** "Kalan İzin Bakiyesi": null for non-PerYear types or when the employee has no leave profile. */
  remainingBalance?: number | null;
  /** "Çakışan İzinler": same org unit, active leave over this request's dates, excluding the requester. */
  teamOverlap?: LeaveTeamOverlap | null;
}

/** `LeaveCalendarEntryDto`: masked entries (sensitive type or hidden type name) carry no type fields. */
export interface LeaveCalendarEntry {
  requestId: string;
  employeeId: string;
  employeeName: string;
  startDate: string;
  endDate: string;
  calculatedDays: number;
  status: "approved" | "pending";
  isMasked: boolean;
  leaveTypeCode?: LeaveTypeCode | null;
  leaveTypeName?: string | null;
  overlappingApprovedCount?: number | null;
}

export interface LeaveCalendarQuery {
  from: string;
  to: string;
  orgUnitId?: string | null;
}

/** `AssignLeaveRuleSetResult`: employees that could not be assigned come back as warnings (localized `message`). */
export interface LeaveRuleSetAssignmentResult {
  assignedCount: number;
  warnings: { employeeId: string; code: string; message: string }[];
}

/** `RequestableLeaveTypeDto` (`GET /leave/me/leave-types`): active self-service types, `name` localized by the API. */
export interface RequestableLeaveType {
  id: string;
  code: LeaveTypeCode;
  name: string;
  description?: string | null;
  unit: LeaveUnit;
  isPaid: boolean;
  hasBalance: boolean;
  perRequestMaxDays?: number | null;
  requiresDescription: boolean;
  requiresDocument: boolean;
  countsNonWorkingDays: boolean;
}

/** `TeamOverlapDto`: other approved/pending leaves in the same org unit over a date range (S-7), non-blocking info. */
export interface LeaveTeamOverlap {
  count: number;
  employees: { employeeId: string; name: string; startDate: string; endDate: string }[];
  /** The requester's org unit name (S-8, senseik-026); best-effort, absent when the lookup misses. */
  orgUnitName?: string | null;
}

/** `LeaveRequestPreviewResponse`: day count of a draft; rule violations come back as localized warnings. */
export interface LeaveRequestPreview {
  days: number;
  weeklyRestDays: number;
  publicHolidayDays: number;
  warnings: { code: string; message: string }[];
  /** Absent on older API builds. */
  teamOverlap?: LeaveTeamOverlap | null;
}

export interface LeaveRequestPreviewInput {
  leaveTypeId: string;
  start: string;
  end: string;
  editedRequestId?: string;
  /** Hourly request draft ("HH:mm:ss"). */
  startTime?: string;
  endTime?: string;
  /** HalfDay request draft. */
  halfDayPeriod?: LeaveHalfDayPeriod;
}

/** "Kıdem kademesi": completed service years compared with `afterYears` add `extraDays` (cumulative); Annual type only. */
export interface LeaveSeniorityTier {
  afterYears: number;
  comparison: "greaterOrEqual" | "greaterThan";
  extraDays: number;
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
  seniorityTiers?: LeaveSeniorityTier[] | null;
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
  /** Hourly request ("HH:mm:ss"); omit for FullDay/HalfDay. */
  startTime?: string;
  endTime?: string;
  /** HalfDay request; omit for FullDay/Hourly. */
  halfDayPeriod?: LeaveHalfDayPeriod;
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

export async function getMyRequestableLeaveTypes(): Promise<RequestableLeaveType[]> {
  const { data } = await apiClient.get<RequestableLeaveType[]>(`${LEAVE_BASE}/me/leave-types`);
  return data;
}

export async function previewMyLeaveRequest(input: LeaveRequestPreviewInput): Promise<LeaveRequestPreview> {
  const { data } = await apiClient.get<LeaveRequestPreview>(`${LEAVE_BASE}/me/requests/preview`, { params: input });
  return data;
}

export async function previewEmployeeLeaveRequest(employeeId: string, input: LeaveRequestPreviewInput): Promise<LeaveRequestPreview> {
  const { data } = await apiClient.get<LeaveRequestPreview>(`${LEAVE_BASE}/employees/${employeeId}/requests/preview`, { params: input });
  return data;
}

export async function getMyLeaveLedger(leaveTypeId?: string): Promise<LeaveLedgerEntry[]> {
  const { data } = await apiClient.get<LeaveLedgerEntry[]>(`${LEAVE_BASE}/me/ledger`, { params: leaveTypeId ? { leaveTypeId } : undefined });
  return data;
}

/** `LeaveRequestOnBehalfBody` (İşlemler → İzin Ekle); `bypassApproval` needs leave.requests.bypass_approval. */
export interface LeaveRequestOnBehalfInput extends LeaveRequestInput {
  bypassApproval: boolean;
}

export async function createLeaveRequestOnBehalf(employeeId: string, input: LeaveRequestOnBehalfInput): Promise<LeaveRequest> {
  const { data } = await apiClient.post<LeaveRequest>(`${LEAVE_BASE}/employees/${employeeId}/requests`, input);
  return data;
}

/** Self-service submit body; `previousRequestId` resubmits a rejected own request (409 leave.errors.resubmission_source_invalid). */
export interface MyLeaveRequestInput extends LeaveRequestInput {
  previousRequestId?: string | null;
}

export async function submitLeaveRequest(input: MyLeaveRequestInput): Promise<LeaveRequest> {
  const { data } = await apiClient.post<LeaveRequest>(`${LEAVE_BASE}/me/requests`, input);
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

// ---------------------------------------------------------------- Rule set management (leave.rule_sets.manage / .assign)

export async function renameLeaveRuleSet(id: string, name: string): Promise<void> {
  await apiClient.put(`${LEAVE_BASE}/rule-sets/${id}/name`, { name });
}

export async function setDefaultLeaveRuleSet(id: string): Promise<void> {
  await apiClient.post(`${LEAVE_BASE}/rule-sets/${id}/set-default`);
}

/** 409 leave.errors.rule_set_is_default / leave.errors.rule_set_has_employees. */
export async function deleteLeaveRuleSet(id: string): Promise<void> {
  await apiClient.delete(`${LEAVE_BASE}/rule-sets/${id}`);
}

/** Moves the employees to the rule set (leave.rule_sets.assign). */
export async function assignLeaveRuleSet(id: string, employeeIds: string[]): Promise<LeaveRuleSetAssignmentResult> {
  const { data } = await apiClient.post<LeaveRuleSetAssignmentResult>(`${LEAVE_BASE}/rule-sets/${id}/assign`, { employeeIds });
  return { assignedCount: data.assignedCount ?? 0, warnings: data.warnings ?? [] };
}

/** "Yeni Tür ve Kural Ekle": adds a leave type to the rule set; returns the new type's id. */
export async function addLeaveType(ruleSetId: string, settings: LeaveTypeSettings): Promise<string> {
  const { data } = await apiClient.post<string>(`${LEAVE_BASE}/rule-sets/${ruleSetId}/types`, settings);
  return data;
}

export async function updateLeaveType(ruleSetId: string, typeId: string, settings: LeaveTypeSettings): Promise<void> {
  await apiClient.put(`${LEAVE_BASE}/rule-sets/${ruleSetId}/types/${typeId}`, settings);
}

/** Hides/reactivates a type without deleting it (its history and ledger rows stay put). */
export async function setLeaveTypeDisabled(ruleSetId: string, typeId: string, disabled: boolean): Promise<void> {
  await apiClient.put(`${LEAVE_BASE}/rule-sets/${ruleSetId}/types/${typeId}/disabled`, { disabled });
}

// ---------------------------------------------------------------- Request detail / cancel / calendar

export async function getMyLeaveRequest(id: string): Promise<LeaveRequestDetail> {
  const { data } = await apiClient.get<LeaveRequestDetail>(`${LEAVE_BASE}/me/requests/${id}`);
  return data;
}

export async function getEmployeeLeaveRequest(employeeId: string, id: string): Promise<LeaveRequestDetail> {
  const { data } = await apiClient.get<LeaveRequestDetail>(`${LEAVE_BASE}/employees/${employeeId}/requests/${id}`);
  return data;
}

/** Cancels a pending or approved request (leave.requests.cancel_approved); reason required, ≤500 chars. 409 leave.errors.request_not_cancellable. */
export async function cancelLeaveRequest(employeeId: string, id: string, reason: string): Promise<void> {
  await apiClient.post(`${LEAVE_BASE}/employees/${employeeId}/requests/${id}/cancel`, { reason });
}

/** Leave calendar (leave.my.calendar + Calendar.ShowMenu); at most 93 days (400 leave.errors.calendar_range_too_large). */
export async function getLeaveCalendar(query: LeaveCalendarQuery): Promise<LeaveCalendarEntry[]> {
  const { data } = await apiClient.get<LeaveCalendarEntry[]>(`${LEAVE_BASE}/calendar`, {
    params: { from: query.from, to: query.to, ...(query.orgUnitId ? { orgUnitId: query.orgUnitId } : {}) },
  });
  return data;
}

/** Pending leave requests of the caller's team (leave.requests.read data scope) for the Ana Sayfa "İzin Talepleri" widget. */
export async function getTeamLeaveRequests(): Promise<LeaveCalendarEntry[]> {
  const { data } = await apiClient.get<LeaveCalendarEntry[]>(`${LEAVE_BASE}/team/requests`);
  return data;
}

// ---------------------------------------------------------------- Absence ("Devamsızlık"; S-7, leave.absences.{view,manage})

export type AbsenceKind = "unexcused" | "excused" | "late";

/** `AbsenceDto`: an append-only "mazeretsiz/mazeretli devamsızlık" record; a corrective entry sets `reversesId` and the original gets `isReversed`. */
export interface Absence {
  id: string;
  date: string;
  kind: AbsenceKind;
  hours?: number | null;
  note?: string | null;
  recordedBy: string;
  recordedAt: string;
  reversesId?: string | null;
  isReversed: boolean;
}

export interface AbsenceInput {
  date: string;
  kind: AbsenceKind;
  /** Only meaningful for `kind: "late"`. */
  hours?: number | null;
  note?: string | null;
}

/** leave.absences.view */
export async function listAbsences(employeeId: string, from: string, to: string): Promise<Absence[]> {
  const { data } = await apiClient.get<Absence[]>(`${LEAVE_BASE}/employees/${employeeId}/absences`, { params: { from, to } });
  return data;
}

/** leave.absences.manage */
export async function addAbsence(employeeId: string, input: AbsenceInput): Promise<string> {
  const { data } = await apiClient.post<{ id: string }>(`${LEAVE_BASE}/employees/${employeeId}/absences`, input);
  return data.id;
}

/** Corrects a record with a reversal entry (append-only, no delete); 409 leave.absence.already_reversed. */
export async function reverseAbsence(employeeId: string, id: string, reason: string): Promise<string> {
  const { data } = await apiClient.post<{ id: string }>(`${LEAVE_BASE}/employees/${employeeId}/absences/${id}/reverse`, { reason });
  return data.id;
}

// ---------------------------------------------------------------- Overtime ("Fazla mesai")

export type OvertimeRequestStatus = "pending" | "approved" | "rejected" | "withdrawn" | "cancelled";
/** `OvertimeRequestSource`: manual = self-service, admin = on behalf. */
export type OvertimeRequestSource = "manual" | "attendance" | "admin" | "import";
export type OvertimeDayKind = "workday" | "weeklyRest" | "publicHoliday";
export type OvertimeConversionKind = "pay" | "timeOff";
/** Conversion basis: payable minutes (default) or the full duration (leave.overtimes.override_payable). */
export type OvertimeMinutesBasis = "payable" | "full";
export type OvertimeConversionStatus = "posted" | "reversed" | "expired";
export type OvertimeConsentSource = "employmentContract" | "standaloneForm";
/** `OvertimePayRateKind` (İş K. m.41): Rate25 = %25 fazlası, Rate50 = %50 fazlası (varsayılan, asıl fazla mesai). */
export type OvertimePayRateKind = "rate25" | "rate50";

/** `OvertimeRequestDto`: `startTime` is a TimeOnly ("HH:mm:ss"), `startAt`/`endAt` the employee's local wall clock. */
export interface OvertimeRequest {
  id: string;
  employeeId: string;
  workDate: string;
  startTime: string;
  durationMinutes: number;
  startAt: string;
  endAt: string;
  dayKind: OvertimeDayKind;
  overlapWithScheduleMinutes: number;
  payableMinutes: number;
  description?: string | null;
  source: OvertimeRequestSource;
  status: OvertimeRequestStatus;
  approvalRequestId?: string | null;
  submittedAt: string;
  decidedAt?: string | null;
  /** Set once the approved request was converted (time off). */
  conversionId?: string | null;
  payRateKind: OvertimePayRateKind;
  payRateMultiplier: number;
}

/** `OvertimeSubmissionDto`: `warnings` are resource keys ("leave.warnings.overtime_consent_missing"), localized by the web. */
export interface OvertimeSubmission {
  id: string;
  overlapWithScheduleMinutes: number;
  payableMinutes: number;
  warnings: string[];
}

/** `OvertimeRequestBody`. */
export interface OvertimeRequestInput {
  workDate: string;
  /** "HH:mm:ss". */
  startTime: string;
  durationMinutes: number;
  description: string | null;
  /** Omitted defaults to "rate50" (API side). */
  payRateKind?: OvertimePayRateKind;
}

/** Kart senseik-012 §2: 270 saat yıllık fazla mesai sınırı göstergesi (hesaplanan, kalıcı sayaç değil). */
export interface AnnualOvertimeUsage {
  year: number;
  usedMinutes: number;
  limitMinutes: number;
  exceeded: boolean;
}

/** `OvertimeConversionDto`. */
export interface OvertimeConversion {
  id: string;
  overtimeRequestId: string;
  employeeId: string;
  kind: OvertimeConversionKind;
  minutesBasis: OvertimeMinutesBasis;
  rate: number;
  basisMinutes: number;
  resultMinutes: number;
  resultDays: number;
  leaveTypeId?: string | null;
  ledgerEntryId?: string | null;
  status: OvertimeConversionStatus;
  convertedAt: string;
  /** Yalnız time-off dönüşümlerinde dolu: m.41 6 ay kullanım süresi son tarihi. */
  usedByDate?: string | null;
}

/** `ConvertOvertimeToTimeOffRequest`: rate 1–3 (API default when null), `leaveTypeId` null = the default balance type. */
export interface OvertimeConversionInput {
  rate: number | null;
  minutesBasis: OvertimeMinutesBasis;
  leaveTypeId: string | null;
  note: string | null;
}

/** `OvertimeConsentDto`: written consent; a revocation takes effect at `revocationEffectiveAt`. */
export interface OvertimeConsent {
  id: string;
  employeeId: string;
  givenAt: string;
  source: OvertimeConsentSource;
  coversNightWork: boolean;
  revocationNoticeAt?: string | null;
  revocationEffectiveAt?: string | null;
  documentId?: string | null;
}

/** `RecordOvertimeConsentRequest`. */
export interface OvertimeConsentInput {
  givenAt: string;
  source: OvertimeConsentSource;
  coversNightWork: boolean;
  documentId: string | null;
}

/** leave.my.overtimes */
export async function getMyOvertimes(): Promise<OvertimeRequest[]> {
  const { data } = await apiClient.get<OvertimeRequest[]>(`${LEAVE_BASE}/me/overtimes`);
  return data;
}

export async function submitMyOvertime(input: OvertimeRequestInput): Promise<OvertimeSubmission> {
  const { data } = await apiClient.post<OvertimeSubmission>(`${LEAVE_BASE}/me/overtimes`, input);
  return data;
}

export async function withdrawMyOvertime(id: string): Promise<void> {
  await apiClient.post(`${LEAVE_BASE}/me/overtimes/${id}/withdraw`);
}

/** leave.my.overtimes: 270 saat yıllık sınır göstergesi. */
export async function getMyOvertimeAnnualUsage(year?: number): Promise<AnnualOvertimeUsage> {
  const { data } = await apiClient.get<AnnualOvertimeUsage>(`${LEAVE_BASE}/me/overtimes/annual-usage`, { params: { year } });
  return data;
}

/** leave.overtimes.read */
export async function getEmployeeOvertimes(employeeId: string): Promise<OvertimeRequest[]> {
  const { data } = await apiClient.get<OvertimeRequest[]>(`${LEAVE_BASE}/employees/${employeeId}/overtimes`);
  return data;
}

/** leave.overtimes.read: 270 saat yıllık sınır göstergesi (çalışan kartı). */
export async function getEmployeeOvertimeAnnualUsage(employeeId: string, year?: number): Promise<AnnualOvertimeUsage> {
  const { data } = await apiClient.get<AnnualOvertimeUsage>(`${LEAVE_BASE}/employees/${employeeId}/overtimes/annual-usage`, { params: { year } });
  return data;
}

/** leave.overtimes.create_on_behalf: HR/yönetici oran düzeltmesi (yalnız Pending). */
export async function changeOvertimePayRateKind(employeeId: string, id: string, payRateKind: OvertimePayRateKind): Promise<void> {
  await apiClient.post(`${LEAVE_BASE}/employees/${employeeId}/overtimes/${id}/pay-rate`, { payRateKind });
}

/** leave.overtimes.create_on_behalf (approval still runs). */
export async function createOvertimeOnBehalf(employeeId: string, input: OvertimeRequestInput): Promise<OvertimeSubmission> {
  const { data } = await apiClient.post<OvertimeSubmission>(`${LEAVE_BASE}/employees/${employeeId}/overtimes`, input);
  return data;
}

/** leave.overtimes.convert; `minutesBasis: "full"` also needs leave.overtimes.override_payable. */
export async function convertOvertimeToTimeOff(employeeId: string, id: string, input: OvertimeConversionInput): Promise<OvertimeConversion> {
  const { data } = await apiClient.post<OvertimeConversion>(`${LEAVE_BASE}/employees/${employeeId}/overtimes/${id}/convert-to-time-off`, input);
  return data;
}

export async function getOvertimeConversions(employeeId: string): Promise<OvertimeConversion[]> {
  const { data } = await apiClient.get<OvertimeConversion[]>(`${LEAVE_BASE}/employees/${employeeId}/overtime-conversions`);
  return data;
}

/** leave.overtime_consents.manage */
export async function getOvertimeConsents(employeeId: string): Promise<OvertimeConsent[]> {
  const { data } = await apiClient.get<OvertimeConsent[]>(`${LEAVE_BASE}/employees/${employeeId}/overtime-consents`);
  return data;
}

export async function recordOvertimeConsent(employeeId: string, input: OvertimeConsentInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${LEAVE_BASE}/employees/${employeeId}/overtime-consents`, input);
  return data;
}

/** Written revocation notice; the API returns the updated consent with `revocationEffectiveAt`. */
export async function revokeOvertimeConsent(employeeId: string, id: string, noticeAt: string): Promise<OvertimeConsent> {
  const { data } = await apiClient.post<OvertimeConsent>(`${LEAVE_BASE}/employees/${employeeId}/overtime-consents/${id}/revoke`, { noticeAt });
  return data;
}
