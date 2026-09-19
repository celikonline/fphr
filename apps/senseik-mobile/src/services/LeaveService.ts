import { apiClient } from "@/lib/apiClient";

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
  | "remoteWork"
  | "birthday";

export type LeaveUnit = "fullDay" | "halfDay" | "hourly";
export type LeaveRequestStatus = "pending" | "approved" | "rejected" | "withdrawn" | "cancelled";

/** `LeaveBalance` projection of one PerYear type (days). */
export interface LeaveBalance {
  leaveTypeId: string;
  currentPeriodFrom: string;
  currentPeriodTo: string;
  nextAccrualDate: string;
  totalAccrued: number;
  totalUsed: number;
  currentBalance: number;
  availableBalance: number;
  negativeBalanceLimit?: number | null;
}

/** `LeaveBalanceDto`: `leaveTypeName` may be a raw resource key for seeded types; `getMyBalances` resolves it (`leaveTypeDisplayName`). */
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
}

/** Self-service submit body (`LeaveRequestBody`); `previousRequestId` resubmits a rejected own request. */
export interface MyLeaveRequestInput {
  leaveTypeId: string;
  startDate: string;
  endDate: string;
  description: string | null;
  previousRequestId?: string | null;
}

/**
 * Turkish names of the seeded leave-type resource keys (mirror of `leave.type.*` in SharedResource.resx; the mobile
 * app has no i18n and is Turkish-only). The leave API returns these keys raw on `/me/balances` and `/me/leave-types`
 * (the web resolves them with its own i18n), so the service resolves them before the screens render them.
 */
const LEAVE_TYPE_NAMES: Readonly<Record<string, string>> = {
  "leave.type.annual": "Yıllık izin",
  "leave.type.excuse": "Mazeret izni",
  "leave.type.sick": "Hastalık izni",
  "leave.type.job_search": "Yeni iş arama izni",
  "leave.type.marriage": "Evlilik izni",
  "leave.type.bereavement": "Ölüm izni",
  "leave.type.paternity": "Babalık izni",
  "leave.type.maternity": "Doğum izni",
  "leave.type.postnatal_unpaid": "Doğum sonrası ücretsiz izin",
  "leave.type.paid_military_service": "Bedelli askerlik izni",
  "leave.type.unpaid": "Ücretsiz izin",
  "leave.type.foster_care": "Koruyucu aile izni",
  "leave.type.template.remote_work": "Uzaktan Çalışma",
  "leave.type.template.birthday": "Doğum günü izni",
};

/** Resolves a seeded leave-type resource key to its Turkish name; free-form (user-entered) names pass through. */
export function leaveTypeDisplayName(name: string): string {
  return LEAVE_TYPE_NAMES[name] ?? name;
}

export class LeaveService {
  async getMyBalances(): Promise<EmployeeLeaveBalances> {
    const response = await apiClient.get<EmployeeLeaveBalances>(`${LEAVE_BASE}/me/balances`);
    return {
      ...response.data,
      balances: response.data.balances.map((item) => ({ ...item, leaveTypeName: leaveTypeDisplayName(item.leaveTypeName) })),
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

  async submitLeaveRequest(input: MyLeaveRequestInput): Promise<LeaveRequest> {
    const response = await apiClient.post<LeaveRequest>(`${LEAVE_BASE}/me/requests`, input);
    return response.data;
  }

  async withdrawLeaveRequest(id: string): Promise<void> {
    await apiClient.post(`${LEAVE_BASE}/me/requests/${id}/withdraw`);
  }
}

export const leaveService = new LeaveService();
