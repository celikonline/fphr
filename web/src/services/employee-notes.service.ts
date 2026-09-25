/** Çalışan notları (employee.notes.view/manage): mirrors `EmployeeNotesController` (`api/v1/employee-notes`). */
import axios from "axios";
import { apiClient } from "@senseik/api-client";
import type { PagedResult } from "@/types";

const BASE = "/v1/employee-notes";

/** Server limits (EmployeeNoteLimits). */
export const NOTE_TITLE_MAX = 255;
export const NOTE_CONTENT_MAX = 512;

export type EmployeeNoteImportance = "low" | "normal" | "high";
export const NOTE_IMPORTANCES: EmployeeNoteImportance[] = ["low", "normal", "high"];

export interface EmployeeNoteAttachment {
  fileName: string;
  contentType: string;
  sizeBytes: number;
  downloadUrl: string;
}

/** `EmployeeNoteDto`. */
export interface EmployeeNote {
  id: string;
  employeeId: string;
  title: string;
  content: string;
  importance: EmployeeNoteImportance;
  authorUserId: string;
  authorName?: string | null;
  createdAt: string;
  attachment?: EmployeeNoteAttachment | null;
}

export interface SaveEmployeeNoteInput {
  title: string;
  content: string;
  importance: EmployeeNoteImportance;
}

interface InitiateAttachmentResult {
  uploadUrl: string;
  requestId: string;
  expiresAt: string;
}

export async function listEmployeeNotes(
  employeeId: string,
  importance?: EmployeeNoteImportance
): Promise<PagedResult<EmployeeNote>> {
  const { data } = await apiClient.get<PagedResult<EmployeeNote>>(BASE, {
    params: { employeeId, importance, page: 1, pageSize: 100 },
  });
  return data;
}

export async function createEmployeeNote(
  employeeId: string,
  input: SaveEmployeeNoteInput
): Promise<EmployeeNote> {
  const { data } = await apiClient.post<EmployeeNote>(BASE, { employeeId, ...input });
  return data;
}

export async function updateEmployeeNote(
  id: string,
  input: SaveEmployeeNoteInput
): Promise<EmployeeNote> {
  const { data } = await apiClient.put<EmployeeNote>(`${BASE}/${id}`, input);
  return data;
}

export async function deleteEmployeeNote(id: string): Promise<void> {
  await apiClient.delete(`${BASE}/${id}`);
}

/** Presigned flow: initiate -> PUT bytes to storage -> confirm. */
export async function uploadNoteAttachment(noteId: string, file: File): Promise<EmployeeNote> {
  const contentType = file.type || "application/octet-stream";
  const { data: initiated } = await apiClient.post<InitiateAttachmentResult>(
    `${BASE}/${noteId}/attachment/initiate`,
    { fileName: file.name, contentType, fileSizeBytes: file.size }
  );
  const uploaded = await axios.put(initiated.uploadUrl, file, {
    headers: { "Content-Type": contentType },
  });
  const eTag = (uploaded.headers["etag"] as string | undefined) ?? initiated.requestId;
  const { data } = await apiClient.post<EmployeeNote>(`${BASE}/${noteId}/attachment/confirm`, {
    eTag,
  });
  return data;
}

export async function deleteNoteAttachment(noteId: string): Promise<void> {
  await apiClient.delete(`${BASE}/${noteId}/attachment`);
}
