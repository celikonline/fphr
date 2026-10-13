import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { ActionIcon, Badge, Button, Card, Group, Skeleton, Table, Text, Title } from "@mantine/core";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { usePermission } from "@/components/permission-guard";
import {
  INCENTIVE_ASSIGNMENTS_QUERY_KEY,
  IncentiveAssignModal,
  IncentiveAssignmentDeleteConfirm,
  IncentiveAssignmentEditModal,
  isDeclarationOnly,
} from "@/pages/payroll/incentive-assign-modal";
import { formatAssignmentMonth } from "@/pages/payroll/pay-item-assign-modal";
import { listIncentiveAssignments, type IncentiveAssignment } from "@/services/payroll-incentives.service";
import type { EmployeeDetail } from "@/services/employees.service";

/** Shared query: the employee's incentive assignments (the profile card derives its "legacy field" hint from the same list). */
export function useEmployeeIncentiveAssignments(employeeId: string, enabled = true) {
  return useQuery({
    queryKey: [...INCENTIVE_ASSIGNMENTS_QUERY_KEY, "employee", employeeId],
    queryFn: () => listIncentiveAssignments({ employeeId }),
    enabled,
    retry: false,
  });
}

/** True when an SGK-category assignment (every kind but the income tax relief) covers the given month. */
export function hasEffectiveSgkAssignment(assignments: IncentiveAssignment[] | undefined, now: Date): boolean {
  const current = now.getFullYear() * 12 + now.getMonth();
  return (assignments ?? []).some((item) => {
    if (item.kind === "incomeTaxWithholdingRelief") return false;
    const from = item.fromYear * 12 + item.fromMonth - 1;
    const to = item.toYear != null && item.toMonth != null ? item.toYear * 12 + item.toMonth - 1 : Number.POSITIVE_INFINITY;
    return from <= current && current <= to;
  });
}

function formatRange(item: IncentiveAssignment, openEnded: string): string {
  const to = item.toYear != null && item.toMonth != null ? formatAssignmentMonth(item.toYear, item.toMonth) : openEnded;
  return `${formatAssignmentMonth(item.fromYear, item.fromMonth)} - ${to}`;
}

/**
 * Employee card > Bordro: the employee's incentive assignments. Read-only with `payroll.trace.view` (the section's gate);
 * add / end month / remove need `payroll.incentive.manage`.
 */
export function EmployeeIncentivesCard({ employee }: { employee: Pick<EmployeeDetail, "id" | "displayName"> }) {
  const { t } = useTranslation(["payroll"]);
  const canManage = usePermission("payroll.incentive.manage");
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<IncentiveAssignment | null>(null);
  const [removing, setRemoving] = useState<IncentiveAssignment | null>(null);
  const { data, isLoading, isError } = useEmployeeIncentiveAssignments(employee.id);
  const now = new Date();

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" align="flex-start" mb="sm" wrap="wrap">
        <Title order={4}>{t("payroll:incentiveAssignments.employeeTitle")}</Title>
        {canManage && (
          <Button size="xs" leftSection={<Plus size={14} />} onClick={() => setAdding(true)}>
            {t("payroll:incentiveAssignments.add")}
          </Button>
        )}
      </Group>
      {isLoading ? (
        <Skeleton h={64} />
      ) : isError || !data ? (
        <Text size="sm" c="red">
          {t("payroll:incentiveAssignments.loadError")}
        </Text>
      ) : data.length === 0 ? (
        <Text size="sm" c="dimmed" data-testid="employee-incentives-empty">
          {t("payroll:incentiveAssignments.employeeEmpty")}
        </Text>
      ) : (
        <Table.ScrollContainer minWidth={640}>
          <Table verticalSpacing="xs" horizontalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("payroll:incentiveAssignments.columns.incentive")}</Table.Th>
                <Table.Th>{t("payroll:incentiveAssignments.columns.declarationCode")}</Table.Th>
                <Table.Th>{t("payroll:incentiveAssignments.columns.kind")}</Table.Th>
                <Table.Th>{t("payroll:incentiveAssignments.columns.approval")}</Table.Th>
                <Table.Th>{t("payroll:incentiveAssignments.columns.range")}</Table.Th>
                <Table.Th>{t("payroll:incentiveAssignments.columns.note")}</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {data.map((item) => {
                const approved = item.isSmmmApproved ?? false;
                return (
                  <Table.Tr key={item.id}>
                    <Table.Td>
                      {item.code}
                      {item.name ? ` - ${item.name}` : ""}
                    </Table.Td>
                    <Table.Td>
                      <Text ff="monospace" size="sm">
                        {item.declarationCode ?? "-"}
                      </Text>
                    </Table.Td>
                    <Table.Td>{t(`payroll:incentives.kind.${item.kind}`, { defaultValue: item.kind })}</Table.Td>
                    <Table.Td>
                      <Badge color={approved ? "green" : "yellow"} variant="light">
                        {approved ? t("payroll:incentives.approval.approved") : t("payroll:incentives.approval.pending")}
                      </Badge>
                      {!approved && !isDeclarationOnly(item.kind) && (
                        <Text size="xs" c="orange" data-testid="incentive-unapproved-hint">
                          {t("payroll:incentiveAssignments.unapprovedHint")}
                        </Text>
                      )}
                    </Table.Td>
                    <Table.Td>{formatRange(item, t("payroll:incentiveAssignments.openEnded"))}</Table.Td>
                    <Table.Td>{item.note ?? "-"}</Table.Td>
                    <Table.Td>
                      {canManage && (
                        <Group gap={4} wrap="nowrap" justify="flex-end">
                          <ActionIcon variant="subtle" aria-label={t("payroll:incentiveAssignments.edit", { code: item.code })} onClick={() => setEditing(item)}>
                            <Pencil size={16} />
                          </ActionIcon>
                          <ActionIcon
                            variant="subtle"
                            color="red"
                            aria-label={t("payroll:incentiveAssignments.removeLabel", { code: item.code })}
                            onClick={() => setRemoving(item)}
                          >
                            <Trash2 size={16} />
                          </ActionIcon>
                        </Group>
                      )}
                    </Table.Td>
                  </Table.Tr>
                );
              })}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      {canManage && (
        <>
          <IncentiveAssignModal
            opened={adding}
            onClose={() => setAdding(false)}
            fixedEmployee={{ id: employee.id, displayName: employee.displayName }}
            defaultYear={now.getFullYear()}
            defaultMonth={now.getMonth() + 1}
          />
          <IncentiveAssignmentEditModal assignment={editing} onClose={() => setEditing(null)} />
          <IncentiveAssignmentDeleteConfirm assignment={removing} onClose={() => setRemoving(null)} />
        </>
      )}
    </Card>
  );
}
