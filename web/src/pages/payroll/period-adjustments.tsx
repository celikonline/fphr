import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ActionIcon, Alert, Button, Card, Checkbox, Group, Modal, NumberInput, Select, Skeleton, Table, Text, TextInput, Title } from "@mantine/core";
import { Plus, Trash2 } from "lucide-react";
import EmployeePicker, { type PickedEmployee } from "@/components/employees/employee-picker";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatMoney } from "@/lib/money";
import type { PayrollPeriod } from "@/services/payroll.service";
import { PayItemAssignModal } from "./pay-item-assign-modal";
import {
  addPayrollAdjustment,
  listPayrollAdjustments,
  removePayrollAdjustment,
  type PayrollAdjustmentKind,
} from "@/services/payroll.service";

const KINDS: PayrollAdjustmentKind[] = ["bonus", "commission", "performance", "other"];

/**
 * "Ek kazançlar": one-off gross earnings (prim, komisyon, performans) added to employees in this period. They are taxable like the
 * salary and only change while the period is open; adding or removing one marks that payslip changed so it is recalculated.
 */
export function PeriodAdjustments({ period }: { period: PayrollPeriod }) {
  const { t, i18n } = useTranslation(["payroll", "common"]);
  const queryClient = useQueryClient();
  const canManage = usePermission("payroll.adjustment.manage");
  const canManagePayItems = usePermission("payroll.payitem.manage");
  const isOpen = period.status === "open";
  const [adding, setAdding] = useState(false);
  const [addingFormulaItem, setAddingFormulaItem] = useState(false);
  const [employee, setEmployee] = useState<PickedEmployee | null>(null);
  const [kind, setKind] = useState<PayrollAdjustmentKind>("bonus");
  const [label, setLabel] = useState("");
  const [amount, setAmount] = useState<number | string>("");
  const [isNet, setIsNet] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const queryKey = ["payroll", "periods", period.id, "adjustments"];
  const { data, isLoading } = useQuery({ queryKey, queryFn: () => listPayrollAdjustments(period.id) });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ["payroll", "periods"] });

  const closeDialog = () => {
    setAdding(false);
    setEmployee(null);
    setKind("bonus");
    setLabel("");
    setAmount("");
    setIsNet(false);
    setError(null);
  };

  const add = useMutation({
    mutationFn: () => addPayrollAdjustment(period.id, { employeeId: employee!.id, kind, label: label.trim(), grossAmount: Number(amount), isNet }),
    onSuccess: async () => {
      await refresh();
      closeDialog();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("payroll:adjustments.error"))),
  });
  const remove = useMutation({
    mutationFn: (id: string) => removePayrollAdjustment(id),
    onSuccess: refresh,
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("payroll:adjustments.error")) }),
  });

  if (isLoading || !data) {
    return <Skeleton h={96} />;
  }

  const money = (value: number) => formatMoney(value, "TRY", i18n.language);
  const canSubmit = employee !== null && label.trim().length > 0 && Number(amount) > 0;

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" align="flex-start" mb="sm" wrap="wrap">
        <div>
          <Title order={4}>{t("payroll:adjustments.title")}</Title>
          <Text size="xs" c="dimmed">
            {t("payroll:adjustments.subtitle")}
          </Text>
        </div>
        {isOpen && (canManage || canManagePayItems) && (
          <Group gap="xs">
            {canManage && (
              <Button size="xs" leftSection={<Plus size={14} />} onClick={() => setAdding(true)}>
                {t("payroll:adjustments.add")}
              </Button>
            )}
            {canManagePayItems && (
              <Button size="xs" variant="light" leftSection={<Plus size={14} />} onClick={() => setAddingFormulaItem(true)}>
                {t("payroll:adjustments.addFormulaItem", { defaultValue: "Formüllü kazanç/kesinti ekle" })}
              </Button>
            )}
          </Group>
        )}
      </Group>

      {data.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("payroll:adjustments.empty")}
        </Text>
      ) : (
        <Table.ScrollContainer minWidth={480}>
          <Table verticalSpacing="xs" horizontalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("payroll:adjustments.employee")}</Table.Th>
                <Table.Th>{t("payroll:adjustments.kindLabel")}</Table.Th>
                <Table.Th>{t("payroll:adjustments.name")}</Table.Th>
                <Table.Th ta="right">{t("payroll:adjustments.gross")}</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {data.map((item) => (
                <Table.Tr key={item.id}>
                  <Table.Td>{item.employeeName}</Table.Td>
                  <Table.Td>{t(`payroll:adjustments.kind.${item.kind}`)}</Table.Td>
                  <Table.Td>{item.label}</Table.Td>
                  <Table.Td ta="right">
                    {money(item.grossAmount)}
                    {item.isNet && (
                      <Text span size="xs" c="dimmed">
                        {" "}
                        ({t("payroll:adjustments.netTag")})
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td>
                    {canManage && isOpen && (
                      <ActionIcon
                        variant="subtle"
                        color="red"
                        aria-label={t("payroll:adjustments.remove")}
                        loading={remove.isPending && remove.variables === item.id}
                        onClick={() => remove.mutate(item.id)}
                      >
                        <Trash2 size={16} />
                      </ActionIcon>
                    )}
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}

      <Modal opened={adding} onClose={closeDialog} title={t("payroll:adjustments.add")} size="30rem">
        <form
          onSubmit={(event) => {
            event.preventDefault();
            setError(null);
            add.mutate();
          }}
        >
          <EmployeePicker value={employee} onChange={setEmployee} />
          <Select
            mt="sm"
            label={t("payroll:adjustments.kindLabel")}
            data={KINDS.map((value) => ({ value, label: t(`payroll:adjustments.kind.${value}`) }))}
            value={kind}
            onChange={(value) => value && setKind(value as PayrollAdjustmentKind)}
            allowDeselect={false}
          />
          <TextInput mt="sm" label={t("payroll:adjustments.name")} value={label} onChange={(event) => setLabel(event.currentTarget.value)} maxLength={200} required />
          <NumberInput mt="sm" label={t("payroll:adjustments.gross")} value={amount} onChange={setAmount} min={0} decimalScale={2} thousandSeparator="." decimalSeparator="," required />
          <Checkbox mt="sm" label={t("payroll:adjustments.isNet")} description={t("payroll:adjustments.isNetHint")} checked={isNet} onChange={(event) => setIsNet(event.currentTarget.checked)} />
          {error && (
            <Alert color="red" mt="sm">
              {error}
            </Alert>
          )}
          <Group justify="flex-end" mt="md">
            <Button variant="default" onClick={closeDialog}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" loading={add.isPending} disabled={!canSubmit}>
              {t("payroll:adjustments.save")}
            </Button>
          </Group>
        </form>
      </Modal>
      <PayItemAssignModal
        opened={addingFormulaItem}
        onClose={() => setAddingFormulaItem(false)}
        defaultYear={period.year}
        defaultMonth={period.month}
        singleMonth
      />
    </Card>
  );
}
