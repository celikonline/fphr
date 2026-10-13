import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import { ActionIcon, Alert, Button, Card, Group, Modal, NumberInput, Skeleton, Stack, Table, Text, TextInput, Title } from "@mantine/core";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { usePermission } from "@/components/permission-guard";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatMoney } from "@/lib/money";
import { DirectionBadge, usePayItemDirections } from "./pay-item-direction";
import type { PayrollPeriod } from "@/services/payroll.service";
import {
  listPayItemAssignments,
  listPeriodPayItemLines,
  updatePayItemAssignment,
  type PayItemAssignment,
} from "@/services/payroll-pay-items.service";
import {
  formatAssignmentMonth,
  PAY_ITEM_ASSIGNMENTS_QUERY_KEY,
  parseMonthValue,
  PayItemAssignModal,
  PayItemAssignmentDeleteConfirm,
  toMonthValue,
  useRefreshAssignments,
} from "./pay-item-assign-modal";

/** Display range of an assignment: "01/2026 - 12/2026" or "01/2026 - ..." (open ended). */
export function formatAssignmentRange(assignment: PayItemAssignment, openEnded: string): string {
  const from = formatAssignmentMonth(assignment.fromYear, assignment.fromMonth);
  const to = assignment.toYear != null && assignment.toMonth != null ? formatAssignmentMonth(assignment.toYear, assignment.toMonth) : openEnded;
  return `${from} - ${to}`;
}

/**
 * "Ödeme türü atamaları": formula pay items assigned to employees for this period, plus the calculated lines of the latest run.
 * Add/edit/delete are only offered while the period is open and with `payroll.payitem.manage`.
 */
export function PeriodPayItems({ period }: { period: PayrollPeriod }) {
  return (
    <>
      <AssignmentsCard period={period} />
      <PayItemLinesCard periodId={period.id} />
    </>
  );
}

function AssignmentsCard({ period }: { period: PayrollPeriod }) {
  const { t, i18n } = useTranslation(["payroll", "common"]);
  const canManage = usePermission("payroll.payitem.manage");
  const canEdit = canManage && period.status === "open";
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<PayItemAssignment | null>(null);
  const [removing, setRemoving] = useState<PayItemAssignment | null>(null);
  const directionOf = usePayItemDirections();

  const { data, isLoading, isError } = useQuery({
    queryKey: [...PAY_ITEM_ASSIGNMENTS_QUERY_KEY, "period", period.id],
    queryFn: () => listPayItemAssignments({ periodId: period.id }),
    retry: false,
  });

  if (isLoading) {
    return <Skeleton h={96} />;
  }

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" align="flex-start" mb="sm" wrap="wrap">
        <div>
          <Title order={4}>{t("payroll:assignments.title")}</Title>
          <Text size="xs" c="dimmed">
            {t("payroll:assignments.subtitle")}
          </Text>
        </div>
        {canEdit && (
          <Button size="xs" leftSection={<Plus size={14} />} onClick={() => setAdding(true)}>
            {t("payroll:assignments.add")}
          </Button>
        )}
      </Group>

      {isError || !data ? (
        <Text size="sm" c="red">
          {t("payroll:assignments.loadError")}
        </Text>
      ) : data.length === 0 ? (
        <Text size="sm" c="dimmed" data-testid="assignments-empty">
          {t("payroll:assignments.empty")}
        </Text>
      ) : (
        <Table.ScrollContainer minWidth={560}>
          <Table verticalSpacing="xs" horizontalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("payroll:assignments.employee")}</Table.Th>
                <Table.Th>{t("payroll:assignments.payItem")}</Table.Th>
                <Table.Th>{t("payroll:payItems.directionColumn")}</Table.Th>
                <Table.Th ta="right">{t("payroll:assignments.value")}</Table.Th>
                <Table.Th>{t("payroll:assignments.range")}</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {data.map((item) => (
                <Table.Tr key={item.id}>
                  <Table.Td>{item.employeeName ?? "—"}</Table.Td>
                  <Table.Td>
                    {item.code}
                    {item.name ? ` - ${item.name}` : ""}
                  </Table.Td>
                  <Table.Td>
                    <DirectionBadge direction={directionOf(item.payItemDefinitionId, item.direction)} />
                  </Table.Td>
                  <Table.Td ta="right">{item.value != null ? new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 4 }).format(item.value) : t("payroll:assignments.defaultValue")}</Table.Td>
                  <Table.Td>{formatAssignmentRange(item, t("payroll:assignments.openEnded"))}</Table.Td>
                  <Table.Td>
                    {canEdit && (
                      <Group gap={4} wrap="nowrap" justify="flex-end">
                        <ActionIcon variant="subtle" aria-label={t("payroll:assignments.edit", { code: item.code })} onClick={() => setEditing(item)}>
                          <Pencil size={16} />
                        </ActionIcon>
                        <ActionIcon variant="subtle" color="red" aria-label={t("payroll:assignments.removeLabel", { code: item.code })} onClick={() => setRemoving(item)}>
                          <Trash2 size={16} />
                        </ActionIcon>
                      </Group>
                    )}
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}

      <PayItemAssignModal opened={adding} onClose={() => setAdding(false)} defaultYear={period.year} defaultMonth={period.month} />
      <EditAssignmentModal assignment={editing} onClose={() => setEditing(null)} />
      <PayItemAssignmentDeleteConfirm assignment={removing} onClose={() => setRemoving(null)} />
    </Card>
  );
}

