/**
 * Form model for the company event panel (E4). Pure functions: validation and the request payload builder.
 * B14's PUT is a full replacement, so `buildEventPayload` always emits every field (nulls explicit).
 */
import { parseDateOnly, toDateOnly } from "@/lib/dates";
import { listEmployees } from "@/services/employees.service";
import type {
  CompanyCalendarEntry,
  CompanyEventMode,
  CreateCompanyEventInput,
} from "@/services/company-calendar.service";
import type { EmployeeSelection } from "@/types";

export type ManualEventType = Extract<CompanyCalendarEntry["type"], "companyEvent" | "meeting">;

export interface EventFormValues {
  title: string;
  description: string;
  type: ManualEventType;
  visibilityOrgUnitId: string | null;
  categoryId: string | null;
  mode: CompanyEventMode | null;
  location: string;
  onlineUrl: string;
  isAllDay: boolean;
  startDate: string;
  startTime: string;
  endDate: string;
  endTime: string;
  inviteeEmployeeIds: string[];
  notifyInvitees: boolean;
}

export type EventFormError = "titleRequired" | "locationRequired" | "onlineUrlRequired" | "endsBeforeStarts";

const DEFAULT_START_TIME = "09:00";
const DEFAULT_END_TIME = "10:00";

export function emptyEventForm(today = toDateOnly(new Date())): EventFormValues {
  return {
    title: "",
    description: "",
    type: "companyEvent",
    visibilityOrgUnitId: null,
    categoryId: null,
    mode: null,
    location: "",
    onlineUrl: "",
    isAllDay: true,
    startDate: today,
    startTime: DEFAULT_START_TIME,
    endDate: today,
    endTime: DEFAULT_END_TIME,
    inviteeEmployeeIds: [],
    notifyInvitees: false,
  };
}

function pad(n: number): string {
  return String(n).padStart(2, "0");
}

/** Prefills the form from a calendar entry. All-day instants are midnight UTC; timed ones are shown in local time. */
export function eventFormFromEntry(entry: CompanyCalendarEntry): EventFormValues {
  const base = emptyEventForm(entry.date);
  const allDay = entry.isAllDay ?? !entry.startsAt;
  const type: ManualEventType = entry.type === "meeting" ? "meeting" : "companyEvent";
  const common = {
    ...base,
    title: entry.title,
    description: entry.description ?? "",
    type,
    visibilityOrgUnitId: entry.visibilityOrgUnitId ?? null,
    categoryId: entry.categoryId ?? null,
    mode: entry.mode ?? null,
    location: entry.location ?? "",
    onlineUrl: entry.onlineUrl ?? "",
    inviteeEmployeeIds: entry.inviteeEmployeeIds ?? [],
    isAllDay: allDay,
  };
  if (!entry.startsAt) return common;
  const starts = new Date(entry.startsAt);
  const ends = entry.endsAt ? new Date(entry.endsAt) : starts;
  if (allDay) {
    return { ...common, startDate: entry.startsAt.slice(0, 10), endDate: (entry.endsAt ?? entry.startsAt).slice(0, 10) };
  }
  return {
    ...common,
    startDate: toDateOnly(starts),
    startTime: `${pad(starts.getHours())}:${pad(starts.getMinutes())}`,
    endDate: toDateOnly(ends),
    endTime: `${pad(ends.getHours())}:${pad(ends.getMinutes())}`,
  };
}

/** Instant for the API: all-day → midnight UTC of the calendar date; timed → the local wall-clock time as UTC ISO. */
function toInstant(date: string, time: string, allDay: boolean): string {
  if (allDay) return `${date}T00:00:00.000Z`;
  const local = parseDateOnly(date);
  const [h, m] = time.split(":").map(Number);
  local.setHours(h || 0, m || 0, 0, 0);
  return local.toISOString();
}

export function validateEventForm(values: EventFormValues): EventFormError[] {
  const errors: EventFormError[] = [];
  if (!values.title.trim()) errors.push("titleRequired");
  if (values.mode === "onsite" && !values.location.trim()) errors.push("locationRequired");
  if (values.mode === "online" && !/^https?:\/\/\S+$/i.test(values.onlineUrl.trim())) errors.push("onlineUrlRequired");
  const starts = toInstant(values.startDate, values.startTime, values.isAllDay);
  const ends = toInstant(values.endDate, values.endTime, values.isAllDay);
  if (ends < starts) errors.push("endsBeforeStarts");
  return errors;
}

