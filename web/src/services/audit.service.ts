/**
 * Audit Log Service
 *
 * Screen 6 (Item 20): Timestamp / Actor / Action / Resource / Details, with date-range,
 * actor and action-type filters, pagination and CSV export.
 *
 * Wired to the real backend endpoint (Faz 1 BE twin, sözleşme S-1 in status/faz-01/TALIMAT.md /
 * status/PANO.md, "hizalı" with this file's previous mock shape):
 *   GET /v1/identity/audit-log?from&to&actor&action&page&pageSize -> PagedResult<AuditLogEntryDto>
 * Gated by `identity.audit.view`; `details` comes back masked from the server when the caller lacks
 * `identity.audit.sensitive_view` (see IdentityPermissions.cs) - nothing to do here, we just render
 * whatever string the server sends.
 *
 * Two gaps versus the mock this replaces, both flagged in rapor-fe-20260916-1613.md for the BE twin/PO:
 * 1. The documented query string has no free-text `q` param and no `sort` param. Both are still sent
 *    (harmless if the backend ignores unknown query keys) so the search box and the timestamp column's
 *    sort toggle keep working the moment the backend adds support; until then they may be no-ops against
 *    a real API (the endpoint isn't in main yet as of this turn, so this could not be verified live).
 * 2. There is no actors-lookup endpoint in the contract (`actor` is `string`, not a closed enum), so
 *    `pages/audit-log.tsx` no longer offers a dropdown of known actors - see that file for the resulting
 *    (unavoidable) small filter-control change.
 */
import { apiClient } from "@senseik/api-client";
import type { PagedResult, PagedQueryParams } from "@/types";

export type AuditActionType = "create" | "update" | "delete" | "login" | "invite" | "role_change";

export interface AuditEvent {
  id: string;
  occurredAt: string;
  actor: string;
  action: AuditActionType;
  resource: string;
  /** Present on the real DTO (`AuditLogEntryDto.resourceId`); the grid doesn't render it (yet). */
  resourceId?: string;
  details: string;
}

const AUDIT_LOG_BASE = "/v1/identity/audit-log";

const ACTIONS: AuditActionType[] = ["create", "update", "delete", "login", "invite", "role_change"];

export interface AuditLogFilters {
  actor?: string;
  action?: AuditActionType;
  from?: string;
  to?: string;
}

export interface ListAuditEventsQuery extends AuditLogFilters, PagedQueryParams {}

export async function listAuditEvents(query: ListAuditEventsQuery = {}): Promise<PagedResult<AuditEvent>> {
  const { data } = await apiClient.get<PagedResult<AuditEvent>>(AUDIT_LOG_BASE, { params: query });
  return data;
}

/** The action taxonomy is a fixed backend enum (see BE sözleşme), not something to fetch. */
export function listAuditActionTypes(): AuditActionType[] {
  return ACTIONS;
}

const EXPORT_PAGE_SIZE = 100;
const EXPORT_MAX_PAGES = 50;

/**
 * Walks every page matching the given filters (pageSize=100, up to 50 pages = 5000 rows) and builds
 * a CSV blob - the full filtered result set, not just the page currently rendered on screen.
 */
export async function exportAuditEventsCsv(filters: AuditLogFilters = {}): Promise<Blob> {
  const rows = ["Timestamp,Actor,Action,Resource,Details"];
  for (let page = 1; page <= EXPORT_MAX_PAGES; page += 1) {
    const result = await listAuditEvents({ ...filters, page, pageSize: EXPORT_PAGE_SIZE });
    for (const e of result.items) {
      rows.push(`${e.occurredAt},${e.actor},${e.action},${e.resource},"${e.details.replace(/"/g, '""')}"`);
    }
    if (!result.hasNext || result.items.length === 0) break;
  }
  return new Blob([rows.join("\n")], { type: "text/csv;charset=utf-8;" });
}
