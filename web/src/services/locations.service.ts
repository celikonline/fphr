/**
 * Locations Service
 *
 * Screen 3 (Item 20 frontend / roadmap Faz 0 item #6 backend): physical offices/stores/warehouses/
 * factories (or a Remote placeholder), each with a country code.
 *
 * Wired to the real `Sense.Modules.Organization` endpoints (LocationsController) - see
 * web/src/services/users.service.ts for the same real-backend pattern this mirrors. The list endpoint
 * follows the project's grid standard (?page&pageSize&sort&q), matching PagedResult<T> exactly.
 *
 * Note: the backend models the relationship as OrgUnit.LocationId -> Location (a location can be
 * shared by several org units), not the other way around, so - unlike the old localStorage mock -
 * Location itself carries no orgUnitId. Assigning a location to an org unit happens from the
 * Şirket yapısı screen (web/src/pages/settings/company-structure.tsx), not from here.
 */
import { apiClient } from "@senseik/api-client";
import type { PagedResult, PagedQueryParams } from "@/types";

/** The API serializes enums camelCase (JsonStringEnumConverter with CamelCase naming). */
export type LocationType = "office" | "store" | "warehouse" | "factory" | "remote";

export const LOCATION_TYPES: LocationType[] = ["office", "store", "warehouse", "factory", "remote"];

export interface Location {
  id: string;
  name: string;
  type: LocationType;
  countryCode: string;
  address: string | null;
  isActive: boolean;
}

export interface LocationInput {
  name: string;
  type: LocationType;
  countryCode: string;
  address?: string | null;
}

const LOCATIONS_BASE = "/v1/organization/locations";

const normalize = (location: Location): Location => ({
  ...location,
  type: (location.type.charAt(0).toLowerCase() + location.type.slice(1)) as LocationType,
});

export async function listLocations(query: PagedQueryParams = {}): Promise<PagedResult<Location>> {
  const { data } = await apiClient.get<PagedResult<Location>>(LOCATIONS_BASE, { params: query });
  return { ...data, items: data.items.map(normalize) };
}

export async function createLocation(input: LocationInput): Promise<Location> {
  const { data } = await apiClient.post<Location>(LOCATIONS_BASE, input);
  return normalize(data);
}

export async function updateLocation(id: string, input: LocationInput): Promise<Location> {
  const { data } = await apiClient.put<Location>(`${LOCATIONS_BASE}/${id}`, input);
  return normalize(data);
}

export async function deleteLocation(id: string): Promise<void> {
  await apiClient.delete(`${LOCATIONS_BASE}/${id}`);
}
