import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ActionIcon, Alert, Button, Group, Modal, NumberInput, Select, Stack, Text, TextInput } from "@mantine/core";
import { X } from "lucide-react";
import EmployeePicker, { type PickedEmployee } from "@/components/employees/employee-picker";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  createPayItemAssignments,
  deletePayItemAssignment,
  listPayItems,
  type PayItemAssignment,
} from "@/services/payroll-pay-items.service";

export const PAY_ITEM_ASSIGNMENTS_QUERY_KEY = ["payroll", "pay-item-assignments"] as const;
/** Server limit for one bulk assignment (`PayrollLimits.MaxPayItemAssignmentEmployees`). */
export const MAX_ASSIGNMENT_EMPLOYEES = 500;

const pad = (month: number) => String(month).padStart(2, "0");

/** `<input type="month">` value ("YYYY-MM") for a year/month. */
export function toMonthValue(year: number, month: number): string {
  return `${year}-${pad(month)}`;
}

/** Parses an `<input type="month">` value; null when empty or malformed. */
export function parseMonthValue(value: string): { year: number; month: number } | null {
  const match = /^(\d{4})-(\d{2})$/.exec(value);
  if (!match) return null;
  const month = Number(match[2]);
  return month >= 1 && month <= 12 ? { year: Number(match[1]), month } : null;
}

export function formatAssignmentMonth(year: number, month: number): string {
  return `${pad(month)}/${year}`;
}

/** Invalidates assignment lists plus period data (adding/removing an assignment marks payslips as changed). */
export function useRefreshAssignments(): () => Promise<void> {
  const queryClient = useQueryClient();
  return async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: PAY_ITEM_ASSIGNMENTS_QUERY_KEY }),
      queryClient.invalidateQueries({ queryKey: ["payroll", "periods"] }),
    ]);
  };
}

interface PayItemAssignModalProps {
  opened: boolean;
  onClose: () => void;
  /** When set the modal assigns to this single employee (employee card) and hides the picker. */
  fixedEmployee?: PickedEmployee;
  defaultYear: number;
  defaultMonth: number;
}

/** "Ödeme türü ata": one active pay item to one or many employees (single = a list of one), optional value and start/end month. */
export function PayItemAssignModal({ opened, onClose, ...rest }: PayItemAssignModalProps) {
  const { t } = useTranslation(["payroll"]);
  return (
    <Modal opened={opened} onClose={onClose} title={t("payroll:assignments.modal.title")} size="34rem">
      {opened && <AssignBody onClose={onClose} {...rest} />}
    </Modal>
  );
}