export function buildEventPayload(values: EventFormValues): CreateCompanyEventInput {
  return {
    title: values.title.trim(),
    description: values.description.trim() || null,
    eventDate: values.startDate,
    type: values.type,
    visibilityOrgUnitId: values.visibilityOrgUnitId || null,
    startsAt: toInstant(values.startDate, values.startTime, values.isAllDay),
    endsAt: toInstant(values.endDate, values.endTime, values.isAllDay),
    isAllDay: values.isAllDay,
    mode: values.mode,
    location: values.mode === "onsite" ? values.location.trim() : null,
    onlineUrl: values.mode === "online" ? values.onlineUrl.trim() : null,
    notifyInvitees: values.notifyInvitees,
    categoryId: values.categoryId,
    inviteeEmployeeIds: values.inviteeEmployeeIds,
  };
}

const RESOLVE_PAGE_SIZE = 200;

/** Backend `CompanyEventLimits.MaxInvitees`. */
export const MAX_INVITEES = 500;

export class InviteeLimitError extends Error {
  constructor(readonly count: number) {
    super(`invitee limit exceeded: ${count}`);
  }
}

export class EmptyInviteeSelectionError extends Error {
  constructor() {
    super("selection resolved to no employees");
  }
}

/**
 * Resolves the multi-picker result to explicit employee ids (filter selections are expanded page by page).
 * Throws {@link EmptyInviteeSelectionError} for an empty result and {@link InviteeLimitError} above {@link MAX_INVITEES}.
 */
export async function resolveSelectionToIds(selection: EmployeeSelection): Promise<string[]> {
  const ids = await collectIds(selection);
  if (ids.length === 0) throw new EmptyInviteeSelectionError();
  if (ids.length > MAX_INVITEES) throw new InviteeLimitError(ids.length);
  return ids;
}

async function collectIds(selection: EmployeeSelection): Promise<string[]> {
  if (selection.employeeIds) return [...selection.employeeIds];
  const excluded = new Set(selection.excludedEmployeeIds ?? []);
  const ids: string[] = [];
  for (let page = 1; ; page++) {
    const result = await listEmployees({
      ...(selection.filter ?? {}),
      q: selection.filter?.q ?? undefined,
      page,
      pageSize: RESOLVE_PAGE_SIZE,
      sort: "displayName",
    });
    for (const item of result.items) if (!excluded.has(item.id)) ids.push(item.id);
    if (result.items.length === 0 || page * RESOLVE_PAGE_SIZE >= result.totalCount) return ids;
  }
}

/**
 * Local (browser) calendar days an entry occupies. Timed events follow `startsAt`..`endsAt` in local time (the
 * backend derives `date` from the UTC day, which can be off by one); all-day and derived entries stay on `date`.
 */
export function entryDays(entry: CompanyCalendarEntry): string[] {
  if (entry.isAllDay !== false || !entry.startsAt) return [entry.date];
  const first = new Date(entry.startsAt);
  const last = entry.endsAt ? new Date(entry.endsAt) : first;
  const days: string[] = [];
  const cursor = new Date(first.getFullYear(), first.getMonth(), first.getDate());
  const end = new Date(last.getFullYear(), last.getMonth(), last.getDate());
  for (; cursor <= end && days.length < MAX_SPAN_DAYS; cursor.setDate(cursor.getDate() + 1)) days.push(toDateOnly(cursor));
  return days.length > 0 ? days : [toDateOnly(first)];
}

const MAX_SPAN_DAYS = 62;

/** Calendar entry time label ("09:00–10:00"); null for all-day and derived entries. */
export function eventTimeRange(entry: CompanyCalendarEntry, locale: string): string | null {
  if (entry.isAllDay !== false || !entry.startsAt) return null;
  const fmt = new Intl.DateTimeFormat(locale, { hour: "2-digit", minute: "2-digit" });
  const start = fmt.format(new Date(entry.startsAt));
  return entry.endsAt ? `${start}–${fmt.format(new Date(entry.endsAt))}` : start;
}