function EditAssignmentModal({ assignment, onClose }: { assignment: PayItemAssignment | null; onClose: () => void }) {
  const { t } = useTranslation(["payroll"]);
  return (
    <Modal opened={assignment !== null} onClose={onClose} title={t("payroll:assignments.editTitle")} size="28rem">
      {assignment && <EditBody key={assignment.id} assignment={assignment} onClose={onClose} />}
    </Modal>
  );
}

function EditBody({ assignment, onClose }: { assignment: PayItemAssignment; onClose: () => void }) {
  const { t } = useTranslation(["payroll", "common"]);
  const refresh = useRefreshAssignments();
  const [value, setValue] = useState<number | string>(assignment.value ?? "");
  const [toMonth, setToMonth] = useState(assignment.toYear != null && assignment.toMonth != null ? toMonthValue(assignment.toYear, assignment.toMonth) : "");
  const [error, setError] = useState<string | null>(null);

  const to = parseMonthValue(toMonth);
  const toInvalid = toMonth !== "" && to === null;

  const save = useMutation({
    mutationFn: () =>
      updatePayItemAssignment(assignment.id, {
        value: value === "" ? null : Number(value),
        toYear: to?.year ?? null,
        toMonth: to?.month ?? null,
      }),
    onSuccess: async () => {
      await refresh();
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("payroll:assignments.error"))),
  });

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        setError(null);
        save.mutate();
      }}
    >
      <Stack gap="sm">
        <Text size="sm">
          {assignment.employeeName ?? "—"} · {assignment.code}
        </Text>
        <NumberInput label={t("payroll:assignments.modal.value")} value={value} onChange={setValue} decimalScale={4} decimalSeparator="." />
        <TextInput
          type="month"
          label={t("payroll:assignments.modal.toMonth")}
          description={t("payroll:assignments.modal.toMonthHint")}
          value={toMonth}
          onChange={(event) => setToMonth(event.currentTarget.value)}
        />
        {error && <Alert color="red">{error}</Alert>}
        <Group justify="flex-end" mt="xs">
          <Button variant="default" onClick={onClose}>
            {t("common:cancel")}
          </Button>
          <Button type="submit" loading={save.isPending} disabled={toInvalid}>
            {t("payroll:assignments.modal.save")}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}

function PayItemLinesCard({ periodId }: { periodId: string }) {
  const { t, i18n } = useTranslation(["payroll"]);
  const { data, isLoading, isError } = useQuery({
    queryKey: ["payroll", "periods", periodId, "pay-item-lines"],
    queryFn: () => listPeriodPayItemLines(periodId),
    retry: false,
  });
  const money = (value: number) => formatMoney(value, "TRY", i18n.language);

  if (isLoading) {
    return <Skeleton h={64} />;
  }

  return (
    <Card withBorder padding="lg">
      <Title order={4}>{t("payroll:payItemLines.title")}</Title>
      <Text size="xs" c="dimmed" mb="sm">
        {t("payroll:payItemLines.subtitle")}
      </Text>
      {isError || !data ? (
        <Text size="sm" c="red" data-testid="pay-item-lines-error">
          {t("payroll:loadErrors.payItemLines")}
        </Text>
      ) : data.length === 0 ? (
        <Text size="sm" c="dimmed" data-testid="pay-item-lines-empty">
          {t("payroll:payItemLines.empty")}
        </Text>
      ) : (
        <Table.ScrollContainer minWidth={560}>
          <Table verticalSpacing="xs" horizontalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("payroll:payItemLines.employee")}</Table.Th>
                <Table.Th>{t("payroll:payItemLines.payItem")}</Table.Th>
                <Table.Th>{t("payroll:payItems.directionColumn")}</Table.Th>
                <Table.Th ta="right">{t("payroll:payItemLines.amount")}</Table.Th>
                <Table.Th ta="right">{t("payroll:payItemLines.netTarget")}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {data.map((line, index) => {
                const deduction = line.direction === "deduction";
                return (
                <Table.Tr key={`${line.employeeId}-${line.code}-${index}`}>
                  <Table.Td>{line.employeeName ?? "—"}</Table.Td>
                  <Table.Td>
                    {line.code}
                    {line.name ? ` - ${line.name}` : ""}
                  </Table.Td>
                  <Table.Td>
                    <DirectionBadge direction={deduction ? "deduction" : "earning"} />
                  </Table.Td>
                  <Table.Td ta="right" c={deduction ? "red" : undefined} data-testid={deduction ? "deduction-amount" : undefined}>
                    {line.amount != null ? (deduction ? `−${money(Math.abs(line.amount))}` : money(line.amount)) : "—"}
                  </Table.Td>
                  <Table.Td ta="right">{line.netTarget != null ? money(line.netTarget) : "—"}</Table.Td>
                </Table.Tr>
                );
              })}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
    </Card>
  );
}
