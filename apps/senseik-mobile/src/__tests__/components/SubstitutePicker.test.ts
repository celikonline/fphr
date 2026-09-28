import { excludeSelf } from "@/components/leave/SubstitutePicker";
import type { PersonListItem } from "@/services/PeopleService";

function buildPerson(overrides: Partial<PersonListItem> = {}): PersonListItem {
  return {
    id: "employee-1",
    displayName: "Ayşe Yılmaz",
    titleName: "Uzman",
    departmentName: "İK",
    workEmail: "ayse.yilmaz@example.com",
    workPhone: null,
    ...overrides,
  };
}

describe("excludeSelf (B-mobil-005: vekil listesinden kendini çıkar)", () => {
  it("removes the caller's own employeeId from the directory results", () => {
    const self = buildPerson({ id: "employee-1" });
    const colleague = buildPerson({ id: "employee-2", displayName: "Mehmet Demir" });

    expect(excludeSelf([self, colleague], "employee-1")).toEqual([colleague]);
  });

  it("returns the list unchanged when excludeEmployeeId is null (never match by email/name instead)", () => {
    const people = [buildPerson({ id: "employee-1" }), buildPerson({ id: "employee-2" })];

    expect(excludeSelf(people, null)).toEqual(people);
  });

  it("returns the list unchanged when excludeEmployeeId is undefined", () => {
    const people = [buildPerson({ id: "employee-1" })];

    expect(excludeSelf(people, undefined)).toEqual(people);
  });

  it("returns an empty list unchanged", () => {
    expect(excludeSelf([], "employee-1")).toEqual([]);
  });
});
