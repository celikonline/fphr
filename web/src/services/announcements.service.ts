/**
 * Kurumsal duyurular: mirrors `AnnouncementsController.cs` (`api/v1/announcements`). The feed (`/feed`, `/{id}/read`)
 * is self-service (`announcements.my.feed`); list/create/publish/archive/read-count need `announcements.announcements.*`.
 */
import { apiClient } from "@senseik/api-client";

const BASE = "/v1/announcements";

export const ANNOUNCEMENT_TYPES = ["general", "hr", "policy", "emergency"] as const;
export const ANNOUNCEMENT_PRIORITIES = ["low", "normal", "high", "urgent"] as const;

export type AnnouncementType = (typeof ANNOUNCEMENT_TYPES)[number];
export type AnnouncementPriority = (typeof ANNOUNCEMENT_PRIORITIES)[number];
export type AnnouncementStatus = "draft" | "published" | "expired" | "archived";

/**
 * The DTOs carry C# `Enum.ToString()` ("HR", "Published") rather than the camelCase enum converter output; every
 * value is a single word, so lower-casing yields the i18n key / union member.
 */
export function enumKey<T extends string>(value: string): T {
  return value.toLowerCase() as T;
}

/** `AnnouncementFeedItemDto`. */
export interface AnnouncementFeedItem {
  id: string;
  title: string;
  content: string;
  type: string;
  priority: string;
  publishedAt?: string | null;
  expiresAt?: string | null;
  isRead: boolean;
}

/** `AnnouncementDto` (management list). */
export interface Announcement {
  id: string;
  title: string;
  content: string;
  type: string;
  priority: string;
  status: string;
  publishedBy: string;
  publishedAt?: string | null;
  expiresAt?: string | null;
  targetAudience?: string | null;
  createdAt: string;
}

export interface CreateAnnouncementInput {
  title: string;
  content: string;
  type: AnnouncementType;
  priority: AnnouncementPriority;
  targetAudience?: string | null;
}

export async function getMyAnnouncementFeed(): Promise<AnnouncementFeedItem[]> {
  const { data } = await apiClient.get<AnnouncementFeedItem[]>(`${BASE}/feed`);
  return data;
}

export async function markAnnouncementRead(id: string): Promise<void> {
  await apiClient.post(`${BASE}/${id}/read`);
}

export async function listAnnouncements(): Promise<Announcement[]> {
  const { data } = await apiClient.get<Announcement[]>(BASE);
  return data;
}

export async function createAnnouncement(input: CreateAnnouncementInput): Promise<string> {
  const { data } = await apiClient.post<string>(BASE, input);
  return data;
}

/** `expiresAt` is an ISO instant; null keeps the announcement live until it is archived. */
export async function publishAnnouncement(id: string, expiresAt: string | null): Promise<void> {
  await apiClient.post(`${BASE}/${id}/publish`, { expiresAt });
}

export async function archiveAnnouncement(id: string): Promise<void> {
  await apiClient.post(`${BASE}/${id}/archive`);
}

export async function getAnnouncementReadCount(id: string): Promise<number> {
  const { data } = await apiClient.get<number>(`${BASE}/${id}/read-count`);
  return data;
}
