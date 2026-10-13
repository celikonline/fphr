import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ActionIcon, Alert, Button, Group, Modal, Select, Stack, Text, TextInput } from "@mantine/core";
import { X } from "lucide-react";
import EmployeePicker, { type PickedEmployee } from "@/components/employees/employee-picker";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  createIncentiveAssignments,
  deleteIncentiveAssignment,
  listIncentiveDefinitions,
  updateIncentiveAssignment,
  type IncentiveAssignment,
  type IncentiveDefinition,
} from "@/services/payroll-incentives.service";
import { MAX_ASSIGNMENT_EMPLOYEES, parseMonthValue, toMonthValue } from "./pay-item-assign-modal";

export const INCENTIVE_ASSIGNMENTS_QUERY_KEY = ["payroll", "incentive-assignments"] as const;
export const INCENTIVE_LEGACY_QUERY_KEY = ["payroll", "incentive-legacy"] as const;
const INCENTIVE_SUMMARY_QUERY_KEY = ["company-profile", "incentive-summary"] as const;

/** Invalidates assignment lists plus period data (payslips get flagged), the migration preview and the company summary. */
export function useRefreshIncentiveAssignments(): () => Promise<void> {
  const queryClient = useQueryClient();
  return async () => {
    await Promise.all(
      [INCENTIVE_ASSIGNMENTS_QUERY_KEY, INCENTIVE_LEGACY_QUERY_KEY, INCENTIVE_SUMMARY_QUERY_KEY, ["payroll", "periods"]].map((queryKey) =>
        queryClient.invalidateQueries({ queryKey }),
      ),
    );
  };
}

/** True when the incentive is assigned to the declaration only (kind d) - approval is irrelevant for the calculation. */
export const isDeclarationOnly = (kind: IncentiveDefinition["kind"]): boolean => kind === "declarationOnly";

/** Warnings shown once a definition is picked: unapproved (declared only), MaxMonths and a validity start after the chosen month. */
export function DefinitionAdvice({ definition, fromMonth }: { definition: IncentiveDefinition | undefined; fromMonth: string }) {
  const { t, i18n } = useTranslation(["payroll"]);
  if (!definition) return null;
  const from = parseMonthValue(fromMonth);
  const validFrom = definition.validFrom ? new Date(`${definition.validFrom}T00:00:00`) : null;
  const startsLater = from !== null && validFrom !== null && validFrom > new Date(from.year, from.month - 1, 1);
  return (
    <Stack gap="xs">
      {!isDeclarationOnly(definition.kind) && definition.isSmmmApproved !== true && (
        <Alert color="yellow" data-testid="incentive-unapproved-warning">
          {t("payroll:incentiveAssignments.modal.unapprovedWarning")}
        </Alert>
      )}
      {startsLater && (
        <Alert color="yellow" data-testid="incentive-validfrom-warning">
          {t("payroll:incentiveAssignments.modal.validFromWarning", { date: validFrom.toLocaleDateString(i18n.language) })}
        </Alert>
      )}
    </Stack>
  );
}

interface IncentiveAssignModalProps {
  opened: boolean;
  onClose: () => void;
  /** When set the modal assigns to this single employee (employee card) and hides the picker. */
  fixedEmployee?: PickedEmployee;
  defaultYear: number;
  defaultMonth: number;
}

/** "Teşvik ata": one active definition to one or many employees (single = a list of one), start/end month and a note. */
export function IncentiveAssignModal({ opened, onClose, ...rest }: IncentiveAssignModalProps) {
  const { t } = useTranslation(["payroll"]);
  return (
    <Modal opened={opened} onClose={onClose} title={t("payroll:incentiveAssignments.modal.title")} size="34rem">
      {opened && <AssignBody onClose={onClose} {...rest} />}
    </Modal>
  );
}

