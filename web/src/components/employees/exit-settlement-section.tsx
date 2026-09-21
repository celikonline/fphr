import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ActionIcon, Alert, Button, Card, Group, Modal, Table, Text, Title } from "@mantine/core";
import { Pencil, Plus, RefreshCw, Trash2 } from "lucide-react";
import { toast } from "@/hooks/use-toast";
import { useModuleEnabled } from "@/hooks/use-module-enabled";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import { formatMoney } from "@/lib/money";
import {
  getExitSettlement,
  recalculateExitSettlement,
  removeExitSettlementLine,
  type ExitSettlement,
  type ExitSettlementLine,
} from "@/services/payroll-severance.service";
import type { EmployeeDetail } from "@/services/employees.service";
import { ExitSettlementLineDialog } from "./exit-settlement-line-dialog";

/**
 * "Çıkış hesabı": the kıdem/ihbar/izin amounts Payroll froze when the employee was terminated, plus any lines added since
 * (ikale bedeli, ek ödeme, çıkış ayı ücreti), each netted with SGK, gelir vergisi and damga vergisi. Unlike the wizard's
 * preview it is a record, so later salary or legislation changes never move the frozen amounts. Hidden for active employees,
 * when the Payroll module is off, and when no settlement was recorded (e.g. exits before the feature existed).
 */
