import type { TFunction } from "i18next";
import { getApiErrorStatus, getApiProblem } from "@/lib/api-error";

/** Known backend error codes that get a page-specific message instead of the generic one. */
const CODE_MESSAGES: Record<string, string> = {
  "organization.unit_type.system_immutable": "structure:errors.levelSystemImmutable",
  "organization.unit_type.in_use": "structure:errors.levelInUse",
  "organization.unit.parent_cycle": "structure:errors.parentCycle",
  "organization.location.in_use": "structure:errors.locationInUse",
  "organization.unit.has_active_children": "structure:errors.unitHasChildren",
  "organization.unit.has_active_employees": "structure:errors.unitHasActiveEmployees",
};

/**
 * Picks the most useful message for a failed structure call: a caller status override, a known code,
 * the server's localized `detail`, else the given fallback key.
 */
export function structureErrorMessage(
  error: unknown,
  t: TFunction,
  fallbackKey = "structure:errors.generic",
  statusKeys: Partial<Record<number, string>> = {},
): string {
  const problem = getApiProblem(error);
  const status = getApiErrorStatus(error);
  if (status !== undefined && statusKeys[status]) return t(statusKeys[status]!);
  if (problem?.code && CODE_MESSAGES[problem.code]) return t(CODE_MESSAGES[problem.code]);
  if (problem?.detail && problem.detail !== problem.code) return problem.detail;
  if (status === 403) return t("structure:errors.forbidden");
  return t(fallbackKey);
}
