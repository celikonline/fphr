/**
 * Platform (süper admin) service: şirket (kiracı) listesi ve yeni şirket oluşturma.
 *
 * Mirrors the BE `Sense.Modules.Identity.Api.Controllers.TenantsController` (GET/POST /v1/identity/tenants).
 * Yalnız platform kiracısındaki süper admin (`platform.tenants.manage` + IsPlatformStaff) kullanabilir.
 */
import { apiClient } from "@senseik/api-client";

const TENANTS = "/v1/identity/tenants";

export const PLATFORM_PERMISSIONS = {
  tenantsManage: "platform.tenants.manage",
} as const;

export type TenantStatus = "trial" | "active" | "suspended" | "closed";

/** `invite`: Owner davet e-postasıyla parolasını kendisi belirler; `password`: verilen parolayla doğrudan etkin açılır. */
export type TenantOwnerMode = "invite" | "password";

export interface TenantSummary {
  id: string;
  slug: string;
  name: string;
  status: TenantStatus;
  /** Omitted by the API when there is no trial (WhenWritingNull). */
  trialEndsAt?: string;
  createdAt: string;
  /** The platform tenant itself (not a customer). */
  isPlatform: boolean;
}

export interface CreateTenantInput {
  companyName: string;
  slug: string;
  ownerEmail: string;
  ownerFirstName: string;
  ownerLastName: string;
  ownerMode: TenantOwnerMode;
  /** Required when `ownerMode` is `password`. */
  password?: string;
  phone?: string;
  language?: string;
}

export interface CreateTenantResult {
  tenantId: string;
  slug: string;
  ownerUserId: string;
  invitationSent: boolean;
}

export async function listTenants(): Promise<TenantSummary[]> {
  const { data } = await apiClient.get<TenantSummary[]>(TENANTS);
  return data;
}

export async function createTenant(input: CreateTenantInput): Promise<CreateTenantResult> {
  const { data } = await apiClient.post<CreateTenantResult>(TENANTS, input);
  return data;
}
