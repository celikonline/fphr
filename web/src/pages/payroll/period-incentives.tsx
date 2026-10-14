import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { ActionIcon, Badge, Button, Card, Group, Skeleton, Table, Text, Title } from "@mantine/core";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { usePermission } from "@/components/permission-guard";
import { listIncentiveAssignments, type IncentiveAssignment } from "@/services/payroll-incentives.service";
import type { PayrollPeriod } from "@/services/payroll.service";
import { formatAssignmentMonth } from "./pay-item-assign-modal";
import {
  INCENTIVE_ASSIGNMENTS_QUERY_KEY,
  IncentiveAssignModal,
  IncentiveAssignmentDeleteConfirm,
  IncentiveAssignmentEditModal,
} from "./incentive-assign-modal";

export function PeriodIncentives({ period }: { period: PayrollPeriod }) {
  const canView = usePermission("payroll.trace.view");
  const canManage = usePermission("payroll.incentive.manage");
  if (!canView && !canManage) return null;
  return <PeriodIncentivesBody period={period} canManage={canManage} />;
}

function PeriodIncentivesBody({ period, canManage }: { period: PayrollPeriod; canManage: boolean }) {
  const { t } = useTranslation(["payroll"]);
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<IncentiveAssignment | null>(null);
  const [removing, setRemoving] = useState<IncentiveAssignment | null>(null);
  const canEdit = canManage && period.status === "open";
  const query = useQuery({
    queryKey: [...INCENTIVE_ASSIGNMENTS_QUERY_KEY, "period", period.id],
    queryFn: () => listIncentiveAssignments({ periodId: period.id }),
    retry: false,
  });

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" mb="sm">
        <div>
          <Title order={4}>{t("payroll:incentiveAssignments.employeeTitle")}</Title>
          <Text size="xs" c="dimmed">{t("payroll:incentiveAssignments.periodSubtitle")}</Text>
        </div>
        {canEdit && (
          <Button size="xs" leftSection={<Plus size={14} />} onClick={() => setAdding(true)}>
            {t("payroll:incentiveAssignments.add")}
          </Button>
        )}
      </Group>
      {query.isLoading ? <Skeleton h={64} /> : query.isError || !query.data ? (
        <Text size="sm" c="red">{t("payroll:incentiveAssignments.loadError")}</Text>
      ) : query.data.length === 0 ? (
        <Text size="sm" c="dimmed">{t("payroll:incentiveAssignments.periodEmpty")}</Text>
      ) : (
        <Table.ScrollContainer minWidth={620}>
          <Table verticalSpacing="xs">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("payroll:adjustments.employee")}</Table.Th>
                <Table.Th>{t("payroll:incentives.columns.name")}</Table.Th>
                <Table.Th>{t("payroll:incentives.columns.declarationCode")}</Table.Th>
                <Table.Th>{t("payroll:incentives.columns.approval")}</Table.Th>
                <Table.Th>{t("payroll:assignments.range")}</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {query.data.map((assignment) => (
                <Table.Tr key={assignment.id}>
                  <Table.Td>{assignment.employeeName ?? "—"}</Table.Td>
                  <Table.Td>{assignment.code} — {assignment.name}</Table.Td>
                  <Table.Td>{assignment.declarationCode ?? "—"}</Table.Td>
                  <Table.Td>
                    <Badge color={assignment.isSmmmApproved ? "green" : "yellow"} variant="light">
                      {t(`payroll:incentives.approval.${assignment.isSmmmApproved ? "approved" : "pending"}`)}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    {formatAssignmentMonth(assignment.fromYear, assignment.fromMonth)} — {assignment.toYear != null && assignment.toMonth != null
                      ? formatAssignmentMonth(assignment.toYear, assignment.toMonth) : t("payroll:incentiveAssignments.openEnded")}
                  </Table.Td>
                  <Table.Td>
                    {canEdit && (
                      <Group gap={4} wrap="nowrap">
                        <ActionIcon variant="subtle" aria-label={t("payroll:incentiveAssignments.edit", { code: assignment.code })} onClick={() => setEditing(assignment)}><Pencil size={16} /></ActionIcon>
                        <ActionIcon variant="subtle" color="red" aria-label={t("payroll:incentiveAssignments.removeLabel", { code: assignment.code })} onClick={() => setRemoving(assignment)}><Trash2 size={16} /></ActionIcon>
                      </Group>
                    )}
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      <IncentiveAssignModal opened={adding} onClose={() => setAdding(false)} defaultYear={period.year} defaultMonth={period.month} />
      <IncentiveAssignmentEditModal assignment={editing} onClose={() => setEditing(null)} />
      <IncentiveAssignmentDeleteConfirm assignment={removing} onClose={() => setRemoving(null)} />
    </Card>
  );
}
