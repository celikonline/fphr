import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { usePermission } from "@/components/permission-guard";
import {
  APPROVAL_REQUEST_TYPES,
  listApprovalPolicies,
  listUnitApprovalPolicies,
  type ApprovalPolicyOption,
  type OrgUnit,
  type OrgUnitApprovalPolicy,
  type OrgUnitApprovalPolicyInput,
} from "@/services/structure.service";

/** Select value standing for "no own assignment" (inherit from the parent unit). */
const INHERIT = "__inherit__";

export interface ApprovalPoliciesState {
  rows: OrgUnitApprovalPolicy[];
  options: ApprovalPolicyOption[];
  /** Workflow module not reachable (404/403/network): selects render disabled with a hint. */
  optionsUnavailable: boolean;
  isLoading: boolean;
  canManage: boolean;
  draft: Record<string, string | null>;
  setValue: (requestType: string, approvalPolicyId: string | null) => void;
  /** Only the request types the user actually changed, ready for setUnitApprovalPolicies. */
  changes: OrgUnitApprovalPolicyInput[];
}

/**
 * Loads a unit's own/effective approval policies plus the Workflow policy list and tracks the pending edits.
 * The caller decides when to persist `changes` (the unit dialog saves them together with the unit).
 */
export function useUnitApprovalPolicies(unitId: string | undefined): ApprovalPoliciesState {
  const canManage = usePermission("organization.approval_assignments.manage");
  const [draft, setDraft] = useState<Record<string, string | null>>({});

  const rowsQuery = useQuery({
    queryKey: ["structure", "unit-approval-policies", unitId],
    queryFn: () => listUnitApprovalPolicies(unitId!),
    enabled: Boolean(unitId),
    retry: false,
  });

  const optionsQuery = useQuery({
    queryKey: ["workflow", "approval-policies"],
    queryFn: listApprovalPolicies,
    enabled: Boolean(unitId),
    retry: false,
  });

  const rows = useMemo(
    () => rowsQuery.data ?? APPROVAL_REQUEST_TYPES.map((requestType) => ({ requestType }) as OrgUnitApprovalPolicy),
    [rowsQuery.data],
  );

  const changes = useMemo(
    () =>
      rows
        .filter((row) => row.requestType in draft && (draft[row.requestType] ?? null) !== (row.approvalPolicyId ?? null))
        .map((row) => ({ requestType: row.requestType, approvalPolicyId: draft[row.requestType] ?? null })),
    [rows, draft],
  );

  return {
    rows,
    options: optionsQuery.data ?? [],
    optionsUnavailable: optionsQuery.isError,
    isLoading: rowsQuery.isLoading || optionsQuery.isLoading,
    canManage,
    draft,
    setValue: (requestType, approvalPolicyId) => setDraft((prev) => ({ ...prev, [requestType]: approvalPolicyId })),
    changes,
  };
}

interface ApprovalPoliciesSectionProps {
  state: ApprovalPoliciesState;
  /** Units of the tenant, used to name the ancestor an inherited policy comes from. */
  units: OrgUnit[];
  disabled?: boolean;
}

/**
 * "Opsiyonel alanlar → Onay süreçleri" of a unit: one row per request type (İzin / Avans / Harcama / Fazla mesai),
 * showing the unit's own policy and, when it has none, the one inherited from an ancestor.
 */
export default function ApprovalPoliciesSection({ state, units, disabled }: ApprovalPoliciesSectionProps) {
  const { t } = useTranslation(["structure"]);
  const { rows, options, optionsUnavailable, canManage, draft, setValue } = state;
  const unitNameById = useMemo(() => new Map(units.map((u) => [u.id, u.name])), [units]);
  const optionName = (id: string) => options.find((option) => option.id === id)?.name ?? t("structure:approvalPolicies.unknownPolicy");

  return (
    <div className="space-y-3 rounded-md border border-border p-4">
      <div>
        <p className="text-sm font-medium">{t("structure:approvalPolicies.title")}</p>
        <p className="text-xs text-muted-foreground">{t("structure:approvalPolicies.hint")}</p>
      </div>

      {optionsUnavailable && <p className="text-xs text-muted-foreground">{t("structure:approvalPolicies.workflowUnavailable")}</p>}

      {rows.map((row) => {
        const own = draft[row.requestType] !== undefined ? draft[row.requestType] : (row.approvalPolicyId ?? null);
        const inheritedFrom = row.inheritedFromOrgUnitId;
        const inheritedName = inheritedFrom ? (unitNameById.get(inheritedFrom) ?? t("structure:approvalPolicies.unknownUnit")) : null;
        return (
          <div key={row.requestType}>
            <Label htmlFor={`approval-${row.requestType}`}>
              {t(`structure:approvalPolicies.requestType.${row.requestType}`, { defaultValue: row.requestType })}
            </Label>
            <Select
              value={own ?? INHERIT}
              onValueChange={(value) => setValue(row.requestType, value === INHERIT ? null : value)}
              disabled={disabled || optionsUnavailable || !canManage}
            >
              <SelectTrigger id={`approval-${row.requestType}`}>
                <SelectValue placeholder={t("structure:approvalPolicies.inherit")} />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={INHERIT}>{t("structure:approvalPolicies.inherit")}</SelectItem>
                {options.map((option) => (
                  <SelectItem key={option.id} value={option.id}>
                    {option.name}
                  </SelectItem>
                ))}
                {own && !options.some((option) => option.id === own) && (
                  <SelectItem value={own}>{t("structure:approvalPolicies.unknownPolicy")}</SelectItem>
                )}
              </SelectContent>
            </Select>
            {own === null && inheritedName && row.effectiveApprovalPolicyId && (
              <p className="mt-1 text-xs text-muted-foreground">
                {options.length > 0
                  ? t("structure:approvalPolicies.inheritedFromWithPolicy", {
                      unit: inheritedName,
                      policy: optionName(row.effectiveApprovalPolicyId),
                    })
                  : t("structure:approvalPolicies.inheritedFrom", { unit: inheritedName })}
              </p>
            )}
            {own === null && !row.effectiveApprovalPolicyId && (
              <p className="mt-1 text-xs text-muted-foreground">{t("structure:approvalPolicies.defaultFirstManager")}</p>
            )}
          </div>
        );
      })}
    </div>
  );
}
