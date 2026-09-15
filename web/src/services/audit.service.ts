/**
 * Audit Log Service
 *
 * Screen 6 (Item 20): Timestamp / Actor / Action / Resource / Details, with date-range,
 * actor and action-type filters, pagination and CSV export.
 *
 * TODO(item-20-backend): There is no human-facing audit log table or endpoint yet. What exists
 * (Items 15/16) is `AuditTenantInterceptor` (stamps CreatedAt/ModifiedDate/*UserId on IAuditable
 * entities) and the `outbox_messages` table (Sense.Shared.Infrastructure/Persistence/Outbox) which
 * captures domain events with `ActorUserId`/`Type`/`Payload`/`OccurredAt` for at-least-once dispatch -
 * it's a processing queue, not a queryable audit trail, and rows are pruned once processed. A real
 * implementation would project outbox events (or a dedicated append-only AuditLog table) behind
 * something like `GET /v1/identity/audit-log?from&to&actor&action&page&pageSize`, gated by the
 * already-defined `identity.audit.view` / `identity.audit.sensitive_view` permissions
 * (see IdentityPermissions.cs). This service mocks that endpoint against an in-memory seed so the
 * screen is fully usable end-to-end; swap `listAuditEvents`/`exportAuditEventsCsv` for real
 * `apiClient` calls once the backend exists.
 */
import { mockDelay } from "@/services/mock/local-store";
import { paginate, type ClientPageQuery } from "@/services/mock/paginate";
import type { PagedResult } from "@/types";

export type AuditActionType = "create" | "update" | "delete" | "login" | "invite" | "role_change";

export interface AuditEvent {
  id: string;
  occurredAt: string;
  actor: string;
  action: AuditActionType;
  resource: string;
  details: string;
}

const ACTORS = ["ozgur.celik@algosense.com.tr", "ayse.yilmaz@algosense.com.tr", "mehmet.kaya@algosense.com.tr", "system"];
const ACTIONS: AuditActionType[] = ["create", "update", "delete", "login", "invite", "role_change"];
const RESOURCES = ["User", "Role", "OrgUnit", "Location", "Position", "CompanyProfile", "Document"];

function seedEvents(): AuditEvent[] {
  const events: AuditEvent[] = [];
  const now = Date.now();
  for (let i = 0; i < 137; i += 1) {
    const action = ACTIONS[i % ACTIONS.length];
    const resource = RESOURCES[i % RESOURCES.length];
    const actor = ACTORS[i % ACTORS.length];
    events.push({
      id: `evt-${i}`,
      occurredAt: new Date(now - i * 45 * 60 * 1000).toISOString(),
      actor,
      action,
      resource,
      details: `${actor} performed ${action} on ${resource} #${1000 + i}`,
    });
  }
  return events;
}

let cachedEvents: AuditEvent[] | null = null;
function allEvents(): AuditEvent[] {
  if (!cachedEvents) cachedEvents = seedEvents();
  return cachedEvents;
}

export interface ListAuditEventsQuery extends ClientPageQuery {
  actor?: string;
  action?: AuditActionType;
  from?: string;
  to?: string;
}

export async function listAuditEvents(query: ListAuditEventsQuery = {}): Promise<PagedResult<AuditEvent>> {
  await mockDelay();
  let events = allEvents();

  if (query.actor) events = events.filter((e) => e.actor === query.actor);
  if (query.action) events = events.filter((e) => e.action === query.action);
  if (query.from) events = events.filter((e) => e.occurredAt >= query.from!);
  if (query.to) events = events.filter((e) => e.occurredAt <= query.to!);

  return paginate(
    events,
    { ...query, sort: query.sort ?? "-occurredAt" },
    (e) => `${e.actor} ${e.action} ${e.resource} ${e.details}`,
    (e) => ({ occurredAt: e.occurredAt })
  );
}

export function listAuditActors(): string[] {
  return ACTORS;
}

export function listAuditActionTypes(): AuditActionType[] {
  return ACTIONS;
}

/** Builds a CSV blob from the (already filtered) events currently shown on screen. */
export function exportAuditEventsToCsv(events: AuditEvent[]): Blob {
  const header = "Timestamp,Actor,Action,Resource,Details";
  const rows = events.map(
    (e) => `${e.occurredAt},${e.actor},${e.action},${e.resource},"${e.details.replace(/"/g, '""')}"`
  );
  return new Blob([[header, ...rows].join("\n")], { type: "text/csv;charset=utf-8;" });
}