function AssignBody({ onClose, fixedEmployee, defaultYear, defaultMonth }: Omit<PayItemAssignModalProps, "opened">) {
  const { t } = useTranslation(["payroll", "common"]);
  const refresh = useRefreshAssignments();
  const [itemId, setItemId] = useState<string | null>(null);
  const [employees, setEmployees] = useState<PickedEmployee[]>(fixedEmployee ? [fixedEmployee] : []);
  const [picker, setPicker] = useState<PickedEmployee | null>(null);
  const [value, setValue] = useState<number | string>("");
  const [fromMonth, setFromMonth] = useState(toMonthValue(defaultYear, defaultMonth));
  const [toMonth, setToMonth] = useState("");
  const [error, setError] = useState<string | null>(null);

  const items = useQuery({ queryKey: ["payroll", "pay-items", "active"], queryFn: () => listPayItems(false) });
  const activeItems = (items.data ?? []).filter((item) => item.isActive !== false && item.direction === "earning");
  const selected = activeItems.find((item) => item.id === itemId);

  const from = parseMonthValue(fromMonth);
  const to = parseMonthValue(toMonth);
  const toInvalid = toMonth !== "" && to === null;
  const canSubmit = selected !== undefined && employees.length > 0 && employees.length <= MAX_ASSIGNMENT_EMPLOYEES && from !== null && !toInvalid;

  const save = useMutation({
    mutationFn: () =>
      createPayItemAssignments({
        payItemDefinitionId: selected!.id,
        employeeIds: employees.map((employee) => employee.id),
        value: value === "" ? null : Number(value),
        fromYear: from!.year,
        fromMonth: from!.month,
        toYear: to?.year ?? null,
        toMonth: to?.month ?? null,
      }),
    onSuccess: async () => {
      await refresh();
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("payroll:assignments.error"))),
  });

  const addPicked = (employee: PickedEmployee | null) => {
    setPicker(null);
    if (employee && !employees.some((existing) => existing.id === employee.id)) {
      setEmployees([...employees, employee]);
    }
  };

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        setError(null);
        save.mutate();
      }}
    >
      <Stack gap="sm">
        <Select
          label={t("payroll:assignments.modal.payItem")}
          placeholder={t("payroll:assignments.modal.payItemPlaceholder")}
          nothingFoundMessage={t("payroll:assignments.modal.noActiveItems")}
          data={activeItems.map((item) => ({ value: item.id, label: `${item.code} - ${item.name}` }))}
          value={itemId}
          onChange={setItemId}
          searchable
          required
        />
        {fixedEmployee ? (
          <Text size="sm">{fixedEmployee.displayName}</Text>
        ) : (
          <div>
            <EmployeePicker label={t("payroll:assignments.modal.addEmployee")} value={picker} onChange={addPicked} />
            {employees.length === 0 ? (
              <Text size="xs" c="dimmed" mt={4}>
                {t("payroll:assignments.modal.noEmployees")}
              </Text>
            ) : (
              <Stack gap={2} mt="xs" data-testid="assign-employees">
                <Text size="xs" c="dimmed">
                  {t("payroll:assignments.modal.selectedCount", { count: employees.length, max: MAX_ASSIGNMENT_EMPLOYEES })}
                </Text>
                {employees.map((employee) => (
                  <Group key={employee.id} justify="space-between" wrap="nowrap">
                    <Text size="sm">{employee.displayName}</Text>
                    <ActionIcon
                      size="sm"
                      variant="subtle"
                      color="red"
                      aria-label={t("payroll:assignments.modal.removeEmployee", { name: employee.displayName })}
                      onClick={() => setEmployees(employees.filter((existing) => existing.id !== employee.id))}
                    >
                      <X size={14} />
                    </ActionIcon>
                  </Group>
                ))}
              </Stack>
            )}
            {employees.length > MAX_ASSIGNMENT_EMPLOYEES && (
              <Text size="xs" c="red" role="alert">
                {t("payroll:assignments.modal.tooMany", { max: MAX_ASSIGNMENT_EMPLOYEES })}
              </Text>
            )}
          </div>
        )}
        <NumberInput
          label={t("payroll:assignments.modal.value")}
          description={
            selected?.defaultValue != null
              ? t("payroll:assignments.modal.valueDefault", { value: selected.defaultValue })
              : t("payroll:assignments.modal.valueHint")
          }
          value={value}
          onChange={setValue}
          decimalScale={4}
          decimalSeparator="."
        />
        <Group grow align="flex-start">
          <TextInput type="month" label={t("payroll:assignments.modal.fromMonth")} value={fromMonth} onChange={(event) => setFromMonth(event.currentTarget.value)} required />
          <TextInput
            type="month"
            label={t("payroll:assignments.modal.toMonth")}
            description={t("payroll:assignments.modal.toMonthHint")}
            value={toMonth}
            onChange={(event) => setToMonth(event.currentTarget.value)}
          />
        </Group>
        {error && <Alert color="red">{error}</Alert>}
        <Group justify="flex-end" mt="xs">
          <Button variant="default" onClick={onClose}>
            {t("common:cancel")}
          </Button>
          <Button type="submit" loading={save.isPending} disabled={!canSubmit}>
            {t("payroll:assignments.modal.save")}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}

interface DeleteConfirmProps {
  assignment: PayItemAssignment | null;
  onClose: () => void;
}

/** Confirmation modal for `DELETE /pay-item-assignments/{id}`; server errors (closed period) are shown inline. */
export function PayItemAssignmentDeleteConfirm({ assignment, onClose }: DeleteConfirmProps) {
  const { t } = useTranslation(["payroll", "common"]);
  const refresh = useRefreshAssignments();
  const [error, setError] = useState<string | null>(null);
  const close = () => {
    setError(null);
    onClose();
  };
  const remove = useMutation({
    mutationFn: (id: string) => deletePayItemAssignment(id),
    onSuccess: async () => {
      await refresh();
      close();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("payroll:assignments.error"))),
  });

  return (
    <Modal opened={assignment !== null} onClose={close} title={t("payroll:assignments.removeConfirmTitle")} size="28rem">
      <Text size="sm">{t("payroll:assignments.removeConfirm", { code: assignment?.code, employee: assignment?.employeeName ?? "" })}</Text>
      {error && (
        <Alert color="red" mt="sm">
          {error}
        </Alert>
      )}
      <Group justify="flex-end" mt="md">
        <Button variant="default" onClick={close}>
          {t("common:cancel")}
        </Button>
        <Button color="red" loading={remove.isPending} onClick={() => assignment && remove.mutate(assignment.id)}>
          {t("payroll:assignments.remove")}
        </Button>
      </Group>
    </Modal>
  );
}
