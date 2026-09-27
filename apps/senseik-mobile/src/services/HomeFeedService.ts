import { apiClient } from "@/lib/apiClient";
import { dateOnlyToDate } from "@/lib/formatters";

/**
 * Home screen feed (spec §4): the four "who/what is coming up" sections below the balances and
 * action grid. Each endpoint is read-only and permission-scoped server-side - a 403 or an empty list
 * simply hides that section (see `homeSections.sectionFrom`), so nothing here retries or maps errors.
 * The `select*` functions are pure so the section rules are unit-tested without the network.
 */

/** `LeaveCalendarEntryDto` (`LeaveCalendarFeatures.cs`); `leaveTypeName` is localized server-side. */
export interface LeaveCalendarEntry {
  requestId: string;
  employeeId: string;
  employeeName: string;
  startDate: string;
  endDate: string;
  calculatedDays: number;
  status: "pending" | "approved";
  isMasked: boolean;
  leaveTypeCode?: string | null;
  leaveTypeName?: string | null;
  overlappingApprovedCount?: number;
}

/** `UpcomingBirthdayDto` (`EmployeeDtos.cs`); `id` is the employee id. */
export interface UpcomingBirthday {
  id: string;
  displayName: string;
  orgUnitId?: string | null;
  nextBirthday: string;
}

/** `CompanyCalendarEntryDto` (`CompanyEventDtos.cs`); derived birthday/leave rows carry no `id`. */
export interface CompanyCalendarEntry {
  id?: string | null;
  title: string;
  description?: string | null;
  date: string;
  type: "companyEvent" | "meeting" | "birthday" | "leaveStart";
  employeeId?: string | null;
}

/** `AnnouncementFeedItemDto` (`AnnouncementFeed.cs`); `content` is stored as rich-text HTML. */
export interface AnnouncementFeedItem {
  id: string;
  title: string;
  content: string;
  type: string;
  priority: string;
  publishedAt?: string | null;
  expiresAt?: string | null;
  isRead: boolean;
  kind: string;
  senderEmployeeId?: string | null;
}

/** One row of the "Bugün izinde olanlar" card. */
export interface PersonOff {
  employeeId: string;
  name: string;
  typeLabel: string;
  days: number;
  startDate: string;
  endDate: string;
  isRemote: boolean;
}

const MAX_PEOPLE_OFF = 5;
const MAX_BIRTHDAYS = 3;
const MAX_EVENTS = 3;
const MAX_ANNOUNCEMENTS = 2;
const DEFAULT_BIRTHDAY_DAYS = 30;
const DAY_MS = 24 * 60 * 60 * 1000;

const MASKED_LEAVE_LABEL = "İzinli";
const REMOTE_WORK_LABEL = "Uzaktan çalışıyor";

/**
 * Approved leave of other employees, one row per person (first entry wins), at most `max`.
 * A masked entry (caller may not see the type) never reveals it - it reads "İzinli".
 */
export function selectPeopleOffToday(
  entries: LeaveCalendarEntry[],
  selfEmployeeId: string | undefined,
  max: number = MAX_PEOPLE_OFF
): PersonOff[] {
  const seen = new Set<string>();
  const people: PersonOff[] = [];
  for (const entry of entries) {
    if (people.length >= max) break;
    if (entry.status !== "approved" || entry.employeeId === selfEmployeeId) continue;
    if (seen.has(entry.employeeId)) continue;
    seen.add(entry.employeeId);

    const isRemote = !entry.isMasked && entry.leaveTypeCode === "remoteWork";
    const typeLabel = entry.isMasked
      ? MASKED_LEAVE_LABEL
      : isRemote
        ? REMOTE_WORK_LABEL
        : (entry.leaveTypeName ?? MASKED_LEAVE_LABEL);
    people.push({
      employeeId: entry.employeeId,
      name: entry.employeeName,
      typeLabel,
      days: entry.calculatedDays,
      startDate: entry.startDate,
      endDate: entry.endDate,
      isRemote,
    });
  }
  return people;
}

/** Whole calendar days from `from` to `to` (both "yyyy-MM-dd"); rounding absorbs DST-shortened days. */
function daysBetween(from: string, to: string): number {
  return Math.round((dateOnlyToDate(to).getTime() - dateOnlyToDate(from).getTime()) / DAY_MS);
}

/** Soonest birthdays first, at most `max`, each with its day count from `today` ("yyyy-MM-dd"). */
export function selectUpcomingBirthdays(
  items: UpcomingBirthday[],
  today: string,
  max: number = MAX_BIRTHDAYS
): Array<UpcomingBirthday & { daysLeft: number }> {
  return [...items]
    .sort((a, b) => a.nextBirthday.localeCompare(b.nextBirthday))
    .slice(0, max)
    .map((item) => ({ ...item, daysLeft: daysBetween(today, item.nextBirthday) }));
}

/** Only real company events and meetings (birthdays/leave starts have their own sections), soonest first. */
export function selectUpcomingEvents(
  entries: CompanyCalendarEntry[],
  max: number = MAX_EVENTS
): CompanyCalendarEntry[] {
  return entries
    .filter((entry) => entry.type === "companyEvent" || entry.type === "meeting")
    .sort((a, b) => a.date.localeCompare(b.date))
    .slice(0, max);
}

/** Announcements only (recognitions etc. dropped; `kind` casing varies by API build), newest first. */
export function selectAnnouncements(
  items: AnnouncementFeedItem[],
  max: number = MAX_ANNOUNCEMENTS
): AnnouncementFeedItem[] {
  return items
    .filter((item) => item.kind.toLowerCase() === "announcement")
    .sort((a, b) => (b.publishedAt ?? "").localeCompare(a.publishedAt ?? ""))
    .slice(0, max);
}

/** Annual leave first, the rest in server order (stable; returns a new array). */
export function sortBalancesAnnualFirst<T extends { leaveTypeCode: string }>(items: T[]): T[] {
  return [
    ...items.filter((item) => item.leaveTypeCode === "annual"),
    ...items.filter((item) => item.leaveTypeCode !== "annual"),
  ];
}

export class HomeFeedService {
  async getLeaveCalendar(from: string, to: string): Promise<LeaveCalendarEntry[]> {
    const response = await apiClient.get<LeaveCalendarEntry[]>("/api/v1/leave/calendar", {
      params: { from, to },
    });
    return response.data;
  }

  async getUpcomingBirthdays(days: number = DEFAULT_BIRTHDAY_DAYS): Promise<UpcomingBirthday[]> {
    const response = await apiClient.get<UpcomingBirthday[]>(
      "/api/v1/employees/upcoming-birthdays",
      { params: { days } }
    );
    return response.data;
  }

  async getCompanyCalendar(from: string, to: string): Promise<CompanyCalendarEntry[]> {
    const response = await apiClient.get<CompanyCalendarEntry[]>(
      "/api/v1/announcements/events/calendar",
      { params: { from, to } }
    );
    return response.data;
  }

  async getAnnouncementFeed(): Promise<AnnouncementFeedItem[]> {
    const response = await apiClient.get<AnnouncementFeedItem[]>("/api/v1/announcements/feed");
    return response.data;
  }
}

export const homeFeedService = new HomeFeedService();
