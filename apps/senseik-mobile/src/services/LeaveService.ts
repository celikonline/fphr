import { apiClient } from "@/lib/apiClient";
import { leaveTypeDisplayName } from "@/lib/leaveTypeNames";

/**
 * Leave module (self-service): mirrors `LeaveControllers.cs`'s `/leave/me` group
 * (`api/v1/leave/me/balances`, `/leave/me/requests`, `/leave/me/leave-types`) - the same endpoints
 * `web/src/services/leave.service.ts` calls. Only the self-service surface a phone needs: balances,
 * "my requests" list, requestable leave types, and submitting a new request. Approval/decision and
 * HR-facing endpoints (employees/{id}/..., team/requests) stay web-only for now.
 */
const LEAVE_BASE = "/api/v1/leave";

/** Mirrors `LeaveTypeCode` (Sense.Modules.Leave.Domain), camelCase JsonStringEnumConverter. */
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
  | "breastfeeding"
  | "disabledChild"
  | "remoteWork"
  | "birthday";

export type LeaveUnit = "fullDay" | "halfDay" | "hourly";
export type LeaveRequestStatus = "pending" | "approved" | "rejected" | "withdrawn" | "cancelled";
/** Mirrors `HalfDayPeriod` (`LeaveUnit.HalfDay` requests only). */
export type LeaveHalfDayPeriod = "morning" | "afternoon";

/**
 * `LeaveBalance` projection of one PerYear type (days). `totalAccrued`/`totalUsed` are LIFETIME
 * sums (or period sums for "resets each period" types - see `LeaveBalanceEngine.Project`), so they
 * generally do NOT add up to `availableBalance`; the fields that do are the period-scoped ones:
 * `availableBalance = carriedOverIntoPeriod + lastPeriodAccrued + extraGrants + adjustments -
 * currentPeriodUsed - futureApprovedInPeriod` (`LeaveBalanceEngine.cs:239-256`, server-computed -
 * the mobile app never recomputes it, only breaks it down for display, B8).
 */
export interface LeaveBalance {
  leaveTypeId: string;
  currentPeriodFrom: string;
  currentPeriodTo: string;
  nextAccrualDate: string;
  totalAccrued: number;
  totalUsed: number;
  /** Carried over from the previous period into this one. */
  carriedOverIntoPeriod: number;
  /** This period's accrual (may differ from lifetime `totalAccrued`). */
  lastPeriodAccrued: number;
  /** Manually granted extra days in this period. */
  extraGrants: number;
  /** Manual balance corrections in this period (opening balance included); signed - can be negative. */
  adjustments: number;
  /** Days used so far in this period (may differ from lifetime `totalUsed`). */
  currentPeriodUsed: number;
  /** Approved leave with a future start date in this period - already subtracted from `availableBalance`. */
  futureApprovedInPeriod: number;
  currentBalance: number;
  availableBalance: number;
  negativeBalanceLimit?: number | null;
}

/**
 * `LeaveBalanceDto`: `leaveTypeName` may be a raw resource key for seeded types; `getMyBalances`
 * resolves it (`leaveTypeDisplayName`). `pendingDays`: days of the employee's still-pending (not
 * yet decided) requests of this type - not yet deducted from `balance.availableBalance` by this
 * DTO itself, but `LeaveRequestRules` deducts it when validating a new request. Absent on older API builds.
 */
export interface LeaveBalanceItem {
  leaveTypeName: string;
  leaveTypeCode: LeaveTypeCode;
  balance: LeaveBalance;
  pendingDays?: number;
}

export interface EmployeeLeaveBalances {
  employeeId: string;
  ruleSetId: string;
  hireDate: string;
  leaveAccrualStartDate: string;
  balances: LeaveBalanceItem[];
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
  submittedAt: string;
  decidedAt?: string | null;
  leaveTypeName?: string | null;
  previousRequestId?: string | null;
  cancelledAt?: string | null;
  cancellationReason?: string | null;
}

/**
 * `RequestableLeaveTypeDto` (`GET /leave/me/leave-types`): active self-service types. Since senseik-039 the API returns
 * `name` raw (e.g. "leave.type.annual"); `getMyRequestableLeaveTypes` resolves it (`leaveTypeDisplayName`).
 */
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
  /** "Vekil" gerektiren türler; absent on older API builds (treated as `false`, mirrors `web/src/services/leave.service.ts`). */
  requiresSubstitute?: boolean;
}

/**
 * Self-service submit body (`LeaveRequestBody`); `previousRequestId` resubmits a rejected own
 * request. `halfDayPeriod` is required when the selected type's `unit` is `"halfDay"`; `startTime`/
 * `endTime` ("HH:mm:ss") when it's `"hourly"` (start/end date are the same single day in both
 * cases). `substituteEmployeeId` is required when the type's `requiresSubstitute` is set
 * (`LeaveRequestRules.ValidateSubstituteAsync` - server rejects a missing substitute, the requester
 * themself, an inactive employee, or one already on leave over the same dates). `documentIds`
 * (uploaded via `DocumentService.uploadMyDocument`) is required when `requiresDocument` is set.
 */
export interface MyLeaveRequestInput {
  leaveTypeId: string;
  startDate: string;
  endDate: string;
  description: string | null;
  halfDayPeriod?: LeaveHalfDayPeriod;
  startTime?: string;
  endTime?: string;
  substituteEmployeeId?: string;
  documentIds?: string[];
  previousRequestId?: string | null;
}

