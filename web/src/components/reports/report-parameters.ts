import { toDateOnly } from "@/lib/dates";
import { REPORT_PARAMETER_KEYS, type ReportParameter } from "@/services/reports.service";

/** Filter values keyed by wire key (`year`, `from`, `to`, `orgUnitIds`, …); a `dateRange` parameter is stored as `from` + `to`. */
export type ParameterValues = Record<string, string>;

export function defaultValuesFor(parameters: ReportParameter[], today: Date): ParameterValues {
  const values: ParameterValues = {};
  for (const parameter of parameters) {
    if (!parameter.isRequired) continue;
    if (parameter.kind === "year") values[REPORT_PARAMETER_KEYS.year] = String(today.getFullYear());
    if (parameter.kind === "dateRange") {
      values[REPORT_PARAMETER_KEYS.from] = toDateOnly(new Date(today.getFullYear(), today.getMonth(), 1));
      values[REPORT_PARAMETER_KEYS.to] = toDateOnly(new Date(today.getFullYear(), today.getMonth() + 1, 0));
    }
  }
  return values;
}

export function isSatisfied(parameter: ReportParameter, values: ParameterValues): boolean {
  if (!parameter.isRequired) return true;
  if (parameter.kind === "dateRange") return Boolean(values[REPORT_PARAMETER_KEYS.from] && values[REPORT_PARAMETER_KEYS.to]);
  return Boolean(values[parameter.key]);
}

export function allSatisfied(parameters: ReportParameter[], values: ParameterValues): boolean {
  return parameters.every((parameter) => isSatisfied(parameter, values));
}

/** The wire keys a report's filters travel under (a date range is `from` + `to`). */
function wireKeys(parameters: ReportParameter[]): string[] {
  return parameters.flatMap((parameter) =>
    parameter.kind === "dateRange" ? [REPORT_PARAMETER_KEYS.from, REPORT_PARAMETER_KEYS.to] : [parameter.key],
  );
}

/** Filters → address query string (blank values omitted). Carries no personal data: unit ids, year/month, dates, a flag. */
export function valuesToSearchParams(values: ParameterValues): URLSearchParams {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(values)) {
    if (value) search.set(key, value);
  }
  return search;
}

/** Address query string → filters: defaults first, then any value present for a filter this report accepts. */
export function valuesFromSearchParams(search: URLSearchParams, parameters: ReportParameter[], today: Date): ParameterValues {
  const values = defaultValuesFor(parameters, today);
  for (const key of wireKeys(parameters)) {
    const value = search.get(key);
    if (value) values[key] = value;
  }
  return values;
}
