/**
 * Organization service (Faz 2, TALIMAT tur 3 / sözleşme S-4): tenant-wide organization catalogs
 * that live outside the "Şirket yapısı" screens already covered by structure.service.ts (org unit
 * types, org units, companies, holiday calendars). Starts with the cost center catalog.
 *
 * Mirrors the BE twin's `Sense.Modules.Organization.Api.Controllers.CostCentersController`
 * (docs/implementation/faz2-organization-api-sozlesmesi.md tracks the shared contract).
 */
import { apiClient } from "@senseik/api-client";

const COST_CENTERS = "/v1/organization/cost-centers";

export interface CostCenter {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
}

export async function getCostCenters(includeInactive = false): Promise<CostCenter[]> {
  const { data } = await apiClient.get<CostCenter[]>(COST_CENTERS, { params: { includeInactive } });
  return data;
}

export async function createCostCenter(input: { code: string; name: string }): Promise<{ id: string }> {
  const { data } = await apiClient.post<{ id: string }>(COST_CENTERS, input);
  return data;
}

/** The code is immutable after creation - only the name can be renamed. */
export async function renameCostCenter(costCenterId: string, name: string): Promise<void> {
  await apiClient.put(`${COST_CENTERS}/${costCenterId}`, { name });
}

export async function setCostCenterActive(costCenterId: string, isActive: boolean): Promise<void> {
  await apiClient.put(`${COST_CENTERS}/${costCenterId}/active`, { isActive });
}
