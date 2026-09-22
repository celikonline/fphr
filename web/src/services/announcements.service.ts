/**
 * Kurumsal duyurular: mirrors `AnnouncementsController.cs` (`api/v1/announcements`). The feed (`/feed`, `/{id}/read`)
 * is self-service (`announcements.my.feed`); list/create/publish/archive/read-count need `announcements.announcements.*`.
 */
import axios from "axios";
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

/** Tek bir duyuru (düzenleme sayfasının mevcut bir taslağı yüklemesi için). */
export async function getAnnouncement(id: string): Promise<Announcement> {
  const { data } = await apiClient.get<Announcement>(`${BASE}/${id}`);
  return data;
}

export async function createAnnouncement(input: CreateAnnouncementInput): Promise<string> {
  const { data } = await apiClient.post<string>(BASE, input);
  return data;
}

/** Taslak duyuruyu düzenle (tam sayfa editör); yalnızca taslak durumdayken serbesttir. */
export async function updateAnnouncement(
  id: string,
  input: CreateAnnouncementInput
): Promise<void> {
  await apiClient.put(`${BASE}/${id}`, input);
}

export const ANNOUNCEMENT_ATTACHMENT_KINDS = ["inline", "file"] as const;
export type AnnouncementAttachmentKind = (typeof ANNOUNCEMENT_ATTACHMENT_KINDS)[number];

/** `AnnouncementAttachmentDto`: gömülü resim veya indirilebilir dosya, taze (kısa ömürlü) bir indirme URL'siyle - asla kalıcı bir URL değildir, her sorguda yenilenir. */
export interface AnnouncementAttachment {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  kind: string;
  downloadUrl: string;
}

/** Duyuru ekleri (yönetim/editör görünümü, taslak dahil her durum). */
export async function listAnnouncementAttachments(
  announcementId: string
): Promise<AnnouncementAttachment[]> {
  const { data } = await apiClient.get<AnnouncementAttachment[]>(
    `${BASE}/${announcementId}/attachments`
  );
  return data;
}

/** Duyuru ekleri (çalışan akışı görünümü): yalnızca yayında ve süresi geçmemiş duyurular için. */
export async function listMyAnnouncementAttachments(
  announcementId: string
): Promise<AnnouncementAttachment[]> {
  const { data } = await apiClient.get<AnnouncementAttachment[]>(
    `${BASE}/feed/${announcementId}/attachments`
  );
  return data;
}

interface InitiateAttachmentUploadResponse {
  attachmentId: string;
  uploadUrl: string;
  requestId: string;
  expiresAt: string;
}

interface ConfirmAttachmentUploadResponse {
  attachmentId: string;
  downloadUrl: string;
}

/**
 * Presigned-URL ile bir ek yükler: initiate → PUT (doğrudan storage'a, API'den geçmeden) → confirm. Editördeki resim
 * yapıştırma/sürükleme ve dosya ekleme akışlarının ikisi de bunu kullanır; `kind` yalnızca ekranda nasıl
 * gösterileceğini belirler (gömülü resim / indirilebilir dosya).
 */
export async function uploadAnnouncementAttachment(
  announcementId: string,
  file: File,
  kind: AnnouncementAttachmentKind
): Promise<AnnouncementAttachment> {
  const { data: initiated } = await apiClient.post<InitiateAttachmentUploadResponse>(
    `${BASE}/${announcementId}/attachments/initiate-upload`,
    {
      fileName: file.name,
      contentType: file.type || "application/octet-stream",
      fileSizeBytes: file.size,
      kind,
    }
  );

  // Upload bytes directly to storage via the presigned URL (not our API - no auth header, plain axios).
  const contentType = file.type || "application/octet-stream";
  const uploadResponse = await axios.put(initiated.uploadUrl, file, {
    headers: { "Content-Type": contentType },
  });
  const eTag = (uploadResponse.headers["etag"] as string | undefined) ?? initiated.requestId;

  const { data: confirmed } = await apiClient.post<ConfirmAttachmentUploadResponse>(
    `${BASE}/${announcementId}/attachments/${initiated.attachmentId}/confirm-upload`,
    { eTag, actualFileSizeBytes: file.size }
  );

  return {
    id: confirmed.attachmentId,
    fileName: file.name,
    contentType: file.type,
    sizeBytes: file.size,
    kind,
    downloadUrl: confirmed.downloadUrl,
  };
}

export async function deleteAnnouncementAttachment(
  announcementId: string,
  attachmentId: string
): Promise<void> {
  await apiClient.delete(`${BASE}/${announcementId}/attachments/${attachmentId}`);
}

/**
 * `expiresAt` is an ISO instant; null keeps the announcement live until it is archived. `notifyEmployees` also sends
 * every active employee an e-mail and an in-app notification.
 */
export async function publishAnnouncement(
  id: string,
  expiresAt: string | null,
  notifyEmployees = false
): Promise<void> {
  await apiClient.post(`${BASE}/${id}/publish`, { expiresAt, notifyEmployees });
}

export async function archiveAnnouncement(id: string): Promise<void> {
  await apiClient.post(`${BASE}/${id}/archive`);
}

export async function getAnnouncementReadCount(id: string): Promise<number> {
  const { data } = await apiClient.get<number>(`${BASE}/${id}/read-count`);
  return data;
}
