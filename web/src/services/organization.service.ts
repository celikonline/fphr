/**
 * Organization Units Service
 *
 * Screen 2 (Item 20): tree of org units (Headquarters -> Sales/Engineering/HR/Finance).
 *
 * TODO(item-20-backend): `Sense.Modules.Organization` is currently an empty Phase-0 skeleton
 * (see OrganizationModule.cs: "Faz 0: iskelet; şirket/birim/lokasyon/pozisyon bir sonraki adımda") -
 * there is no OrgUnit entity, migration, or controller yet, and the seed data (Item 18) does not
 * create any org units either. This service mocks `/v1/org/units` REST conventions (matching the
 * shape a future OrganizationModule controller would expose) against localStorage, seeded with the
 * 5-unit hierarchy described in the Item 20 brief, so the screen is fully usable end-to-end.
 * Swap each function body for a real `apiClient` call once the backend exists; the request/response
 * shapes below are the intended real contract.
 */
import { mockDelay, nextMockId, readMock, writeMock } from "@/services/mock/local-store";

export interface OrgUnit {
  id: string;
  name: string;
  parentId: string | null;
  employeeCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface CreateOrgUnitInput {
  name: string;
  parentId: string | null;
}

export interface UpdateOrgUnitInput {
  name: string;
  parentId: string | null;
}

const MOCK_KEY = "senseik.mock.org-units";

function seedUnits(): OrgUnit[] {
  const now = new Date().toISOString();
  const hq: OrgUnit = { id: "hq", name: "Headquarters", parentId: null, employeeCount: 5, createdAt: now, updatedAt: now };
  const children: OrgUnit[] = [
    { id: "sales", name: "Sales", parentId: "hq", employeeCount: 12, createdAt: now, updatedAt: now },
    { id: "engineering", name: "Engineering", parentId: "hq", employeeCount: 18, createdAt: now, updatedAt: now },
    { id: "hr", name: "HR", parentId: "hq", employeeCount: 6, createdAt: now, updatedAt: now },
    { id: "finance", name: "Finance", parentId: "hq", employeeCount: 9, createdAt: now, updatedAt: now },
  ];
  return [hq, ...children];
}

function readAll(): OrgUnit[] {
  return readMock(MOCK_KEY, seedUnits());
}

function writeAll(units: OrgUnit[]): OrgUnit[] {
  return writeMock(MOCK_KEY, units);
}

export async function listOrgUnits(): Promise<OrgUnit[]> {
  await mockDelay();
  return readAll();
}

export async function createOrgUnit(input: CreateOrgUnitInput): Promise<OrgUnit> {
  await mockDelay();
  const units = readAll();
  const now = new Date().toISOString();
  const unit: OrgUnit = {
    id: nextMockId("unit"),
    name: input.name,
    parentId: input.parentId,
    employeeCount: 0,
    createdAt: now,
    updatedAt: now,
  };
  writeAll([...units, unit]);
  return unit;
}

export async function updateOrgUnit(id: string, input: UpdateOrgUnitInput): Promise<OrgUnit> {
  await mockDelay();
  const units = readAll();
  const index = units.findIndex((u) => u.id === id);
  if (index === -1) throw new Error(`Org unit ${id} not found`);
  const updated: OrgUnit = { ...units[index], ...input, updatedAt: new Date().toISOString() };
  units[index] = updated;
  writeAll(units);
  return updated;
}

export async function deleteOrgUnit(id: string): Promise<void> {
  await mockDelay();
  const units = readAll();
  const hasChildren = units.some((u) => u.parentId === id);
  if (hasChildren) {
    throw new Error("Cannot delete a unit that still has child units");
  }
  writeAll(units.filter((u) => u.id !== id));
}

/** Builds a parent -> children lookup for rendering the tree. */
export function groupByParent(units: OrgUnit[]): Map<string | null, OrgUnit[]> {
  const map = new Map<string | null, OrgUnit[]>();
  for (const unit of units) {
    const bucket = map.get(unit.parentId) ?? [];
    bucket.push(unit);
    map.set(unit.parentId, bucket);
  }
  return map;
}
