import { describe, expect, it } from "vitest";
import type { ReportParameter } from "@/services/reports.service";
import { allSatisfied, defaultValuesFor, valuesFromSearchParams, valuesToSearchParams } from "./report-parameters";

const TODAY = new Date(2026, 8, 15);

const YEAR: ReportParameter = { key: "year", kind: "year", isRequired: true };
const RANGE: ReportParameter = { key: "dateRange", kind: "dateRange", isRequired: true };
const UNITS: ReportParameter = { key: "orgUnitIds", kind: "orgUnits", isRequired: false };
const TERMINATED: ReportParameter = { key: "includeTerminated", kind: "boolean", isRequired: false };

describe("report-parameters", () => {
  it("defaults required year and date range to the current year and month", () => {
    expect(defaultValuesFor([YEAR, RANGE, UNITS], TODAY)).toEqual({ year: "2026", from: "2026-09-01", to: "2026-09-30" });
  });

  it("reports unsatisfied when a required filter is empty", () => {
    expect(allSatisfied([YEAR], {})).toBe(false);
    expect(allSatisfied([RANGE], { from: "2026-01-01" })).toBe(false);
    expect(allSatisfied([YEAR, UNITS], { year: "2026" })).toBe(true);
  });

  it("lets the address override defaults for the filters the report accepts", () => {
    const search = new URLSearchParams("year=2025&from=2025-01-01&to=2025-01-31&orgUnitIds=a,b&includeTerminated=true");
    expect(valuesFromSearchParams(search, [YEAR, RANGE, UNITS, TERMINATED], TODAY)).toEqual({
      year: "2025",
      from: "2025-01-01",
      to: "2025-01-31",
      orgUnitIds: "a,b",
      includeTerminated: "true",
    });
  });

  it("ignores address keys the report does not accept", () => {
    const search = new URLSearchParams("year=2025&orgUnitIds=a&evil=x");
    expect(valuesFromSearchParams(search, [YEAR], TODAY)).toEqual({ year: "2025" });
  });

  it("round-trips comma separated unit ids through the address", () => {
    const search = valuesToSearchParams({ orgUnitIds: "a,b", year: "2026" });
    expect(valuesFromSearchParams(new URLSearchParams(search.toString()), [YEAR, UNITS], TODAY)).toEqual({ year: "2026", orgUnitIds: "a,b" });
  });

  it("omits empty values from the address", () => {
    expect(valuesToSearchParams({ year: "", month: "3" }).toString()).toBe("month=3");
  });
});
