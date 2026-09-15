/**
 * Invitations (Faz 0 item 10).
 * - Public accept flow: `GET /v1/identity/invitations/preview/{token}`, `POST /v1/identity/invitations/accept` (anonymous).
 * - Employee invite: `POST /v1/identity/invitations/employee` and bulk `POST /v1/identity/invitations/employees/bulk`
 *   (`identity.users.invite`); used by the employees screens.
 */
import { apiClient } from "@senseik/api-client";
import type { BulkOperationResult, EmployeeSelection } from "@/types";

const INVITATIONS_BASE = "/v1/identity/invitations";

/** Mirrors `InvitationPreviewDto`; `isUsable` is false for expired/used/revoked invitations. */
export interface InvitationPreview {
  email: string;
  firstName: string;
  lastName: string;
  tenantName: string;
  tenantSlug: string;
  expiresAt: string;
  isUsable: boolean;
}

export interface AcceptInvitationInput {
  token: string;
  password: string;
  privacyNoticeAccepted: boolean;
}

export type EmployeeInvitationOutcome = "Sent" | "Resent" | "AlreadyActive";

export type InvitationEmailSource = "Work" | "Personal" | "Account";

/** Mirrors `EmployeeInvitationResultDto`. */
export interface EmployeeInvitationResult {
  employeeId: string;
  outcome: EmployeeInvitationOutcome;
  sentToEmail: string | null;
  emailSource: InvitationEmailSource | null;
}

export async function getInvitationPreview(token: string): Promise<InvitationPreview> {
  const { data } = await apiClient.get<InvitationPreview>(`${INVITATIONS_BASE}/preview/${encodeURIComponent(token)}`);
  return data;
}

/** Returns auth tokens, but the web flow sends the user to /login instead of auto-signing in. */
export async function acceptInvitation(input: AcceptInvitationInput): Promise<void> {
  await apiClient.post(`${INVITATIONS_BASE}/accept`, input);
}

export async function inviteEmployee(employeeId: string): Promise<EmployeeInvitationResult> {
  const { data } = await apiClient.post<EmployeeInvitationResult>(`${INVITATIONS_BASE}/employee`, { employeeId });
  return data;
}

export async function bulkInviteEmployees(selection: EmployeeSelection): Promise<BulkOperationResult> {
  const { data } = await apiClient.post<BulkOperationResult>(`${INVITATIONS_BASE}/employees/bulk`, { selection });
  return data;
}
