/**
 * Positions Service
 *
 * Screen 4 (Item 20): job positions/titles, filterable by organization unit.
 *
 * TODO(item-20-backend): No Position entity/controller exists yet (see organization.service.ts for
 * the full backend-state note). This mocks the intended `/v1/org/positions` REST endpoints against
 * localStorage so the screen works end-to-end; swap each function body for a real `apiClient` call
 * once the backend exists.
 */
import { mockDelay, nextMockId, readMock, writeMock } from "@/services/mock/local-store";
import { paginate, type ClientPageQuery } from "@/services/mock/paginate";
import type { PagedResult } from "@/types";

export type PositionLevel = "junior" | "mid" | "senior" | "lead" | "manager" | "director";

export interface Position {
  id: string;
  title: string;
  level: PositionLevel;
  orgUnitId: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface PositionInput {
  title: string;
  level: PositionLevel;
  orgUnitId: string | null;
}

const MOCK_KEY = "senseik.mock.positions";

function seedPositions(): Position[] {
  const now = new Date().toISOString();
  return [
    { id: "pos-se", title: "Software Engineer", level: "mid", orgUnitId: "engineering", createdAt: now, updatedAt: now },
    { id: "pos-sse", title: "Senior Software Engineer", level: "senior", orgUnitId: "engineering", createdAt: now, updatedAt: now },
    { id: "pos-ae", title: "Account Executive", level: "mid", orgUnitId: "sales", createdAt: now, updatedAt: now },
    { id: "pos-hrbp", title: "HR Business Partner", level: "senior", orgUnitId: "hr", createdAt: now, updatedAt: now },
    { id: "pos-fc", title: "Financial Controller", level: "manager", orgUnitId: "finance", createdAt: now, updatedAt: now },
  ];
}

function readAll(): Position[] {
  return readMock(MOCK_KEY, seedPositions());
}

function writeAll(positions: Position[]): Position[] {
  return writeMock(MOCK_KEY, positions);
}

export interface ListPositionsQuery extends ClientPageQuery {
  orgUnitId?: string;
}

export async function listPositions(query: ListPositionsQuery = {}): Promise<PagedResult<Position>> {
  await mockDelay();
  let all = readAll();
  if (query.orgUnitId) {
    all = all.filter((p) => p.orgUnitId === query.orgUnitId);
  }
  return paginate(all, query, (p) => p.title, (p) => ({ title: p.title }));
}

export async function createPosition(input: PositionInput): Promise<Position> {
  await mockDelay();
  const now = new Date().toISOString();
  const position: Position = { id: nextMockId("pos"), ...input, createdAt: now, updatedAt: now };
  writeAll([...readAll(), position]);
  return position;
}

export async function updatePosition(id: string, input: PositionInput): Promise<Position> {
  await mockDelay();
  const all = readAll();
  const index = all.findIndex((p) => p.id === id);
  if (index === -1) throw new Error(`Position ${id} not found`);
  const updated: Position = { ...all[index], ...input, updatedAt: new Date().toISOString() };
  all[index] = updated;
  writeAll(all);
  return updated;
}

export async function deletePosition(id: string): Promise<void> {
  await mockDelay();
  writeAll(readAll().filter((p) => p.id !== id));
}
