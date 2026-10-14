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
  operationsView: "platform.operations.view",
  jobsView: "platform.jobs.view",
  jobsTrigger: "platform.jobs.trigger",
  jobsManage: "platform.jobs.manage",
  backupsView: "platform.backups.view",
  backupsRun: "platform.backups.run",
  backupsRestore: "platform.backups.restore",
  maintenanceView: "platform.maintenance.view",
  maintenanceManage: "platform.maintenance.manage",
  diagnosticsView: "platform.diagnostics.view",
  auditView: "platform.audit.view",
  auditExport: "platform.audit.export",
} as const;

const PLATFORM_OPERATIONS = "/v1/identity/platform/operations";

export interface PlatformHealthComponent {
  name: string;
  status: "healthy" | "degraded" | "unhealthy";
  durationMs: number;
}

export interface PlatformOperationsOverview {
  status: "healthy" | "degraded" | "unhealthy";
  durationMs: number;
  components: PlatformHealthComponent[];
  jobs: {
    serverCount: number;
    enqueuedCount: number;
    scheduledCount: number;
    processingCount: number;
    failedCount: number;
  };
}

export interface PlatformRecurringJob {
  jobName: string;
  tenantId: string;
  cronExpression: string;
  lastState?: string;
  lastExecutionUtc?: string;
  nextExecutionUtc?: string;
}

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
  /** Omitted by the API when unset (WhenWritingNull). */
  taxNumber?: string;
  locale: string;
  timeZone: string;
}

export interface UpdateTenantInput {
  name: string;
  taxNumber?: string;
  locale: string;
  timeZone: string;
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
  /** Present when the owner invitation must be delivered manually (or is part of a commercial signup). */
  invitationUrl?: string | null;
}

export async function listTenants(): Promise<TenantSummary[]> {
  const { data } = await apiClient.get<TenantSummary[]>(TENANTS);
  return data;
}

export async function updateTenant(id: string, input: UpdateTenantInput): Promise<TenantSummary> {
  const { data } = await apiClient.put<TenantSummary>(`${TENANTS}/${id}`, input);
  return data;
}

export async function createTenant(input: CreateTenantInput): Promise<CreateTenantResult> {
  const { data } = await apiClient.post<CreateTenantResult>(TENANTS, input);
  return data;
}

export async function getPlatformOperationsOverview(): Promise<PlatformOperationsOverview> {
  const { data } = await apiClient.get<PlatformOperationsOverview>(`${PLATFORM_OPERATIONS}/overview`);
  return data;
}

export async function listPlatformRecurringJobs(skip = 0, take = 50): Promise<PlatformRecurringJob[]> {
  const { data } = await apiClient.get<PlatformRecurringJob[]>(`${PLATFORM_OPERATIONS}/jobs`, {
    params: { skip, take },
  });
  return data;
}
