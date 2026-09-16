import { useMemo, useState, type FormEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { Anchor, Badge, Box, Button, Card, Group, Input, Modal, Select, Skeleton, Stack, Text, Textarea, Title } from "@mantine/core";
import { DatePicker } from "@/components/ui/date-picker";
import ConfirmDialog from "@/components/confirm-dialog";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import { EmploymentPeriodsSection } from "./employment-periods-section";
import {
  addCareerRecord,
  deleteCareerRecord,
  getEmployeeCareer,
  getMyCareer,
  updateCareerRecord,
  type CareerChangeType,
  type CareerRecord,
  type CareerRecordInput,
  type EmployeeDetail,
  type EmployeeOrgLevelOption,
  type EmploymentType,
} from "@/services/employees.service";
import { useAuthStore } from "@/store/auth.store";
import EmployeePicker, { type PickedEmployee } from "./employee-picker";
import { CAREER_CHANGE_TYPE_OPTIONS, EMPLOYMENT_TYPE_OPTIONS } from "./employee-enum-options";
import { useOrgLevels, useOrgUnits } from "./use-org-units";

const UPDATE_PERMISSION = "employee.employees.update";
const NONE = "__none__";

type Levels = EmployeeOrgLevelOption[];

/** Today as a backend DateOnly ("yyyy-MM-dd") in the user's local calendar. */
function todayDateOnly(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

function useUnitLookup() {
  const { options, nameById } = useOrgUnits();
  return useMemo(() => {
    const parentById = new Map<string, string | null>(options.map((u) => [u.id, u.parentId]));
    const isDescendantOf = (unitId: string, ancestorId: string) => {
      const seen = new Set<string>();
      let current: string | null | undefined = unitId;
      while (current && !seen.has(current)) {
        if (current === ancestorId) return true;
        seen.add(current);
        current = parentById.get(current);
      }
      return false;
    };
    return { units: options, nameById, isDescendantOf };
  }, [options, nameById]);
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <Text component="dt" size="xs" c="dimmed">
        {label}
      </Text>
      <Text component="dd" size="sm" mt={2}>
        {children}
      </Text>
    </div>
  );
}

/** "Kariyer": current position (per level), history timeline and the "Pozisyon ekle" dialog. */
export function CareerSection({ employee }: { employee: EmployeeDetail }) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const queryClient = useQueryClient();
  const canRead = usePermission("employee.employees.read");
  const canUpdate = usePermission(UPDATE_PERMISSION);
  const ownEmployeeId = useAuthStore((state) => state.user?.employeeId);
  const isOwnCard = !canRead && ownEmployeeId === employee.id;
  const editable = canUpdate && !isOwnCard;

  const { levels } = useOrgLevels();
  const { nameById } = useUnitLookup();
  const [dialog, setDialog] = useState<{ mode: "add" } | { mode: "edit"; record: CareerRecord } | null>(null);
  const [deleting, setDeleting] = useState<CareerRecord | null>(null);

  const queryKey = ["employees", "career", employee.id];
  const { data: records = [], isLoading } = useQuery({
    queryKey,
    queryFn: () => (isOwnCard ? getMyCareer() : getEmployeeCareer(employee.id)),
  });

  const onTimelineChanged = (updated: CareerRecord[], message: string) => {
    queryClient.setQueryData(queryKey, updated);
    queryClient.invalidateQueries({ queryKey: ["employees", "detail", employee.id] });
    queryClient.invalidateQueries({ queryKey: ["employees"], exact: false, predicate: (q) => q.queryKey[1] !== "career" });
    toast({ variant: "success", title: t("common:success"), description: message });
  };

  const deleteMutation = useMutation({
    mutationFn: (record: CareerRecord) => deleteCareerRecord(employee.id, record.id),
    onSuccess: (updated) => {
      setDeleting(null);
      onTimelineChanged(updated, t("employees:career.deleted"));
    },
    onError: (error) => {
      setDeleting(null);
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("employees:genericError")) });
    },
  });

  const current = records.find((r) => r.isCurrent) ?? null;
  const latest = records[0] ?? null;
  const date = (value: string | null) => (value ? formatDateOnly(value, i18n.language) : "—");
  const unitName = (id: string | undefined) => (id ? (nameById.get(id) ?? t("employees:unknownUnit")) : "—");
  const placementSummary = (record: CareerRecord) =>
    levels
      .map((level) => record.assignments.find((a) => a.orgUnitTypeId === level.id))
      .filter((a): a is NonNullable<typeof a> => Boolean(a))
      .map((a) => unitName(a.orgUnitId))
      .join(" / ") || "—";

  if (isLoading) {
    return <Skeleton h={256} />;
  }

  return (
    <Stack gap="lg">
      <Card withBorder padding="lg">
        <Group justify="space-between" wrap="nowrap" mb="md">
          <Title order={4}>{t("employees:career.currentTitle")}</Title>
          {editable && (
            <Button type="button" size="sm" leftSection={<Plus size={16} />} onClick={() => setDialog({ mode: "add" })}>
              {t("employees:career.addPosition")}
            </Button>
          )}
        </Group>
        <div>
          {!current ? (
            <Text size="sm" c="dimmed">
              {t("employees:career.empty")}
            </Text>
          ) : (
            <dl className="grid gap-4 sm:grid-cols-2">
              {levels.map((level) => (
                <Field key={level.id} label={level.name}>
                  {unitName(current.assignments.find((a) => a.orgUnitTypeId === level.id)?.orgUnitId)}
                </Field>
              ))}
              <Field label={t("employees:fields.manager")}>
                {current.managerEmployeeId ? (
                  <Anchor component={Link} size="sm" to={`/app/employees/${current.managerEmployeeId}`}>
                    {current.managerDisplayName ?? t("employees:unknownEmployee")}
                  </Anchor>
                ) : (
                  "—"
                )}
              </Field>
              <Field label={t("employees:fields.employmentType")}>
                {current.employmentType ? t(`employees:enum.employmentType.${current.employmentType}`) : "—"}
              </Field>
              <Field label={t("employees:fields.positionStartDate")}>{date(current.validFrom)}</Field>
              <Field label={t("employees:career.changeType")}>{t(`employees:enum.careerChangeType.${current.changeType}`)}</Field>
            </dl>
          )}
        </div>
      </Card>

      <Card withBorder padding="lg">
        <Title order={4} mb="md">
          {t("employees:career.historyTitle")}
        </Title>
        {records.length === 0 ? (
          <Text size="sm" c="dimmed">
            {t("employees:career.empty")}
          </Text>
        ) : (
          <Box component="ol" className="relative space-y-4 pl-5" style={{ borderLeft: "1px solid var(--mantine-color-default-border)" }}>
            {records.map((record) => {
              const isLatest = record.id === latest?.id;
              return (
                <li key={record.id} className="relative">
                  <Box
                    component="span"
                    className="absolute -left-[26px] top-1.5 h-2.5 w-2.5"
                    style={{
                      borderRadius: "50%",
                      background: record.isCurrent ? "var(--mantine-primary-color-filled)" : "var(--mantine-color-dimmed)",
                      opacity: record.isCurrent ? 1 : 0.4,
                    }}
                  />
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <Stack gap={4} className="min-w-0">
                      <Group gap="xs" wrap="wrap">
                        <Text size="sm" fw={500}>
                          {t("employees:career.period", {
                            from: date(record.validFrom),
                            to: record.validTo ? date(record.validTo) : t("employees:career.ongoing"),
                          })}
                        </Text>
                        <Badge variant="light">{t(`employees:enum.careerChangeType.${record.changeType}`)}</Badge>
                        {record.isCurrent && <Badge variant="filled">{t("employees:career.current")}</Badge>}
                      </Group>
                      <Text size="sm">{placementSummary(record)}</Text>
                      <Text size="xs" c="dimmed">
                        {t("employees:fields.manager")}: {record.managerDisplayName ?? "—"}
                        {record.employmentType && ` · ${t(`employees:enum.employmentType.${record.employmentType}`)}`}
                      </Text>
                      {record.note && (
                        <Text size="xs" c="dimmed">
                          {record.note}
                        </Text>
                      )}
                    </Stack>
                    {editable && (
                      <Group gap={4}>
                        <Button
                          type="button"
                          variant="subtle"
                          size="sm"
                          leftSection={<Pencil size={16} />}
                          onClick={() => setDialog({ mode: "edit", record })}
                        >
                          {t("common:edit")}
                        </Button>
                        {isLatest && records.length > 1 && (
                          <Button type="button" variant="subtle" size="sm" leftSection={<Trash2 size={16} />} onClick={() => setDeleting(record)}>
                            {t("common:delete")}
                          </Button>
                        )}
                      </Group>
                    )}
                  </div>
                </li>
              );
            })}
          </Box>
        )}
      </Card>

      <EmploymentPeriodsSection employee={employee} />

      {dialog && (
        <CareerRecordDialog
          key={dialog.mode === "edit" ? dialog.record.id : "add"}
          employee={employee}
          levels={levels}
          records={records}
          editing={dialog.mode === "edit" ? dialog.record : null}
          onClose={() => setDialog(null)}
          onSaved={(updated) => {
            setDialog(null);
            onTimelineChanged(updated, t("employees:career.saved"));
          }}
        />
      )}

      <ConfirmDialog
        open={Boolean(deleting)}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={t("employees:career.deleteTitle")}
        description={t("employees:career.deleteDescription")}
        isLoading={deleteMutation.isPending}
        onConfirm={() => deleting && deleteMutation.mutate(deleting)}
      />
    </Stack>
  );
}

