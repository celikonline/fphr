import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Paper, Select, Stack, Text } from "@mantine/core";
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
    <Paper withBorder p="md">
      <Stack gap="sm">
        <div>
          <Text size="sm" fw={500}>
            {t("structure:approvalPolicies.title")}
          </Text>
          <Text size="xs" c="dimmed">
            {t("structure:approvalPolicies.hint")}
          </Text>
        </div>

        {optionsUnavailable && (
          <Text size="xs" c="dimmed">
            {t("structure:approvalPolicies.workflowUnavailable")}
          </Text>
        )}

        {rows.map((row) => {
          const own = draft[row.requestType] !== undefined ? draft[row.requestType] : (row.approvalPolicyId ?? null);
          const inheritedFrom = row.inheritedFromOrgUnitId;
          const inheritedName = inheritedFrom ? (unitNameById.get(inheritedFrom) ?? t("structure:approvalPolicies.unknownUnit")) : null;
          const data = [
            { value: INHERIT, label: t("structure:approvalPolicies.inherit") },
            ...options.map((option) => ({ value: option.id, label: option.name })),
            ...(own && !options.some((option) => option.id === own)
              ? [{ value: own, label: t("structure:approvalPolicies.unknownPolicy") }]
              : []),
          ];
          const hint =
            own === null && inheritedName && row.effectiveApprovalPolicyId
              ? options.length > 0
                ? t("structure:approvalPolicies.inheritedFromWithPolicy", {
                    unit: inheritedName,
                    policy: optionName(row.effectiveApprovalPolicyId),
                  })
                : t("structure:approvalPolicies.inheritedFrom", { unit: inheritedName })
              : own === null && !row.effectiveApprovalPolicyId
                ? t("structure:approvalPolicies.defaultFirstManager")
                : undefined;
          return (
            <Select
              key={row.requestType}
              id={`approval-${row.requestType}`}
              label={t(`structure:approvalPolicies.requestType.${row.requestType}`, { defaultValue: row.requestType })}
              description={hint}
              inputWrapperOrder={["label", "input", "description", "error"]}
              data={data}
              value={own ?? INHERIT}
              allowDeselect={false}
              placeholder={t("structure:approvalPolicies.inherit")}
              onChange={(value) => value !== null && setValue(row.requestType, value === INHERIT ? null : value)}
              disabled={disabled || optionsUnavailable || !canManage}
            />
          );
        })}
      </Stack>
    </Paper>
  );
}
