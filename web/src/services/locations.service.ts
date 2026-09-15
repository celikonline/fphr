/**
 * Locations Service
 *
 * Screen 3 (Item 20): physical offices, optionally linked to an organization unit.
 *
 * TODO(item-20-backend): No Location entity/controller exists yet (see organization.service.ts for
 * the full backend-state note - Organization module is still a Phase-0 skeleton). This mocks the
 * intended `/v1/org/locations` REST endpoints against localStorage so the screen works end-to-end;
 * swap each function body for a real `apiClient` call once the backend exists.
 */
import { mockDelay, nextMockId, readMock, writeMock } from "@/services/mock/local-store";
import { paginate, type ClientPageQuery } from "@/services/mock/paginate";
import type { PagedResult } from "@/types";

export interface Location {
  id: string;
  name: string;
  address: string;
  timeZone: string;
  orgUnitId: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface LocationInput {
  name: string;
  address: string;
  timeZone: string;
  orgUnitId: string | null;
}

const MOCK_KEY = "senseik.mock.locations";

function seedLocations(): Location[] {
  const now = new Date().toISOString();
  return [
    { id: "loc-ist", name: "İstanbul HQ", address: "Maslak, İstanbul", timeZone: "Europe/Istanbul", orgUnitId: "hq", createdAt: now, updatedAt: now },
    { id: "loc-ank", name: "Ankara Office", address: "Çankaya, Ankara", timeZone: "Europe/Istanbul", orgUnitId: "sales", createdAt: now, updatedAt: now },
    { id: "loc-izm", name: "İzmir Office", address: "Konak, İzmir", timeZone: "Europe/Istanbul", orgUnitId: "engineering", createdAt: now, updatedAt: now },
  ];
}

function readAll(): Location[] {
  return readMock(MOCK_KEY, seedLocations());
}

function writeAll(locations: Location[]): Location[] {
  return writeMock(MOCK_KEY, locations);
}

export async function listLocations(query: ClientPageQuery = {}): Promise<PagedResult<Location>> {
  await mockDelay();
  return paginate(readAll(), query, (l) => `${l.name} ${l.address}`, (l) => ({ name: l.name }));
}

export async function createLocation(input: LocationInput): Promise<Location> {
  await mockDelay();
  const now = new Date().toISOString();
  const location: Location = { id: nextMockId("loc"), ...input, createdAt: now, updatedAt: now };
  writeAll([...readAll(), location]);
  return location;
}

export async function updateLocation(id: string, input: LocationInput): Promise<Location> {
  await mockDelay();
  const all = readAll();
  const index = all.findIndex((l) => l.id === id);
  if (index === -1) throw new Error(`Location ${id} not found`);
  const updated: Location = { ...all[index], ...input, updatedAt: new Date().toISOString() };
  all[index] = updated;
  writeAll(all);
  return updated;
}

export async function deleteLocation(id: string): Promise<void> {
  await mockDelay();
  writeAll(readAll().filter((l) => l.id !== id));
}
