import { useEffect, useMemo, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import {
  Badge,
  Button,
  Checkbox,
  CloseButton,
  Group,
  Modal,
  Paper,
  ScrollArea,
  Skeleton,
  Stack,
  Text,
  TextInput,
  Title,
  Tooltip,
} from "@mantine/core";
import { CalendarClock, Pencil, Plus, Search, Star, Trash2, UserPlus } from "lucide-react";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
import { DatePicker } from "@/components/ui/date-picker";
import { TimePicker } from "@/components/ui/time-picker";
import ConfirmDialog from "@/components/confirm-dialog";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { listEmployees } from "@/services/employees.service";
import {
  assignWorkSchedule,
  createWorkSchedule,
  deleteWorkSchedule,
  renameWorkSchedule,
  setDefaultWorkSchedule,
  setWorkScheduleActive,
  updateWorkScheduleDays,
  listWorkSchedules,
  WEEK_DAYS_MONDAY_FIRST,
  type WeekDay,
  type WorkSchedule,
  type WorkScheduleDay,
} from "@/services/attendance.service";

const SCHEDULES_KEY = ["attendance", "work-schedules"] as const;
const NAME_MAX_LENGTH = 200;

/** "HH:mm:ss" | null → "HH:mm" for `TimePicker`. */
function toUiTime(value: string | null | undefined): string {
  return value ? value.slice(0, 5) : "";
}

/** "HH:mm" (or "") → "HH:mm:ss" | null for the API. */
function toApiTime(value: string): string | null {
  return value ? `${value}:00` : null;
}

function defaultDays(): WorkScheduleDay[] {
  return WEEK_DAYS_MONDAY_FIRST.map((dayOfWeek) => {
    const isWorkingDay = dayOfWeek !== "saturday" && dayOfWeek !== "sunday";
    return {
      dayOfWeek,
      isWorkingDay,
      start: isWorkingDay ? "09:00:00" : null,
      end: isWorkingDay ? "18:00:00" : null,
      breakStart: isWorkingDay ? "12:30:00" : null,
      breakEnd: isWorkingDay ? "13:30:00" : null,
    };
  });
}

/** "Ayarlar → Kurallar → Çalışma takvimleri" (11.3.6): CRUD + varsayılan/aktiflik, çalışan ataması ayrı akışta. */
export default function WorkSchedulesPage() {
  const canView = usePermission("attendance.workschedule.view");
  if (!canView) return <NoAccess />;
  return <WorkSchedules />;
}

function WorkSchedules() {
  const { t } = useTranslation(["attendance", "common"]);
  const canManage = usePermission("attendance.workschedule.manage");
  const canAssign = usePermission("attendance.workschedule.assign");
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: SCHEDULES_KEY, queryFn: () => listWorkSchedules(true) });
  const schedules = [...(query.data ?? [])].sort((a, b) => Number(b.isDefault) - Number(a.isDefault) || a.name.localeCompare(b.name));

  const [creating, setCreating] = useState(false);
  const [renaming, setRenaming] = useState<WorkSchedule | null>(null);
  const [editingDays, setEditingDays] = useState<WorkSchedule | null>(null);
  const [assigning, setAssigning] = useState<WorkSchedule | null>(null);
  const [deleting, setDeleting] = useState<WorkSchedule | null>(null);

  const onError = (error: unknown) =>
    toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("attendance:genericError")) });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: SCHEDULES_KEY });

  const setDefaultMutation = useMutation({
    mutationFn: (schedule: WorkSchedule) => setDefaultWorkSchedule(schedule.id),
    onSuccess: async () => {
      await invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("attendance:workSchedules.defaultSet") });
    },
    onError,
  });

  const setActiveMutation = useMutation({
    mutationFn: (schedule: WorkSchedule) => setWorkScheduleActive(schedule.id, !schedule.isActive),
    onSuccess: invalidate,
    onError,
  });

  const deleteMutation = useMutation({
    mutationFn: (schedule: WorkSchedule) => deleteWorkSchedule(schedule.id),
    onSuccess: async () => {
      setDeleting(null);
      await invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("attendance:workSchedules.deleted") });
    },
    onError: (error) => {
      setDeleting(null);
      onError(error);
    },
  });

  const columns: ColumnDef<WorkSchedule>[] = [
    {
      id: "name",
      accessorFn: (row) => row.name,
      enableHiding: false,
      header: t("attendance:workSchedules.col.name"),
      meta: { label: t("attendance:workSchedules.col.name") },
      cell: ({ row }) => (
        <Group gap={6} wrap="nowrap">
          <Text span inherit fw={500}>
            {row.original.name}
          </Text>
          {row.original.isDefault && <Badge size="sm">{t("attendance:workSchedules.default")}</Badge>}
          {!row.original.isActive && (
            <Badge size="sm" variant="outline" color="gray">
              {t("attendance:workSchedules.inactive")}
            </Badge>
          )}
        </Group>
      ),
    },
    {
      id: "days",
      accessorFn: (row) => row.days.filter((d) => d.isWorkingDay).length,
      header: t("attendance:workSchedules.col.days"),
      meta: { label: t("attendance:workSchedules.col.days") },
      cell: ({ row }) => t("attendance:workSchedules.workingDayCount", { count: row.original.days.filter((d) => d.isWorkingDay).length }),
    },
    {
      id: "weeklyMinutes",
      accessorFn: (row) => row.weeklyMinutes,
      header: t("attendance:workSchedules.col.weeklyHours"),
      meta: { label: t("attendance:workSchedules.col.weeklyHours") },
      cell: ({ row }) => {
        const hours = Math.floor(row.original.weeklyMinutes / 60);
        const rest = row.original.weeklyMinutes % 60;
        return rest === 0
          ? t("attendance:duration.hoursOnly", { h: hours })
          : t("attendance:duration.hoursMinutes", { h: hours, m: rest });
      },
    },
    {
      id: "assigned",
      accessorFn: (row) => row.assignedEmployeeCount,
      header: t("attendance:workSchedules.col.assigned"),
      meta: { label: t("attendance:workSchedules.col.assigned") },
      cell: ({ row }) => row.original.assignedEmployeeCount,
    },
    ...(canManage || canAssign
      ? [
          {
            id: "actions",
            header: "",
            meta: { label: t("common:actions"), headerClassName: "w-64" },
            enableSorting: false,
            cell: ({ row }: { row: { original: WorkSchedule } }) => {
              const schedule = row.original;
              const deleteBlocked = schedule.isDefault
                ? t("attendance:workSchedules.deleteBlockedDefault")
                : schedule.assignedEmployeeCount > 0
                  ? t("attendance:workSchedules.deleteBlockedEmployees", { count: schedule.assignedEmployeeCount })
                  : null;
              return (
                <Group gap="xs" justify="flex-end" wrap="nowrap">
                  {canAssign && (
                    <Button type="button" size="xs" variant="subtle" leftSection={<UserPlus size={14} />} onClick={() => setAssigning(schedule)}>
                      {t("attendance:workSchedules.assign")}
                    </Button>
                  )}
                  {canManage && (
                    <>
                      <Button type="button" size="xs" variant="subtle" leftSection={<Pencil size={14} />} onClick={() => setEditingDays(schedule)}>
                        {t("attendance:workSchedules.editDays")}
                      </Button>
                      <Button type="button" size="xs" variant="subtle" onClick={() => setRenaming(schedule)}>
                        {t("common:edit")}
                      </Button>
                    </>
                  )}
                  {canManage && !schedule.isDefault && (
                    <Button
                      type="button"
                      size="xs"
                      variant="subtle"
                      leftSection={<Star size={14} />}
                      loading={setDefaultMutation.isPending && setDefaultMutation.variables?.id === schedule.id}
                      onClick={() => setDefaultMutation.mutate(schedule)}
                    >
                      {t("attendance:workSchedules.makeDefault")}
                    </Button>
                  )}
                  {canManage && (
                    <Button
                      type="button"
                      size="xs"
                      variant="subtle"
                      color={schedule.isActive ? "gray" : "teal"}
                      loading={setActiveMutation.isPending && setActiveMutation.variables?.id === schedule.id}
                      onClick={() => setActiveMutation.mutate(schedule)}
                    >
                      {schedule.isActive ? t("attendance:workSchedules.deactivate") : t("attendance:workSchedules.activate")}
                    </Button>
                  )}
                  {canManage &&
                    (deleteBlocked ? (
                      <Tooltip label={deleteBlocked} multiline maw={260}>
                        <span tabIndex={0} aria-label={deleteBlocked}>
                          <Button type="button" size="xs" variant="subtle" color="red" disabled style={{ pointerEvents: "none" }} leftSection={<Trash2 size={14} />}>
                            {t("common:delete")}
                          </Button>
                        </span>
                      </Tooltip>
                    ) : (
                      <Button type="button" size="xs" variant="subtle" color="red" leftSection={<Trash2 size={14} />} onClick={() => setDeleting(schedule)}>
                        {t("common:delete")}
                      </Button>
                    ))}
                </Group>
              );
            },
          } satisfies ColumnDef<WorkSchedule>,
        ]
      : []),
  ];

  const table = useDataTable({
    columns,
    data: schedules,
    getRowId: (row) => row.id,
    columnVisibilityKey: "senseik.attendance.workSchedules.columns",
  });

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" wrap="wrap">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <CalendarClock size={24} /> {t("attendance:workSchedules.title")}
          </Title>
          <Text c="dimmed">{t("attendance:workSchedules.subtitle")}</Text>
        </div>
        {canManage && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={() => setCreating(true)}>
            {t("attendance:workSchedules.addTitle")}
          </Button>
        )}
      </Group>

      {query.isError ? (
        <Text size="sm" c="red">
          {getApiErrorMessage(query.error, t("attendance:genericError"))}
        </Text>
      ) : (
        <Stack gap="xs">
          <Group justify="flex-end">
            <DataTableViewOptions table={table} className="h-9 px-3" />
          </Group>
          <div className="overflow-x-auto">
            <DataTable table={table} isLoading={query.isLoading} />
          </div>
        </Stack>
      )}

      {creating && <ScheduleFormDialog mode="create" onClose={() => setCreating(false)} />}
      {renaming && <RenameDialog schedule={renaming} onClose={() => setRenaming(null)} />}
      {editingDays && <ScheduleFormDialog mode="edit" schedule={editingDays} onClose={() => setEditingDays(null)} />}
      {assigning && <AssignWorkScheduleDialog schedule={assigning} onClose={() => setAssigning(null)} />}

      <ConfirmDialog
        open={Boolean(deleting)}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={t("attendance:workSchedules.deleteTitle")}
        description={t("attendance:workSchedules.deleteDescription", { name: deleting?.name ?? "" })}
        isLoading={deleteMutation.isPending}
        onConfirm={() => deleting && deleteMutation.mutate(deleting)}
      />
    </div>
  );
}

