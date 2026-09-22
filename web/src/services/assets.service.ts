/**
 * Assets module (zimmet, roadmap #65): mirrors `AssetsController`/`AssetCategoriesController` (`api/v1/assets/...`).
 * No assignment history/ledger - an asset has a single current assignment (Status/AssignedEmployeeId/IssuedAt/
 * ReturnedAt), see `Asset.cs` remarks.
 */
import { apiClient } from "@senseik/api-client";
import type { PagedQueryParams, PagedResult } from "@/types";

const ASSETS_BASE = "/v1/assets";

/** Native C# enum, camelCase JsonStringEnumConverter. */
export type AssetStatus = "unassigned" | "assigned" | "retired";
export const ASSET_STATUSES: AssetStatus[] = ["unassigned", "assigned", "retired"];

export interface Asset {
  id: string;
  categoryId: string;
  name: string;
  serialNumber: string | null;
  description: string | null;
  status: AssetStatus;
  assignedEmployeeId: string | null;
  issuedAt: string | null;
  returnedAt: string | null;
  /** KKD (periodically replaced category): issue date + interval, only while assigned; omitted otherwise. */
  replacementDueOn?: string | null;
}

export interface AssetCategory {
  id: string;
  code: string;
  name: string;
  /** Periodic replacement interval in days (e.g. the seeded "ppe" category); omitted when none. */
  replacementIntervalDays?: number | null;
}

export interface CreateAssetInput {
  categoryId: string;
  name: string;
  serialNumber: string | null;
  description: string | null;
}

export interface UpdateAssetInput {
  name: string;
  serialNumber: string | null;
  description: string | null;
}

export async function listAssets(
  params: PagedQueryParams & { employeeId?: string; status?: AssetStatus; categoryId?: string } = {}
): Promise<PagedResult<Asset>> {
  const { data } = await apiClient.get<PagedResult<Asset>>(ASSETS_BASE, { params });
  return data;
}

export async function getAsset(id: string): Promise<Asset> {
  const { data } = await apiClient.get<Asset>(`${ASSETS_BASE}/${id}`);
  return data;
}

export async function getMyAssets(): Promise<Asset[]> {
  const { data } = await apiClient.get<Asset[]>(`${ASSETS_BASE}/my`);
  return data;
}

/** Employee card's "Zimmet" tab (HR/manager view) - the employee's currently assigned assets. */
export async function getEmployeeAssets(employeeId: string): Promise<Asset[]> {
  const { data } = await apiClient.get<Asset[]>(`${ASSETS_BASE}/employees/${employeeId}`);
  return data;
}

export async function createAsset(input: CreateAssetInput): Promise<string> {
  const { data } = await apiClient.post<string>(ASSETS_BASE, input);
  return data;
}

export async function updateAsset(id: string, input: UpdateAssetInput): Promise<void> {
  await apiClient.put(`${ASSETS_BASE}/${id}`, input);
}

export async function assignAsset(id: string, employeeId: string, issuedAt: string): Promise<void> {
  await apiClient.post(`${ASSETS_BASE}/${id}/assign`, { employeeId, issuedAt });
}

export async function returnAsset(id: string, returnedAt: string): Promise<void> {
  await apiClient.post(`${ASSETS_BASE}/${id}/return`, { returnedAt });
}

export async function retireAsset(id: string): Promise<void> {
  await apiClient.post(`${ASSETS_BASE}/${id}/retire`);
}

export async function listAssetCategories(): Promise<AssetCategory[]> {
  const { data } = await apiClient.get<AssetCategory[]>(`${ASSETS_BASE}/categories`);
  return data;
}

export async function createAssetCategory(
  code: string,
  name: string,
  replacementIntervalDays: number | null
): Promise<string> {
  const { data } = await apiClient.post<string>(`${ASSETS_BASE}/categories`, {
    code,
    name,
    replacementIntervalDays,
  });
  return data;
}

/** The server replaces the interval with what is sent (null clears it), so always pass the current value. */
export async function updateAssetCategory(
  id: string,
  name: string,
  replacementIntervalDays: number | null
): Promise<void> {
  await apiClient.put(`${ASSETS_BASE}/categories/${id}`, { name, replacementIntervalDays });
}

/** True when a KKD item's replacement date has passed (dates are "yyyy-MM-dd", compared as strings). */
export function isReplacementOverdue(asset: Asset, todayIso: string): boolean {
  return !!asset.replacementDueOn && asset.replacementDueOn < todayIso;
}