/**
 * `LeaveRequestPreviewResponse` (`GET /leave/me/requests/preview`): live day-count preview shown
 * while filling the form (B-mobil-006) - `days` is what will actually be deducted (e.g. Sunday-only
 * "full day" requests can preview `0` when the day isn't a working day); `warnings[].message` is
 * already localized server-side. Mirrors `web/src/services/leave.service.ts`'s `LeaveRequestPreview`
 * (mobile only reads `days`/`warnings`, not `weeklyRestDays`/`publicHolidayDays`/`teamOverlap`).
 */
export interface LeaveRequestPreview {
  days: number;
  weeklyRestDays: number;
  publicHolidayDays: number;
  warnings: { code: string; message: string }[];
}

export interface LeaveRequestPreviewInput {
  leaveTypeId: string;
  start: string;
  end: string;
  startTime?: string;
  endTime?: string;
  halfDayPeriod?: LeaveHalfDayPeriod;
}

/** `LeaveRequestAttachmentDto` (`LeaveRequestAttachments.cs:12`): one file linked to a request. */
export interface LeaveRequestAttachment {
  documentId: string;
  fileName: string;
  attachedAt: string;
}

/**
 * `LeaveRequestDetailDto` (`GET /leave/employees/{employeeId}/requests/{id}` and
 * `GET /leave/me/requests/{id}`) - A1/senseik-mobile-011: only the field mobile needs
 * (`attachments`); the rest of the (much larger) DTO isn't modeled here.
 */
export interface LeaveRequestDetail {
  attachments?: LeaveRequestAttachment[] | null;
}

/**
 * `download-url` response (senseik-mobile-011): a 15-minute presigned link to the attachment's
 * bytes. Never cache this - fetch it fresh on every "open" tap and never log/persist `url` (it
 * carries a signature), same rule as `DocumentService.ts`'s upload presigned URL.
 */
export interface LeaveAttachmentDownloadUrl {
  url: string;
  expiresAt: string;
  fileName: string;
  contentType: string;
}

export class LeaveService {
  async getMyBalances(): Promise<EmployeeLeaveBalances> {
    const response = await apiClient.get<EmployeeLeaveBalances>(`${LEAVE_BASE}/me/balances`);
    return {
      ...response.data,
      balances: response.data.balances.map((item) => ({
        ...item,
        leaveTypeName: leaveTypeDisplayName(item.leaveTypeName),
      })),
    };
  }

  async getMyRequests(): Promise<LeaveRequest[]> {
    const response = await apiClient.get<LeaveRequest[]>(`${LEAVE_BASE}/me/requests`);
    return response.data;
  }

  async getMyRequestableLeaveTypes(): Promise<RequestableLeaveType[]> {
    const response = await apiClient.get<RequestableLeaveType[]>(`${LEAVE_BASE}/me/leave-types`);
    return response.data.map((type) => ({ ...type, name: leaveTypeDisplayName(type.name) }));
  }

  async previewMyLeaveRequest(input: LeaveRequestPreviewInput): Promise<LeaveRequestPreview> {
    const response = await apiClient.get<LeaveRequestPreview>(`${LEAVE_BASE}/me/requests/preview`, {
      params: input,
    });
    return response.data;
  }

  async submitLeaveRequest(input: MyLeaveRequestInput): Promise<LeaveRequest> {
    const response = await apiClient.post<LeaveRequest>(`${LEAVE_BASE}/me/requests`, input);
    return response.data;
  }

  async withdrawLeaveRequest(id: string): Promise<void> {
    await apiClient.post(`${LEAVE_BASE}/me/requests/${id}/withdraw`);
  }

  /** A1 (onaycı): Manager-scoped detail read, used only to surface a leave task's `attachments`. */
  async getEmployeeLeaveRequest(
    employeeId: string,
    requestId: string
  ): Promise<LeaveRequestDetail> {
    const response = await apiClient.get<LeaveRequestDetail>(
      `${LEAVE_BASE}/employees/${employeeId}/requests/${requestId}`
    );
    return response.data;
  }

  /** senseik-mobile-011: self-service detail read, used only to lazily surface a request's `attachments`. */
  async getMyLeaveRequest(requestId: string): Promise<LeaveRequestDetail> {
    const response = await apiClient.get<LeaveRequestDetail>(
      `${LEAVE_BASE}/me/requests/${requestId}`
    );
    return response.data;
  }

  /** senseik-mobile-011: onaycı/İK - manager-scoped presigned download URL for one attachment. */
  async getEmployeeAttachmentDownloadUrl(
    employeeId: string,
    requestId: string,
    documentId: string
  ): Promise<LeaveAttachmentDownloadUrl> {
    const response = await apiClient.get<LeaveAttachmentDownloadUrl>(
      `${LEAVE_BASE}/employees/${employeeId}/requests/${requestId}/attachments/${documentId}/download-url`
    );
    return response.data;
  }

  /** senseik-mobile-011: talep sahibi - self-service presigned download URL for one attachment. */
  async getMyAttachmentDownloadUrl(
    requestId: string,
    documentId: string
  ): Promise<LeaveAttachmentDownloadUrl> {
    const response = await apiClient.get<LeaveAttachmentDownloadUrl>(
      `${LEAVE_BASE}/me/requests/${requestId}/attachments/${documentId}/download-url`
    );
    return response.data;
  }
}

export const leaveService = new LeaveService();
