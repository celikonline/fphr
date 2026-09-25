import { beforeEach, describe, expect, it, vi } from "vitest";
import type { CompanyCalendarEntry } from "@/services/company-calendar.service";
import type { EmployeeSelection } from "@/types";
import {
  EmptyInviteeSelectionError,
  InviteeLimitError,
  MAX_INVITEES,
  entryDays,
  resolveSelectionToIds,
} from "./event-form-model";

vi.mock("@/services/employees.service", () => ({ listEmployees: vi.fn() }));
import { listEmployees } from "@/services/employees.service";

const ids = (from: number, to: number) => Array.from({ length: to - from }, (_, i) => `e${from + i}`);
const pageOf = (idList: string[], totalCount: number) =>
  ({ items: idList.map((id) => ({ id })), totalCount, page: 1, pageSize: 200 }) as never;
const filterSelection = (excluded: string[] | null = null): EmployeeSelection => ({
  employeeIds: null,
  filter: { q: "a" },
  excludedEmployeeIds: excluded,
});

beforeEach(() => vi.mocked(listEmployees).mockReset());

describe("resolveSelectionToIds", () => {
  it("returns explicit ids untouched without querying", async () => {
    const out = await resolveSelectionToIds({ employeeIds: ["a", "b"], filter: null, excludedEmployeeIds: null });
    expect(out).toEqual(["a", "b"]);
    expect(listEmployees).not.toHaveBeenCalled();
  });

  it("expands a filter across 3 pages and drops excluded ids", async () => {
    vi.mocked(listEmployees)
      .mockResolvedValueOnce(pageOf(ids(0, 200), 450))
      .mockResolvedValueOnce(pageOf(ids(200, 400), 450))
      .mockResolvedValueOnce(pageOf(ids(400, 450), 450));
    const out = await resolveSelectionToIds(filterSelection(["e1", "e250", "e449"]));
    expect(listEmployees).toHaveBeenCalledTimes(3);
    expect(vi.mocked(listEmployees).mock.calls.map((c) => c[0]?.page)).toEqual([1, 2, 3]);
    expect(out).toHaveLength(447);
    expect(out).not.toContain("e1");
    expect(out).not.toContain("e250");
    expect(out).not.toContain("e449");
  });

  it("rejects more than the invitee cap with the resolved count", async () => {
    vi.mocked(listEmployees)
      .mockResolvedValueOnce(pageOf(ids(0, 200), 501))
      .mockResolvedValueOnce(pageOf(ids(200, 400), 501))
      .mockResolvedValueOnce(pageOf(ids(400, 501), 501));
    const failure = await resolveSelectionToIds(filterSelection()).catch((e: unknown) => e);
    expect(failure).toBeInstanceOf(InviteeLimitError);
    expect((failure as InviteeLimitError).count).toBe(MAX_INVITEES + 1);
  });

  it("accepts exactly the cap after exclusions bring the count down", async () => {
    vi.mocked(listEmployees)
      .mockResolvedValueOnce(pageOf(ids(0, 200), 501))
      .mockResolvedValueOnce(pageOf(ids(200, 400), 501))
      .mockResolvedValueOnce(pageOf(ids(400, 501), 501));
    expect(await resolveSelectionToIds(filterSelection(["e0"]))).toHaveLength(MAX_INVITEES);
  });

  it("errors on an empty result", async () => {
    vi.mocked(listEmployees).mockResolvedValueOnce(pageOf([], 0));
    await expect(resolveSelectionToIds(filterSelection())).rejects.toBeInstanceOf(EmptyInviteeSelectionError);
    await expect(resolveSelectionToIds({ employeeIds: [], filter: null, excludedEmployeeIds: null })).rejects.toBeInstanceOf(
      EmptyInviteeSelectionError,
    );
  });
});

describe("entryDays (local-day placement)", () => {
  const base: CompanyCalendarEntry = { id: "1", title: "t", date: "", type: "companyEvent" };
  const localDay = (y: number, m: number, d: number) => `${y}-${String(m).padStart(2, "0")}-${String(d).padStart(2, "0")}`;

  it("places a timed event on the local day of startsAt even when the backend date is the UTC day", () => {
    const startsAt = new Date(2026, 8, 11, 1, 30).toISOString(); // local 11 Sep 01:30 (UTC day is the 10th in UTC+3)
    const entry = { ...base, isAllDay: false, startsAt, endsAt: new Date(2026, 8, 11, 2, 30).toISOString(), date: startsAt.slice(0, 10) };
    expect(entryDays(entry)).toEqual([localDay(2026, 9, 11)]);
  });

  it("spreads a multi-day timed event over every local day", () => {
    const entry = {
      ...base,
      isAllDay: false,
      date: "2026-09-10",
      startsAt: new Date(2026, 8, 10, 22, 0).toISOString(),
      endsAt: new Date(2026, 8, 12, 9, 0).toISOString(),
    };
    expect(entryDays(entry)).toEqual(["2026-09-10", "2026-09-11", "2026-09-12"]);
  });

  it("keeps all-day and derived entries on their date", () => {
    expect(entryDays({ ...base, isAllDay: true, date: "2026-09-10", startsAt: "2026-09-10T00:00:00.000Z" })).toEqual(["2026-09-10"]);
    expect(entryDays({ ...base, type: "birthday", date: "2026-09-10" })).toEqual(["2026-09-10"]);
  });
});
