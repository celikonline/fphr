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
  /**
   * Version of the privacy notice text the user accepted. Faz 1's `IConsentRecorder` (S-3, tur 3) will persist
   * this once wired up server-side; until then `AcceptInvitationCommand` only carries it through. Required by
   * the validator whenever `privacyNoticeAccepted` is true (it always is — the checkbox is a hard requirement),
   * so this must always be sent.
   *
   * The value itself is product-wide fixed (PANO "KVKK versiyonu" kararı, gün açılışı 2026-09-16: `"2026-09"`
   * across every module, same value Faz 9 uses) and lives in i18n (`auth:privacy.noticeVersion`) instead of a
   * TS constant, since it travels with the localized notice text it stamps.
   */
  privacyNoticeVersion: string;
}

export type EmployeeInvitationOutcome = "Sent" | "Resent" | "AlreadyActive" | "NotRequested";

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

/**
 * `sendCredentials` defaults to true server-side; false validates the employee but creates no user/invitation/e-mail
 * and returns `outcome: "NotRequested"`.
 */
export async function inviteEmployee(employeeId: string, options?: { sendCredentials?: boolean }): Promise<EmployeeInvitationResult> {
  const { data } = await apiClient.post<EmployeeInvitationResult>(`${INVITATIONS_BASE}/employee`, {
    employeeId,
    ...(options?.sendCredentials === undefined ? {} : { sendCredentials: options.sendCredentials }),
  });
  return data;
}

export async function bulkInviteEmployees(selection: EmployeeSelection): Promise<BulkOperationResult> {
  const { data } = await apiClient.post<BulkOperationResult>(`${INVITATIONS_BASE}/employees/bulk`, { selection });
  return data;
}
