/**
 * Support access / platform impersonation grants (Faz 0 item 11).
 * Tenant Owner/Admin (`identity.impersonation.approve`) lists, approves and revokes time-limited grants
 * requested by Sense platform staff (ImpersonationController).
 */
import { apiClient } from "@senseik/api-client";

/** Mirrors the domain `ImpersonationGrantStatus` enum (serialized as string). */
export type ImpersonationGrantStatus = "Pending" | "Approved" | "Active" | "Expired" | "Revoked";

export const IMPERSONATION_GRANT_STATUSES: ImpersonationGrantStatus[] = ["Pending", "Approved", "Active", "Expired", "Revoked"];

/** Mirrors `ImpersonationGrantDto` per the Faz 0 P2 contract. */
export interface ImpersonationGrant {
  id: string;
  platformUserId: string;
  platformUserEmail: string | null;
  reason: string;
  status: ImpersonationGrantStatus;
  requestedAt: string;
  approvedAt: string | null;
  approvedByUserId: string | null;
  expiresAt: string | null;
  startedAt: string | null;
  revokedAt: string | null;
}

const IMPERSONATION_BASE = "/v1/identity/impersonation";

export async function listImpersonationGrants(status?: string): Promise<ImpersonationGrant[]> {
  const { data } = await apiClient.get<ImpersonationGrant[]>(IMPERSONATION_BASE, {
    params: status ? { status } : undefined,
  });
  return data;
}

export async function approveImpersonationGrant(id: string): Promise<void> {
  await apiClient.post(`${IMPERSONATION_BASE}/${id}/approve`);
}

export async function revokeImpersonationGrant(id: string): Promise<void> {
  await apiClient.post(`${IMPERSONATION_BASE}/${id}/revoke`);
}
