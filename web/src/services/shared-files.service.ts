/**
 * Shared company files (senseik-028): a tenant-wide (or role-restricted) file library, distinct from the
 * employee-scoped personnel documents in documents.service.ts. Mirrors that module's presigned-URL upload flow
 * (initiate -> PUT bytes directly to storage -> confirm) against `api/v1/documents/shared-files`.
 */
import axios from "axios";
import { apiClient } from "@senseik/api-client";
import type { PagedQueryParams, PagedResult } from "@/types";

const SHARED_FILES_BASE = "/v1/documents/shared-files";

/** Native C# enum, camelCase JsonStringEnumConverter. */
export type SharedFileVisibility = "everyone" | "restrictedToRoles";

export interface SharedFile {
  id: string;
  fileName: string;
  contentType: string;
  size: number;
  uploadedAt: string;
  uploadedBy: string;
  visibility: SharedFileVisibility;
  allowedRoles: string[];
  /** E8: catalog type (missing = untyped) and the "mobilde görünsün" flag. */
  typeId?: string;
  visibleOnMobile: boolean;
}

export interface SharedFileListFilters {
  mobileOnly?: boolean;
  typeId?: string;
}

export interface SharedFileDetail {
  id: string;
  fileName: string;
  contentType: string;
  size: number;
  uploadedAt: string;
  downloadUrl: string;
  downloadUrlExpiresAt: string;
}

export interface SharedFileQuota {
  usedBytes: number;
  quotaBytes: number;
  usedPercent: number;
  isExceeded: boolean;
}

export async function listSharedFiles(params: PagedQueryParams & SharedFileListFilters = {}): Promise<PagedResult<SharedFile>> {
  const { data } = await apiClient.get<PagedResult<SharedFile>>(SHARED_FILES_BASE, { params });
  return data;
}

export async function getSharedFile(id: string): Promise<SharedFileDetail> {
  const { data } = await apiClient.get<SharedFileDetail>(`${SHARED_FILES_BASE}/${id}`);
  return data;
}

export async function getSharedFileQuota(): Promise<SharedFileQuota> {
  const { data } = await apiClient.get<SharedFileQuota>(`${SHARED_FILES_BASE}/quota`);
  return data;
}

export async function deleteSharedFile(id: string): Promise<void> {
  await apiClient.delete(`${SHARED_FILES_BASE}/${id}`);
}

interface InitiateSharedFileUploadResult {
  fileId: string;
  presignedUrl: string;
  requestId: string;
  expiresAt: string;
}

/** Options for a role-restricted upload; omit (or pass `visibility: "everyone"`) for a tenant-wide file. */
export interface UploadSharedFileOptions {
  visibility?: SharedFileVisibility;
  allowedRoles?: string[];
  /** E8: catalog type and "mobilde görünsün" flag (omit to let the server default from the type). */
  typeId?: string;
  visibleOnMobile?: boolean;
}

/**
 * Uploads a file through the shared company files' presigned-URL flow (initiate -> PUT bytes directly to storage
 * -> confirm) and returns the new file's id. Requires `documents.company.manage`.
 */
export async function uploadSharedFile(file: File, options: UploadSharedFileOptions = {}): Promise<string> {
  const contentType = file.type || "application/octet-stream";
  const { data: initiated } = await apiClient.post<InitiateSharedFileUploadResult>(`${SHARED_FILES_BASE}/initiate-upload`, {
    fileName: file.name,
    contentType,
    fileSizeBytes: file.size,
    visibility: options.visibility ?? "everyone",
    allowedRoles: options.allowedRoles ?? null,
    typeId: options.typeId ?? null,
    visibleOnMobile: options.visibleOnMobile ?? null,
  });

  // Upload bytes directly to storage via the presigned URL (not our API - no auth header, plain axios).
  const uploadResponse = await axios.put(initiated.presignedUrl, file, { headers: { "Content-Type": contentType } });
  const etag = (uploadResponse.headers["etag"] as string | undefined) ?? initiated.requestId;

  await apiClient.post(`${SHARED_FILES_BASE}/${initiated.fileId}/confirm-upload`, {
    eTag: etag,
    actualFileSizeBytes: file.size,
  });

  return initiated.fileId;
}

/** E8: sets a file's type and "mobilde görünsün" flag (`PUT {id}/classification`, requires `documents.company.manage`). */
export async function classifySharedFile(id: string, input: { typeId?: string | null; visibleOnMobile: boolean }): Promise<void> {
  await apiClient.put(`${SHARED_FILES_BASE}/${id}/classification`, { typeId: input.typeId ?? null, visibleOnMobile: input.visibleOnMobile });
}

/** E8: downloads the selected (or filtered) visible files as one zip blob. */
export async function downloadSharedFilesZip(request: { fileIds?: string[]; mobileOnly?: boolean; typeId?: string }): Promise<Blob> {
  const { data } = await apiClient.post<Blob>(`${SHARED_FILES_BASE}/download-zip`, request, { responseType: "blob" });
  return data;
}
