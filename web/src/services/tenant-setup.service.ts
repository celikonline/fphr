/**
 * Tenant setup service: first-run setup wizard (Owner) + own-company profile.
 *
 * Mirrors the BE `CurrentTenantController` (GET/PUT /v1/identity/tenants/current, POST .../setup/complete),
 * `OrganizationBootstrapController` (POST /v1/organization/bootstrap) and `UsersController.Create`.
 */
import { apiClient } from "@senseik/api-client";

const CURRENT_TENANT = "/v1/identity/tenants/current";
const ORGANIZATION_BOOTSTRAP = "/v1/organization/bootstrap";
const USERS = "/v1/identity/users";

/** Same list the BE validator accepts (Identity `SupportedTenantLocales`). */
export const SUPPORTED_TENANT_LOCALES = ["tr-TR", "en-US", "de-DE"] as const;
export type TenantLocale = (typeof SUPPORTED_TENANT_LOCALES)[number];

/** Permission that owns the setup wizard (Owner/Admin hold it through the "*" wildcard). */
export const TENANT_MANAGE_PERMISSION = "identity.tenant.manage";

export interface TenantProfile {
  id: string;
  slug: string;
  name: string;
  /** Omitted by the API when unset (WhenWritingNull). */
  taxNumber?: string;
  status: string;
  trialEndsAt?: string;
  locale: string;
  timeZone: string;
  setupCompleted: boolean;
}

export interface UpdateTenantProfileInput {
  name: string;
  taxNumber?: string;
  locale: string;
  timeZone: string;
}

export async function getTenantProfile(): Promise<TenantProfile> {
  const { data } = await apiClient.get<TenantProfile>(CURRENT_TENANT);
  return data;
}

export async function updateTenantProfile(input: UpdateTenantProfileInput): Promise<TenantProfile> {
  const { data } = await apiClient.put<TenantProfile>(CURRENT_TENANT, input);
  return data;
}

/** Marks the first-run setup as done (idempotent). */
export async function completeTenantSetup(): Promise<void> {
  await apiClient.post(`${CURRENT_TENANT}/setup/complete`);
}

/**
 * Makes sure the tenant has its Organization defaults (unit types, root company, TR holiday calendar). Idempotent; the
 * Worker normally does this from the TenantProvisioned event, this call just doesn't wait for it.
 */
export async function ensureOrganization(companyName: string): Promise<void> {
  await apiClient.post(ORGANIZATION_BOOTSTRAP, { companyName });
}

export interface InviteUserInput {
  email: string;
  firstName: string;
  lastName: string;
  roleIds: string[];
  language?: string;
}

/** Creates a user with the given roles and mails them an invitation to set their password. */
export async function inviteUser(input: InviteUserInput): Promise<string> {
  const { data } = await apiClient.post<string>(USERS, { ...input, sendInvitation: true });
  return data;
}