interface CareerRecordDialogProps {
  employee: EmployeeDetail;
  levels: Levels;
  /** Timeline, newest first. */
  records: CareerRecord[];
  /** Null = "Pozisyon ekle" (prefilled from the current record). */
  editing: CareerRecord | null;
  onClose: () => void;
  onSaved: (updated: CareerRecord[]) => void;
}

function CareerRecordDialog({ employee, levels, records, editing, onClose, onSaved }: CareerRecordDialogProps) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const { units, isDescendantOf } = useUnitLookup();
  const source = editing ?? records.find((r) => r.isCurrent) ?? records[0] ?? null;
  const latest = records[0] ?? null;
  const isLatest = !editing || editing.id === latest?.id;
  // The record right before the one being edited/added: its start bounds the new start date.
  const previous = editing ? records[records.findIndex((r) => r.id === editing.id) + 1] ?? null : latest;

  const [validFrom, setValidFrom] = useState(editing?.validFrom ?? todayDateOnly());
  const [changeType, setChangeType] = useState<CareerChangeType>(editing?.changeType ?? (records.length === 0 ? "hire" : "promotion"));
  const [employmentType, setEmploymentType] = useState<EmploymentType | null>(source?.employmentType ?? employee.employmentType);
  const [manager, setManager] = useState<PickedEmployee | null>(
    source?.managerEmployeeId ? { id: source.managerEmployeeId, displayName: source.managerDisplayName ?? t("employees:unknownEmployee") } : null
  );
  const [note, setNote] = useState(editing?.note ?? "");
  const [selection, setSelection] = useState<Record<string, string | null>>(() =>
    Object.fromEntries((source?.assignments ?? []).map((a) => [a.orgUnitTypeId, a.orgUnitId]))
  );
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (input: CareerRecordInput) =>
      editing ? updateCareerRecord(employee.id, editing.id, input) : addCareerRecord(employee.id, input),
    onSuccess: onSaved,
    onError: (err) => setError(getApiErrorMessage(err, t("employees:genericError"))),
  });

  /** Units offered for a level: that level's units, narrowed to the subtree of the nearest choice made above it (when that leaves any). */
  const optionsFor = (levelIndex: number) => {
    const ofLevel = units.filter((u) => u.typeId === levels[levelIndex].id);
    const parentChoice = levels
      .slice(0, levelIndex)
      .map((level) => selection[level.id])
      .filter((id): id is string => Boolean(id))
      .pop();
    if (!parentChoice) return ofLevel;
    const narrowed = ofLevel.filter((u) => isDescendantOf(u.id, parentChoice));
    return narrowed.length > 0 ? narrowed : ofLevel;
  };

  const choose = (levelIndex: number, unitId: string | null) => {
    setSelection((prev) => {
      const next = { ...prev, [levels[levelIndex].id]: unitId };
      // Clear deeper choices that no longer sit under the new unit.
      for (const deeper of levels.slice(levelIndex + 1)) {
        const chosen = next[deeper.id];
        if (unitId && chosen && !isDescendantOf(chosen, unitId)) next[deeper.id] = null;
      }
      return next;
    });
  };

  function submit(event: FormEvent) {
    event.preventDefault();
    const formatted = (value: string) => formatDateOnly(value, i18n.language);
    if (!validFrom) return setError(t("employees:career.validation.validFromRequired"));
    if (previous && validFrom <= previous.validFrom)
      return setError(t("employees:career.validation.validFromAfterPrevious", { date: formatted(previous.validFrom) }));
    if (!previous && employee.hireDate && validFrom < employee.hireDate)
      return setError(t("employees:career.validation.validFromBeforeHire", { date: formatted(employee.hireDate) }));

    setError(null);
    mutation.mutate({
      validFrom,
      changeType,
      employmentType,
      managerEmployeeId: manager?.id ?? null,
      note: note.trim() || null,
      assignments: levels
        .filter((level) => selection[level.id])
        .map((level) => ({ orgUnitTypeId: level.id, orgUnitId: selection[level.id] as string })),
    });
  }

  return (
    <Modal
      opened
      onClose={onClose}
      title={editing ? t("employees:career.editPosition") : t("employees:career.addPosition")}
      size="42rem"
    >
      <Text size="sm" c="dimmed" mb="md">
        {t("employees:career.dialogDescription")}
      </Text>
      <form onSubmit={submit}>
        <Stack gap="md">
          {levels.length === 0 && (
            <Text size="sm" c="dimmed">
              {t("employees:career.noLevels")}
            </Text>
          )}
          <div className="grid gap-4 sm:grid-cols-2">
            {levels.map((level, index) => {
              const value = selection[level.id] ?? null;
              const options = optionsFor(index);
              const missing = value && !options.some((u) => u.id === value);
              return (
                <Select
                  key={level.id}
                  id={`career-level-${level.id}`}
                  label={level.name}
                  data={[
                    { value: NONE, label: t("employees:career.noUnitSelected") },
                    ...(missing ? [{ value, label: t("employees:unknownUnit") }] : []),
                    ...options.map((unit) => ({ value: unit.id, label: unit.name })),
                  ]}
                  value={value ?? NONE}
                  onChange={(v) => choose(index, v === null || v === NONE ? null : v)}
                  allowDeselect={false}
                  disabled={mutation.isPending}
                />
              );
            })}
            <EmployeePicker
              id="career-manager"
              label={t("employees:fields.manager")}
              value={manager}
              onChange={setManager}
              excludeId={employee.id}
              allowClear
              disabled={mutation.isPending}
            />
            <Select
              id="career-employmentType"
              label={t("employees:fields.employmentType")}
              data={[
                { value: NONE, label: t("employees:enum.notSet") },
                ...EMPLOYMENT_TYPE_OPTIONS.map((option) => ({ value: option, label: t(`employees:enum.employmentType.${option}`) })),
              ]}
              value={employmentType ?? NONE}
              onChange={(v) => setEmploymentType(v === null || v === NONE ? null : (v as EmploymentType))}
              allowDeselect={false}
              disabled={mutation.isPending}
            />
            <Input.Wrapper
              id="career-validFrom"
              label={t("employees:fields.positionStartDate")}
              withAsterisk
              description={isLatest ? t("employees:career.validFromHint") : t("employees:career.validFromLocked")}
              inputWrapperOrder={["label", "input", "description"]}
            >
              <DatePicker id="career-validFrom" value={validFrom} disabled={mutation.isPending || !isLatest} onChange={setValidFrom} />
            </Input.Wrapper>
            <Select
              id="career-changeType"
              label={t("employees:career.changeType")}
              data={CAREER_CHANGE_TYPE_OPTIONS.map((option) => ({ value: option, label: t(`employees:enum.careerChangeType.${option}`) }))}
              value={changeType}
              onChange={(v) => v !== null && setChangeType(v as CareerChangeType)}
              allowDeselect={false}
              disabled={mutation.isPending}
            />
          </div>
          <Textarea
            id="career-note"
            label={t("employees:career.note")}
            value={note}
            maxLength={500}
            autosize
            minRows={3}
            disabled={mutation.isPending}
            onChange={(e) => setNote(e.currentTarget.value)}
          />
          {error && (
            <Text size="sm" c="red">
              {error}
            </Text>
          )}
          <Group justify="flex-end" gap="sm">
            <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