function RenameDialog({ schedule, onClose }: { schedule: WorkSchedule; onClose: () => void }) {
  const { t } = useTranslation(["attendance", "common"]);
  const queryClient = useQueryClient();
  const [name, setName] = useState(schedule.name.slice(0, NAME_MAX_LENGTH));
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (value: string) => renameWorkSchedule(schedule.id, value),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: SCHEDULES_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("attendance:workSchedules.renamed") });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("attendance:genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return setError(t("attendance:workSchedules.nameRequired"));
    setError(null);
    mutation.mutate(name.trim());
  }

  return (
    <Modal opened onClose={onClose} size="28rem" title={t("attendance:workSchedules.renameTitle")}>
      <form onSubmit={submit}>
        <Stack gap="md">
          <TextInput
            id="work-schedule-name"
            label={t("attendance:workSchedules.name")}
            withAsterisk
            value={name}
            maxLength={NAME_MAX_LENGTH}
            disabled={mutation.isPending}
            onChange={(e) => setName(e.currentTarget.value)}
          />
          {error && (
            <Text size="sm" c="red" role="alert">
              {error}
            </Text>
          )}
          <Group justify="flex-end" gap="xs">
            <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" loading={mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}

/** "Yeni çalışma takvimi ekle" / "Günleri düzenle": name (create only) + 7 günlük saat tablosu. */
function ScheduleFormDialog({ mode, schedule, onClose }: { mode: "create" | "edit"; schedule?: WorkSchedule; onClose: () => void }) {
  const { t } = useTranslation(["attendance", "common"]);
  const queryClient = useQueryClient();
  const [name, setName] = useState(schedule?.name.slice(0, NAME_MAX_LENGTH) ?? "");
  const [includeSaturday, setIncludeSaturday] = useState(schedule?.includeSaturdayWhenFullWeek ?? true);
  const [days, setDays] = useState<WorkScheduleDay[]>(() => {
    if (!schedule) return defaultDays();
    const byDay = new Map(schedule.days.map((d) => [d.dayOfWeek, d]));
    return WEEK_DAYS_MONDAY_FIRST.map((dayOfWeek) => byDay.get(dayOfWeek) ?? { dayOfWeek, isWorkingDay: false, start: null, end: null, breakStart: null, breakEnd: null });
  });
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () =>
      mode === "create"
        ? createWorkSchedule({ name: name.trim(), days, includeSaturdayWhenFullWeek: includeSaturday })
        : updateWorkScheduleDays(schedule!.id, { days, includeSaturdayWhenFullWeek: includeSaturday }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: SCHEDULES_KEY });
      toast({
        variant: "success",
        title: t("common:success"),
        description: t(mode === "create" ? "attendance:workSchedules.created" : "attendance:workSchedules.daysUpdated"),
      });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("attendance:genericError"))),
  });

  function updateDay(dayOfWeek: WeekDay, patch: Partial<WorkScheduleDay>) {
    setDays((current) => current.map((d) => (d.dayOfWeek === dayOfWeek ? { ...d, ...patch } : d)));
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    if (mode === "create" && !name.trim()) return setError(t("attendance:workSchedules.nameRequired"));
    setError(null);
    mutation.mutate();
  }

  return (
    <Modal opened onClose={onClose} size="42rem" title={t(mode === "create" ? "attendance:workSchedules.addTitle" : "attendance:workSchedules.editDaysTitle")}>
      <form onSubmit={submit}>
        <Stack gap="md">
          {mode === "create" && (
            <TextInput
              id="work-schedule-new-name"
              label={t("attendance:workSchedules.name")}
              withAsterisk
              value={name}
              maxLength={NAME_MAX_LENGTH}
              disabled={mutation.isPending}
              onChange={(e) => setName(e.currentTarget.value)}
            />
          )}
          <Stack gap={6}>
            {days.map((day) => (
              <Group key={day.dayOfWeek} gap="xs" wrap="wrap" align="center">
                <Checkbox
                  w="7rem"
                  label={t(`attendance:weekDay.${day.dayOfWeek}`)}
                  checked={day.isWorkingDay}
                  disabled={mutation.isPending}
                  onChange={(e) =>
                    updateDay(day.dayOfWeek, e.currentTarget.checked
                      ? { isWorkingDay: true, start: day.start ?? "09:00:00", end: day.end ?? "18:00:00" }
                      : { isWorkingDay: false, start: null, end: null, breakStart: null, breakEnd: null })
                  }
                />
                {day.isWorkingDay ? (
                  <Group gap={4} wrap="nowrap">
                    <TimePicker
                      value={toUiTime(day.start)}
                      disabled={mutation.isPending}
                      onChange={(v) => updateDay(day.dayOfWeek, { start: toApiTime(v) })}
                    />
                    <Text span size="sm" c="dimmed">
                      –
                    </Text>
                    <TimePicker
                      value={toUiTime(day.end)}
                      disabled={mutation.isPending}
                      onChange={(v) => updateDay(day.dayOfWeek, { end: toApiTime(v) })}
                    />
                    <Text span size="xs" c="dimmed" ml="sm">
                      {t("attendance:workSchedules.breakLabel")}
                    </Text>
                    <TimePicker
                      value={toUiTime(day.breakStart)}
                      disabled={mutation.isPending}
                      onChange={(v) => updateDay(day.dayOfWeek, { breakStart: toApiTime(v) })}
                    />
                    <Text span size="sm" c="dimmed">
                      –
                    </Text>
                    <TimePicker
                      value={toUiTime(day.breakEnd)}
                      disabled={mutation.isPending}
                      onChange={(v) => updateDay(day.dayOfWeek, { breakEnd: toApiTime(v) })}
                    />
                  </Group>
                ) : (
                  <Text size="sm" c="dimmed">
                    {t("attendance:workSchedules.dayOff")}
                  </Text>
                )}
              </Group>
            ))}
          </Stack>
          <Checkbox
            label={t("attendance:workSchedules.includeSaturday")}
            description={t("attendance:workSchedules.includeSaturdayDesc")}
            checked={includeSaturday}
            disabled={mutation.isPending}
            onChange={(e) => setIncludeSaturday(e.currentTarget.checked)}
          />
          {error && (
            <Text size="sm" c="red" role="alert">
              {error}
            </Text>
          )}
          <Group justify="flex-end" gap="xs">
            <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" loading={mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}

function todayDateOnly(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

/**
 * "Çalışanlara ata" (attendance.workschedule.assign): searchable employee multi-select + effective-from date.
 * Unlike the leave rule-set assign flow this is all-or-nothing (no per-employee warnings) — a scope/not-found
 * failure for any selected employee rejects the whole request (`AssignWorkScheduleHandler`).
 */
function AssignWorkScheduleDialog({ schedule, onClose }: { schedule: WorkSchedule; onClose: () => void }) {
  const { t } = useTranslation(["attendance", "common", "employees"]);
  const queryClient = useQueryClient();
  const [search, setSearch] = useState("");
  const [debounced, setDebounced] = useState("");
  const [selected, setSelected] = useState<Map<string, string>>(new Map());
  const [effectiveFrom, setEffectiveFrom] = useState(todayDateOnly());
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(search.trim()), 250);
    return () => window.clearTimeout(timer);
  }, [search]);

  const employeesQuery = useQuery({
    queryKey: ["employees", "work-schedule-assign", debounced],
    queryFn: () => listEmployees({ page: 1, pageSize: 20, q: debounced || undefined, status: "Active", sort: "displayName" }),
    retry: false,
  });

  const mutation = useMutation({
    mutationFn: () => assignWorkSchedule(schedule.id, [...selected.keys()], effectiveFrom),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: SCHEDULES_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("attendance:workSchedules.assigned", { count: selected.size }) });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("attendance:genericError"))),
  });

  const toggle = (id: string, name: string, checked: boolean) =>
    setSelected((current) => {
      const next = new Map(current);
      if (checked) next.set(id, name);
      else next.delete(id);
      return next;
    });

  const chips = useMemo(() => [...selected.entries()], [selected]);
  const busy = mutation.isPending;

  function submit(event: FormEvent) {
    event.preventDefault();
    if (selected.size === 0) return setError(t("attendance:workSchedules.assignNoSelection"));
    if (!effectiveFrom) return setError(t("attendance:workSchedules.assignEffectiveFromRequired"));
    setError(null);
    mutation.mutate();
  }

  return (
    <Modal opened onClose={onClose} size="32rem" title={t("attendance:workSchedules.assignTitle")}>
      <form onSubmit={submit}>
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("attendance:workSchedules.assignDescription", { name: schedule.name })}
          </Text>
          <DatePicker id="work-schedule-assign-effective-from" value={effectiveFrom} disabled={busy} onChange={setEffectiveFrom} />
          {chips.length > 0 && (
            <Group gap={6}>
              {chips.map(([id, name]) => (
                <Badge
                  key={id}
                  variant="light"
                  tt="none"
                  pr={3}
                  rightSection={
                    <CloseButton
                      size="xs"
                      radius="xl"
                      variant="transparent"
                      aria-label={t("attendance:workSchedules.assignRemove", { name })}
                      onClick={() => toggle(id, name, false)}
                    />
                  }
                >
                  {name}
                </Badge>
              ))}
            </Group>
          )}
          <TextInput
            leftSection={<Search size={16} />}
            placeholder={t("employees:picker.search")}
            aria-label={t("employees:picker.search")}
            value={search}
            disabled={busy}
            onChange={(e) => setSearch(e.currentTarget.value)}
          />
          <Paper withBorder>
            <ScrollArea.Autosize mah={288}>
              {employeesQuery.isLoading ? (
                <Skeleton h={96} m="xs" w="auto" />
              ) : employeesQuery.isError ? (
                <Text size="sm" c="red" p="sm">
                  {getApiErrorMessage(employeesQuery.error, t("attendance:genericError"))}
                </Text>
              ) : (employeesQuery.data?.items ?? []).length === 0 ? (
                <Text size="sm" c="dimmed" p="sm">
                  {t("employees:picker.empty")}
                </Text>
              ) : (
                <Stack gap={0} py={4}>
                  {(employeesQuery.data?.items ?? []).map((employee) => (
                    <Checkbox
                      key={employee.id}
                      id={`work-schedule-assign-${employee.id}`}
                      px="sm"
                      py={8}
                      checked={selected.has(employee.id)}
                      disabled={busy}
                      onChange={(e) => toggle(employee.id, employee.displayName, e.currentTarget.checked)}
                      styles={{ body: { alignItems: "center" }, labelWrapper: { minWidth: 0, flex: 1 } }}
                      label={
                        <>
                          <Text span size="sm" display="block" truncate="end">
                            {employee.displayName}
                          </Text>
                          {employee.workEmail && (
                            <Text span size="xs" c="dimmed" display="block" truncate="end">
                              {employee.workEmail}
                            </Text>
                          )}
                        </>
                      }
                    />
                  ))}
                </Stack>
              )}
            </ScrollArea.Autosize>
          </Paper>
          {error && (
            <Text size="sm" c="red" role="alert">
              {error}
            </Text>
          )}
          <Group justify="flex-end" gap="xs">
            <Button type="button" variant="default" onClick={onClose} disabled={busy}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" loading={busy} disabled={selected.size === 0}>
              {busy ? t("common:saving") : t("attendance:workSchedules.assignSubmit", { count: selected.size })}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
