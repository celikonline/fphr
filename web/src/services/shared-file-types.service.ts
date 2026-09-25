/** E8: shared file type catalog (`api/v1/documents/shared-file-types`); write ops need `documents.sharedfiles.types.manage`. */
import { apiClient } from "@senseik/api-client";

const SHARED_FILE_TYPES_BASE = "/v1/documents/shared-file-types";

export interface SharedFileType {
  id: string;
  name: string;
  visibleOnMobile: boolean;
  isActive: boolean;
  sortOrder: number;
}

export interface SharedFileTypeInput {
  name: string;
  visibleOnMobile: boolean;
  sortOrder?: number;
}

export async function listSharedFileTypes(isActive?: boolean): Promise<SharedFileType[]> {
  const { data } = await apiClient.get<SharedFileType[]>(SHARED_FILE_TYPES_BASE, { params: { isActive } });
  return data;
}

export async function createSharedFileType(input: SharedFileTypeInput): Promise<SharedFileType> {
  const { data } = await apiClient.post<SharedFileType>(SHARED_FILE_TYPES_BASE, input);
  return data;
}

export async function updateSharedFileType(id: string, input: SharedFileTypeInput): Promise<SharedFileType> {
  const { data } = await apiClient.put<SharedFileType>(`${SHARED_FILE_TYPES_BASE}/${id}`, input);
  return data;
}

export async function setSharedFileTypeActive(id: string, isActive: boolean): Promise<SharedFileType> {
  const { data } = await apiClient.post<SharedFileType>(`${SHARED_FILE_TYPES_BASE}/${id}/${isActive ? "activate" : "deactivate"}`);
  return data;
}