export function ExitSettlementSection({ employee }: { employee: EmployeeDetail }) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const queryClient = useQueryClient();
  const payrollEnabled = useModuleEnabled("payroll");
  const isTerminated = employee.status === "Terminated";
  // undefined = dialog closed, null = adding a manual line, a line = editing it.
  const [editing, setEditing] = useState<ExitSettlementLine | null | undefined>(undefined);
  const [removing, setRemoving] = useState<ExitSettlementLine | null>(null);

  const queryKey = ["exit-settlement", employee.id];
  const { data } = useQuery({
    queryKey,
    queryFn: () => getExitSettlement(employee.id),
    enabled: payrollEnabled && isTerminated,
    retry: false,
  });

  const refreshed = {
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey });
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("employees:exitSettlement.success"),
      });
    },
    onError: (err: unknown) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(err, t("employees:genericError")),
      }),
  };
  const recalculate = useMutation({
    mutationFn: (settlement: ExitSettlement) => recalculateExitSettlement(settlement.id),
    ...refreshed,
  });
  const remove = useMutation({
    mutationFn: (line: ExitSettlementLine) => removeExitSettlementLine(data!.id, line.id),
    ...refreshed,
    onSettled: () => setRemoving(null),
  });

  if (!payrollEnabled || !isTerminated || !data) {
    return null;
  }

  const money = (amount: number) => formatMoney(amount, data.currency, i18n.language);
  const lineName = (line: ExitSettlementLine) =>
    line.label || t(`employees:exitSettlement.kinds.${line.kind}`);

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" align="flex-start" mb="xs" wrap="wrap">
        <div>
          <Title order={4}>{t("employees:exitSettlement.title")}</Title>
          <Text size="xs" c="dimmed">
            {t("employees:exitSettlement.hint")}
          </Text>
        </div>
        <Group gap="xs">
          {!data.netCalculated && (
            <Button
              variant="default"
              size="xs"
              leftSection={<RefreshCw size={14} />}
              loading={recalculate.isPending}
              onClick={() => recalculate.mutate(data)}
            >
              {t("employees:exitSettlement.recalculate")}
            </Button>
          )}
          <Button size="xs" leftSection={<Plus size={14} />} onClick={() => setEditing(null)}>
            {t("employees:exitSettlement.addLine")}
          </Button>
        </Group>
      </Group>

      <Text size="sm" mb="sm">
        {t("employees:exitSettlement.terminationDate")}:{" "}
        {formatDateOnly(data.terminationDate, i18n.language)}
      </Text>

      {!data.netCalculated && (
        <Alert color="yellow" mb="sm">
          {t("employees:exitSettlement.netPending")}
        </Alert>
      )}

      <Table.ScrollContainer minWidth={640}>
        <Table verticalSpacing="xs" horizontalSpacing="sm">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>{t("employees:exitSettlement.columns.item")}</Table.Th>
              <Table.Th ta="right">{t("employees:exitSettlement.columns.gross")}</Table.Th>
              <Table.Th ta="right">{t("employees:exitSettlement.columns.sgk")}</Table.Th>
              <Table.Th ta="right">{t("employees:exitSettlement.columns.incomeTax")}</Table.Th>
              <Table.Th ta="right">{t("employees:exitSettlement.columns.stampTax")}</Table.Th>
              <Table.Th ta="right">{t("employees:exitSettlement.columns.net")}</Table.Th>
              <Table.Th />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {data.lines.map((line) => (
              <Table.Tr key={line.id}>
                <Table.Td>{lineName(line)}</Table.Td>
                <Table.Td ta="right">{money(line.grossAmount)}</Table.Td>
                <Table.Td ta="right">{data.netCalculated ? money(line.sgkAmount) : "—"}</Table.Td>
                <Table.Td ta="right">
                  {data.netCalculated ? money(line.incomeTaxAmount) : "—"}
                </Table.Td>
                <Table.Td ta="right">
                  {data.netCalculated ? money(line.stampTaxAmount) : "—"}
                </Table.Td>
                <Table.Td ta="right">{data.netCalculated ? money(line.netAmount) : "—"}</Table.Td>
                <Table.Td>
                  <Group gap={4} justify="flex-end" wrap="nowrap">
                    <ActionIcon
                      variant="subtle"
                      aria-label={t("employees:exitSettlement.edit")}
                      onClick={() => setEditing(line)}
                    >
                      <Pencil size={16} />
                    </ActionIcon>
                    {!line.isAutomatic && (
                      <ActionIcon
                        variant="subtle"
                        color="red"
                        aria-label={t("employees:exitSettlement.remove")}
                        onClick={() => setRemoving(line)}
                      >
                        <Trash2 size={16} />
                      </ActionIcon>
                    )}
                  </Group>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
          <Table.Tfoot>
            <Table.Tr fw={600}>
              <Table.Td>{t("employees:exitSettlement.totalRow")}</Table.Td>
              <Table.Td ta="right">{money(data.totalAmount)}</Table.Td>
              <Table.Td ta="right">{data.netCalculated ? money(data.sgkDeduction) : "—"}</Table.Td>
              <Table.Td ta="right">{data.netCalculated ? money(data.incomeTax) : "—"}</Table.Td>
              <Table.Td ta="right">{data.netCalculated ? money(data.stampTax) : "—"}</Table.Td>
              <Table.Td ta="right">{data.netCalculated ? money(data.netTotal) : "—"}</Table.Td>
              <Table.Td />
            </Table.Tr>
          </Table.Tfoot>
        </Table>
      </Table.ScrollContainer>

      <Text size="xs" c="dimmed" mt="sm">
        {t("employees:exitSettlement.estimateNote")}
      </Text>

      {editing !== undefined && (
        <ExitSettlementLineDialog
          settlement={data}
          line={editing}
          onClose={() => setEditing(undefined)}
        />
      )}

      <Modal
        opened={removing !== null}
        onClose={() => setRemoving(null)}
        title={t("employees:exitSettlement.remove")}
        size="24rem"
      >
        <Text size="sm" mb="md">
          {t("employees:exitSettlement.removeConfirm")}
        </Text>
        <Group justify="flex-end">
          <Button variant="default" onClick={() => setRemoving(null)}>
            {t("employees:exitSettlement.dialog.cancel")}
          </Button>
          <Button
            color="red"
            loading={remove.isPending}
            onClick={() => removing && remove.mutate(removing)}
          >
            {t("employees:exitSettlement.remove")}
          </Button>
        </Group>
      </Modal>
    </Card>
  );
}