function AssignBody({ onClose, fixedEmployee, defaultYear, defaultMonth }: Omit<IncentiveAssignModalProps, "opened">) {
  const { t } = useTranslation(["payroll", "common"]);
  const refresh = useRefreshIncentiveAssignments();
  const [definitionId, setDefinitionId] = useState<string | null>(null);
  const [employees, setEmployees] = useState<PickedEmployee[]>(fixedEmployee ? [fixedEmployee] : []);
  const [picker, setPicker] = useState<PickedEmployee | null>(null);
  const [fromMonth, setFromMonth] = useState(toMonthValue(defaultYear, defaultMonth));
  const [toMonth, setToMonth] = useState("");
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  const definitions = useQuery({ queryKey: ["payroll", "incentive-definitions", "active"], queryFn: () => listIncentiveDefinitions(false) });
  const activeDefinitions = (definitions.data ?? []).filter((item) => item.isActive !== false);
  const selected = activeDefinitions.find((item) => item.id === definitionId);

  const from = parseMonthValue(fromMonth);
  const to = parseMonthValue(toMonth);
  const toInvalid = toMonth !== "" && to === null;
  const endRequired = selected?.maxMonths != null && to === null;
  const canSubmit =
    selected !== undefined && employees.length > 0 && employees.length <= MAX_ASSIGNMENT_EMPLOYEES && from !== null && !toInvalid && !endRequired;

  const save = useMutation({
    mutationFn: () =>
      createIncentiveAssignments({
        incentiveDefinitionId: selected!.id,
        employeeIds: employees.map((employee) => employee.id),
        fromYear: from!.year,
        fromMonth: from!.month,
        toYear: to?.year ?? null,
        toMonth: to?.month ?? null,
        note: note.trim() || null,
      }),
    onSuccess: async () => {
      await refresh();
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("payroll:incentiveAssignments.error"))),
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
          label={t("payroll:incentiveAssignments.modal.definition")}
          placeholder={t("payroll:incentiveAssignments.modal.definitionPlaceholder")}
          nothingFoundMessage={t("payroll:incentiveAssignments.modal.noActiveDefinitions")}
          data={activeDefinitions.map((item) => ({ value: item.id, label: `${item.code} - ${item.name}` }))}
          value={definitionId}
          onChange={setDefinitionId}
          searchable
          required
        />
        <DefinitionAdvice definition={selected} fromMonth={fromMonth} />
        {fixedEmployee ? (
          <Text size="sm">{fixedEmployee.displayName}</Text>
        ) : (
          <div>
            <EmployeePicker label={t("payroll:incentiveAssignments.modal.addEmployee")} value={picker} onChange={addPicked} />
            {employees.length === 0 ? (
              <Text size="xs" c="dimmed" mt={4}>
                {t("payroll:incentiveAssignments.modal.noEmployees")}
              </Text>
            ) : (
              <Stack gap={2} mt="xs" data-testid="incentive-assign-employees">
                <Text size="xs" c="dimmed">
                  {t("payroll:incentiveAssignments.modal.selectedCount", { count: employees.length, max: MAX_ASSIGNMENT_EMPLOYEES })}
                </Text>
                {employees.map((employee) => (
                  <Group key={employee.id} justify="space-between" wrap="nowrap">
                    <Text size="sm">{employee.displayName}</Text>
                    <ActionIcon
                      size="sm"
                      variant="subtle"
                      color="red"
                      aria-label={t("payroll:incentiveAssignments.modal.removeEmployee", { name: employee.displayName })}
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
                {t("payroll:incentiveAssignments.modal.tooMany", { max: MAX_ASSIGNMENT_EMPLOYEES })}
              </Text>
            )}
          </div>
        )}
        <Group grow align="flex-start">
          <TextInput
            type="month"
            label={t("payroll:incentiveAssignments.modal.fromMonth")}
            value={fromMonth}
            onChange={(event) => setFromMonth(event.currentTarget.value)}
            required
          />
          <TextInput
            type="month"
            label={t("payroll:incentiveAssignments.modal.toMonth")}
            description={
              selected?.maxMonths != null
                ? t("payroll:incentiveAssignments.modal.toMonthRequired", { count: selected.maxMonths })
                : t("payroll:incentiveAssignments.modal.toMonthHint")
            }
            value={toMonth}
            onChange={(event) => setToMonth(event.currentTarget.value)}
            required={selected?.maxMonths != null}
          />
        </Group>
        <TextInput
          label={t("payroll:incentiveAssignments.modal.note")}
          description={t("payroll:incentiveAssignments.modal.noteHint")}
          value={note}
          maxLength={500}
          onChange={(event) => setNote(event.currentTarget.value)}
        />
        {error && <Alert color="red">{error}</Alert>}
        <Group justify="flex-end" mt="xs">
          <Button variant="default" onClick={onClose}>
            {t("common:cancel")}
          </Button>
          <Button type="submit" loading={save.isPending} disabled={!canSubmit}>
            {t("payroll:incentiveAssignments.modal.save")}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}

/** Edit an assignment's end month (the note is sent back unchanged: the server overwrites it). */
export function IncentiveAssignmentEditModal({ assignment, onClose }: { assignment: IncentiveAssignment | null; onClose: () => void }) {
  const { t } = useTranslation(["payroll"]);
  return (
    <Modal opened={assignment !== null} onClose={onClose} title={t("payroll:incentiveAssignments.editTitle")} size="28rem">
      {assignment && <EditBody key={assignment.id} assignment={assignment} onClose={onClose} />}
    </Modal>
  );
}

function EditBody({ assignment, onClose }: { assignment: IncentiveAssignment; onClose: () => void }) {
  const { t } = useTranslation(["payroll", "common"]);
  const refresh = useRefreshIncentiveAssignments();
  const [toMonth, setToMonth] = useState(assignment.toYear != null && assignment.toMonth != null ? toMonthValue(assignment.toYear, assignment.toMonth) : "");
  const [error, setError] = useState<string | null>(null);

  const to = parseMonthValue(toMonth);
  const toInvalid = toMonth !== "" && to === null;

  const save = useMutation({
    mutationFn: () => updateIncentiveAssignment(assignment.id, { toYear: to?.year ?? null, toMonth: to?.month ?? null, note: assignment.note ?? null }),
    onSuccess: async () => {
      await refresh();
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("payroll:incentiveAssignments.error"))),
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
        <TextInput
          type="month"
          label={t("payroll:incentiveAssignments.modal.toMonth")}
          description={t("payroll:incentiveAssignments.modal.toMonthHint")}
          value={toMonth}
          onChange={(event) => setToMonth(event.currentTarget.value)}
        />
        {error && <Alert color="red">{error}</Alert>}
        <Group justify="flex-end" mt="xs">
          <Button variant="default" onClick={onClose}>
            {t("common:cancel")}
          </Button>
          <Button type="submit" loading={save.isPending} disabled={toInvalid}>
            {t("payroll:incentiveAssignments.modal.save")}
          </Button>
        </Group>
      </Stack>
    </form>
  );
}

/** Confirmation modal for `DELETE /incentive-assignments/{id}`; server errors (a closed period used it) are shown inline. */
export function IncentiveAssignmentDeleteConfirm({ assignment, onClose }: { assignment: IncentiveAssignment | null; onClose: () => void }) {
  const { t } = useTranslation(["payroll", "common"]);
  const refresh = useRefreshIncentiveAssignments();
  const [error, setError] = useState<string | null>(null);
  const close = () => {
    setError(null);
    onClose();
  };
  const remove = useMutation({
    mutationFn: (id: string) => deleteIncentiveAssignment(id),
    onSuccess: async () => {
      await refresh();
      close();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("payroll:incentiveAssignments.error"))),
  });

  return (
    <Modal opened={assignment !== null} onClose={close} title={t("payroll:incentiveAssignments.removeConfirmTitle")} size="28rem">
      <Text size="sm">{t("payroll:incentiveAssignments.removeConfirm", { code: assignment?.code, employee: assignment?.employeeName ?? "" })}</Text>
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
          {t("payroll:incentiveAssignments.remove")}
        </Button>
      </Group>
    </Modal>
  );
}
