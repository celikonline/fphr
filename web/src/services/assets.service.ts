/**
 * Assets module (zimmet, roadmap #65, v2): mirrors `AssetsController`/`AssetCategoriesController` (`api/v1/assets/...`).
 * An asset carries its current assignment (Status/AssignedEmployeeId/IssuedAt/ReturnedAt/AcknowledgedAt); closed and
 * open assignments are kept in a ledger read through the `*History` functions.
 * Contract: docs/analysis/zimmet-modulu-v2-kapsam-2026-10-07.md section 4. The API omits null fields.
 */
import { apiClient } from "@senseik/api-client";
import { addDaysToDateOnly } from "@/lib/dates";
import type { PagedQueryParams, PagedResult } from "@/types";

const ASSETS_BASE = "/v1/assets";

/** Native C# enum, camelCase JsonStringEnumConverter. */
export type AssetStatus = "unassigned" | "assigned" | "retired" | "pendingAssignment" | "lost";
export const ASSET_STATUSES: AssetStatus[] = [
  "unassigned",
  "assigned",
  "pendingAssignment",
  "retired",
  "lost",
];

export type AssetCondition = "good" | "fair" | "damaged";
export const ASSET_CONDITIONS: AssetCondition[] = ["good", "fair", "damaged"];

/** Why an assignment was closed. */
export type AssetAssignmentEndReason = "returned" | "lost";

/** The warranty counts as "expiring" when it ends within this many days from today (inclusive). */
export const WARRANTY_EXPIRING_DAYS = 60;

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
  /** Demirbaş no (unique per tenant, case-insensitive). */
  assetTag?: string | null;
  brand?: string | null;
  model?: string | null;
  purchaseDate?: string | null;
  /** Plain number in the tenant's default currency (the API has no currency field). */
  purchaseCost?: number | null;
  warrantyEndsOn?: string | null;
  condition?: AssetCondition;
  /** Only while `assigned`: when the assignee confirmed receipt; omitted while unacknowledged. */
  acknowledgedAt?: string | null;
}

/** One row of an asset's assignment ledger (`GET /{id}/history`), newest first. */
export interface AssetAssignment {
  id: string;
  employeeId: string;
  issuedAt: string;
  returnedAt?: string | null;
  endReason?: AssetAssignmentEndReason | null;
  returnCondition?: AssetCondition | null;
  note?: string | null;
  acknowledgedAt?: string | null;
}

/** An employee's assignment with the asset it was for (`/employees/{id}/history`, `/my/history`), newest first. */
export interface EmployeeAssetHistoryItem extends AssetAssignment {
  assetId: string;
  assetName: string;
  categoryId: string;
  serialNumber?: string | null;
  assetTag?: string | null;
}

export interface AssetSummaryCategory {
  categoryId: string;
  total: number;
  assigned: number;
}

export interface AssetSummary {
  total: number;
  unassigned: number;
  assigned: number;
  pendingAssignment: number;
  retired: number;
  lost: number;
  /** Assigned and not yet acknowledged by the assignee. */
  unacknowledged: number;
  /** Warranty ends within the next 60 days (Retired/Lost excluded). */
  warrantyExpiring: number;
  /** Warranty ended before today (Retired/Lost excluded). */
  warrantyExpired: number;
  /** Assigned KKD items whose replacement date has passed. */
  replacementOverdue: number;
  categories: AssetSummaryCategory[];
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
  assetTag?: string | null;
  brand?: string | null;
  model?: string | null;
  purchaseDate?: string | null;
  purchaseCost?: number | null;
  warrantyEndsOn?: string | null;
}

export interface UpdateAssetInput {
  name: string;
  serialNumber: string | null;
  description: string | null;
  assetTag?: string | null;
  brand?: string | null;
  model?: string | null;
  purchaseDate?: string | null;
  purchaseCost?: number | null;
  warrantyEndsOn?: string | null;
  condition?: AssetCondition;
}

export async function listAssets(
  params: PagedQueryParams & {
    employeeId?: string;
    status?: AssetStatus;
    categoryId?: string;
    /** Case-insensitive match on name, serial number, asset tag, brand or model. */
    search?: string;
  } = {}
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

/** `condition` defaults to good server-side and also becomes the asset's own condition. */
export async function returnAsset(
  id: string,
  returnedAt: string,
  condition?: AssetCondition,
  note?: string
): Promise<void> {
  await apiClient.post(`${ASSETS_BASE}/${id}/return`, { returnedAt, condition, note });
}

/** Unassigned or assigned asset -> lost; an open assignment is closed with end reason "lost". */
export async function markAssetLost(id: string, occurredOn: string, note?: string): Promise<void> {
  await apiClient.post(`${ASSETS_BASE}/${id}/lost`, { occurredOn, note });
}

/** The assignee confirms receipt ("teslim aldim"); only the assigned employee, once per assignment. */
export async function acknowledgeAsset(id: string): Promise<void> {
  await apiClient.post(`${ASSETS_BASE}/${id}/acknowledge`);
}

export async function getAssetHistory(id: string): Promise<AssetAssignment[]> {
  const { data } = await apiClient.get<AssetAssignment[]>(`${ASSETS_BASE}/${id}/history`);
  return data;
}

/** HR view (assets.assets.view): every assignment of the employee, open and closed. */
export async function getEmployeeAssetHistory(
  employeeId: string
): Promise<EmployeeAssetHistoryItem[]> {
  const { data } = await apiClient.get<EmployeeAssetHistoryItem[]>(
    `${ASSETS_BASE}/employees/${employeeId}/history`
  );
  return data;
}

/** The caller's own assignments, open and closed (assets.my.assets). */
export async function getMyAssetHistory(): Promise<EmployeeAssetHistoryItem[]> {
  const { data } = await apiClient.get<EmployeeAssetHistoryItem[]>(`${ASSETS_BASE}/my/history`);
  return data;
}

export async function getAssetSummary(): Promise<AssetSummary> {
  const { data } = await apiClient.get<AssetSummary>(`${ASSETS_BASE}/summary`);
  return data;
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

/** Retired and lost assets no longer carry a warranty follow-up. */
function hasWarrantyFollowUp(asset: Asset): boolean {
  return !!asset.warrantyEndsOn && asset.status !== "retired" && asset.status !== "lost";
}

/** True when the warranty ended before today (dates are "yyyy-MM-dd"); never for retired/lost assets. */
export function isWarrantyExpired(asset: Asset, todayIso: string): boolean {
  return hasWarrantyFollowUp(asset) && asset.warrantyEndsOn! < todayIso;
}

/** True when the warranty ends from today up to 60 days ahead (inclusive); never for retired/lost assets. */
export function isWarrantyExpiring(asset: Asset, todayIso: string): boolean {
  if (!hasWarrantyFollowUp(asset)) return false;
  const endsOn = asset.warrantyEndsOn!;
  return endsOn >= todayIso && endsOn <= addDaysToDateOnly(todayIso, WARRANTY_EXPIRING_DAYS);
}
