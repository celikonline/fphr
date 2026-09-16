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

/** `LeaveBalanceDto`: `leaveTypeName` may be a resource key for seeded types, already resolved by the API. */
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

/** Self-service submit body (`LeaveRequestBody`); `previousRequestId` resubmits a rejected own request. */
export interface MyLeaveRequestInput {
  leaveTypeId: string;
  startDate: string;
  endDate: string;
  description: string | null;
  previousRequestId?: string | null;
}

export class LeaveService {
  async getMyBalances(): Promise<EmployeeLeaveBalances> {
    const response = await apiClient.get<EmployeeLeaveBalances>(`${LEAVE_BASE}/me/balances`);
    return response.data;
  }

  async getMyRequests(): Promise<LeaveRequest[]> {
    const response = await apiClient.get<LeaveRequest[]>(`${LEAVE_BASE}/me/requests`);
    return response.data;
  }

  async getMyRequestableLeaveTypes(): Promise<RequestableLeaveType[]> {
    const response = await apiClient.get<RequestableLeaveType[]>(`${LEAVE_BASE}/me/leave-types`);
    return response.data;
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
