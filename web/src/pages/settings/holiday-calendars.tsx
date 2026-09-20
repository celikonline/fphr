import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { CalendarDays, Pencil, Plus, Repeat, Trash2 } from "lucide-react";
import {
  Badge,
  Button,
  Card,
  Checkbox,
  Group,
  Input,
  Modal,
  NavLink,
  Select,
  Skeleton,
  Stack,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import ConfirmDialog from "@/components/confirm-dialog";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import { DatePicker } from "@/components/ui/date-picker";
import { RowIconButton } from "@/components/ui/row-icon-button";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { structureErrorMessage } from "@/components/structure/errors";
import { toast } from "@/hooks/use-toast";
import { countryOptions } from "@/lib/country-codes";
import { formatDateOnly } from "@/lib/dates";
import {
  addHolidayCalendarEntry,
  createHolidayCalendar,
  deleteHolidayCalendar,
  deleteHolidayCalendarEntry,
  listHolidayCalendarEntries,
  listHolidayCalendars,
  renameHolidayCalendar,
  updateHolidayCalendarEntry,
  type HolidayCalendar,
  type HolidayCalendarEntry,
} from "@/services/structure.service";

const CALENDARS_QUERY_KEY = ["organization", "holiday-calendars"] as const;
const ALL_YEARS = "all";
const DEFAULT_COUNTRY_CODE = "TR";

export default function HolidayCalendarsPage() {
  const canRead = usePermission("organization.holiday_calendars.read");
  if (!canRead) return <NoAccess />;
  return <HolidayCalendars />;
}

function HolidayCalendars() {
  const { t, i18n } = useTranslation(["structure", "common"]);
  const canCreate = usePermission("organization.holiday_calendars.create");
  const canUpdate = usePermission("organization.holiday_calendars.update");
  const canDelete = usePermission("organization.holiday_calendars.delete");
  const queryClient = useQueryClient();

  const calendarsQuery = useQuery({ queryKey: CALENDARS_QUERY_KEY, queryFn: listHolidayCalendars });
  const calendars = useMemo(
    () =>
      [...(calendarsQuery.data ?? [])].sort(
        (a, b) => Number(b.isSystemDefined) - Number(a.isSystemDefined) || a.name.localeCompare(b.name),
      ),
    [calendarsQuery.data],
  );

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selected = calendars.find((c) => c.id === selectedId) ?? calendars[0];

  const entriesQuery = useQuery({
    queryKey: [...CALENDARS_QUERY_KEY, selected?.id, "entries"],
    queryFn: () => listHolidayCalendarEntries(selected!.id),
    enabled: !!selected,
  });

  const [year, setYear] = useState<string>(String(new Date().getFullYear()));
  const years = useMemo(() => {
    const set = new Set((entriesQuery.data ?? []).map((e) => e.date.slice(0, 4)));
    set.add(String(new Date().getFullYear()));
    return [...set].sort();
  }, [entriesQuery.data]);

  const entries = useMemo(
    () =>
      (entriesQuery.data ?? [])
        .filter((e) => year === ALL_YEARS || e.isRecurringAnnually || e.date.startsWith(year))
        .sort((a, b) => (year === ALL_YEARS ? a.date : a.date.slice(5)).localeCompare(year === ALL_YEARS ? b.date : b.date.slice(5))),
    [entriesQuery.data, year],
  );

  // Recurring entries repeat every year, so a single year is ordered by month/day; "all years" by full date.
  const dateSortKey = (entry: HolidayCalendarEntry) => (year === ALL_YEARS ? entry.date : entry.date.slice(5));
  const locale = i18n.language;

  const [calendarDialogOpen, setCalendarDialogOpen] = useState(false);
  const [entryDialogOpen, setEntryDialogOpen] = useState(false);
  const [renamingCalendar, setRenamingCalendar] = useState(false);
  const [deletingCalendar, setDeletingCalendar] = useState(false);
  const [editingEntry, setEditingEntry] = useState<HolidayCalendarEntry | null>(null);
  const [deletingEntry, setDeletingEntry] = useState<HolidayCalendarEntry | null>(null);

  const onError = (error: unknown) =>
    toast({ variant: "destructive", title: t("common:error"), description: structureErrorMessage(error, t) });

  const deleteCalendarMutation = useMutation({
    mutationFn: (calendar: HolidayCalendar) => deleteHolidayCalendar(calendar.id),
    onSuccess: async () => {
      setDeletingCalendar(false);
      setSelectedId(null);
      await queryClient.invalidateQueries({ queryKey: CALENDARS_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("structure:calendars.deleteSuccess") });
    },
    onError: (error) => {
      setDeletingCalendar(false);
      onError(error);
    },
  });

  const deleteEntryMutation = useMutation({
    mutationFn: (entry: HolidayCalendarEntry) => deleteHolidayCalendarEntry(entry.holidayCalendarId, entry.id),
    onSuccess: async (_data, entry) => {
      setDeletingEntry(null);
      await queryClient.invalidateQueries({ queryKey: [...CALENDARS_QUERY_KEY, entry.holidayCalendarId, "entries"] });
      toast({ variant: "success", title: t("common:success"), description: t("structure:calendars.entryDeleted") });
    },
    onError: (error) => {
      setDeletingEntry(null);
      onError(error);
    },
  });

  // The seeded system calendar is read-only for edits and deletes (the server rejects them with 422 as well).
  const canEditEntries = canUpdate && !selected?.isSystemDefined;

  const entryColumns: ColumnDef<HolidayCalendarEntry>[] = [
    {
      id: "date",
      accessorFn: dateSortKey,
      header: t("structure:calendars.date"),
      meta: { label: t("structure:calendars.date"), headerClassName: "w-48" },
      sortingFn: (a, b) => dateSortKey(a.original).localeCompare(dateSortKey(b.original)),
      cell: ({ row }) =>
        row.original.isRecurringAnnually
          ? formatDateOnly(row.original.date, locale, { day: "numeric", month: "long" })
          : formatDateOnly(row.original.date, locale, { dateStyle: "medium" }),
    },
    {
      id: "name",
      accessorKey: "name",
      header: t("structure:calendars.entryName"),
      meta: { label: t("structure:calendars.entryName") },
      sortingFn: (a, b) => a.original.name.localeCompare(b.original.name, locale),
      cell: ({ row }) => (
        <Group gap={6} wrap="nowrap">
          <Text size="sm" fw={500}>
            {row.original.name}
          </Text>
          {row.original.isHalfDay && (
            <Badge size="sm" variant="light" color="orange">
              {t("structure:calendars.halfDay")}
            </Badge>
          )}
        </Group>
      ),
    },
    {
      id: "recurring",
      accessorKey: "isRecurringAnnually",
      header: t("structure:calendars.recurring"),
      meta: { label: t("structure:calendars.recurring"), headerClassName: "w-40" },
      sortingFn: (a, b) => Number(a.original.isRecurringAnnually) - Number(b.original.isRecurringAnnually),
      cell: ({ row }) =>
        row.original.isRecurringAnnually ? (
          <Badge variant="outline" leftSection={<Repeat size={12} />}>
            {t("structure:calendars.everyYear")}
          </Badge>
        ) : (
          <Text size="sm" c="dimmed" component="span">
            {t("structure:calendars.oneTime")}
          </Text>
        ),
    },
    ...(canEditEntries
      ? [
          {
            id: "actions",
            header: () => <span className="sr-only">{t("structure:calendars.actions")}</span>,
            enableSorting: false,
            enableHiding: false,
            meta: { label: t("structure:calendars.actions"), headerClassName: "w-24" },
            cell: ({ row }) => (
              <Group gap={4} wrap="nowrap" justify="flex-end">
                <RowIconButton
                  label={t("structure:calendars.editEntry")}
                  icon={<Pencil size={16} />}
                  onClick={() => setEditingEntry(row.original)}
                />
                <RowIconButton
                  label={t("structure:calendars.deleteEntry")}
                  icon={<Trash2 size={16} />}
                  color="red"
                  onClick={() => setDeletingEntry(row.original)}
                />
              </Group>
            ),
          } satisfies ColumnDef<HolidayCalendarEntry>,
        ]
      : []),
  ];

  // Client-side sorting over the fully loaded entry list; rows arrive pre-sorted by date.
  const entriesTable = useDataTable({
    columns: entryColumns,
    data: entries,
    getRowId: (entry) => entry.id,
  });

  const yearOptions = [
    { value: ALL_YEARS, label: t("structure:calendars.allYears") },
    ...years.map((y) => ({ value: y, label: y })),
  ];

  return (
    <div className="max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <CalendarDays size={24} /> {t("structure:holidayCalendarsTitle")}
          </Title>
          <Text c="dimmed">{t("structure:calendars.subtitle")}</Text>
        </div>
        {canCreate && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={() => setCalendarDialogOpen(true)}>
            {t("structure:calendars.addCalendar")}
          </Button>
        )}
      </Group>

      {calendarsQuery.isLoading ? (
        <div className="grid gap-6 lg:grid-cols-[18rem_1fr]">
          <Skeleton h={256} />
          <Skeleton h={256} />
        </div>
      ) : calendarsQuery.isError ? (
        <Card withBorder padding="lg">
          <Text size="sm" c="red" ta="center">
            {t("structure:errors.loadFailed")}
          </Text>
        </Card>
      ) : calendars.length === 0 ? (
        <Card withBorder padding="lg">
          <Text size="sm" c="dimmed" ta="center">
            {t("structure:calendars.empty")}
          </Text>
        </Card>
      ) : (
        <div className="grid items-start gap-6 lg:grid-cols-[18rem_1fr]">
          <Card withBorder padding="md">
            <Text fw={600} mb="sm">
              {t("structure:calendars.listTitle")}
            </Text>
            <Stack gap={4}>
              {calendars.map((calendar) => (
                <NavLink
                  key={calendar.id}
                  component="button"
                  type="button"
                  active={calendar.id === selected?.id}
                  onClick={() => setSelectedId(calendar.id)}
                  noWrap
                  label={calendar.name}
                  rightSection={
                    <Group gap={6} wrap="nowrap">
                      <Text size="xs" c="dimmed" component="span">
                        {calendar.countryCode}
                      </Text>
                      {calendar.isSystemDefined && <Badge variant="light">{t("structure:calendars.system")}</Badge>}
                    </Group>
                  }
                />
              ))}
            </Stack>
          </Card>

          {selected && (
            <Card withBorder padding="lg">
              <Group justify="space-between" gap="sm" mb="md">
                <div>
                  <Group gap="xs">
                    <Text fw={600} size="lg">
                      {selected.name}
                    </Text>
                    {selected.isSystemDefined && <Badge variant="light">{t("structure:calendars.system")}</Badge>}
                  </Group>
                  <Text size="xs" c="dimmed">
                    {t("structure:calendars.country", { code: selected.countryCode })}
                  </Text>
                </div>
                <Group gap="xs">
                  {canUpdate && !selected.isSystemDefined && (
                    <Button
                      type="button"
                      variant="default"
                      leftSection={<Pencil size={16} />}
                      onClick={() => setRenamingCalendar(true)}
                    >
                      {t("structure:calendars.renameCalendar")}
                    </Button>
                  )}
                  {canDelete && !selected.isSystemDefined && (
                    <Button
                      type="button"
                      variant="default"
                      color="red"
                      leftSection={<Trash2 size={16} />}
                      onClick={() => setDeletingCalendar(true)}
                    >
                      {t("structure:calendars.deleteCalendar")}
                    </Button>
                  )}
                  <Select
                    w={128}
                    data={yearOptions}
                    value={year}
                    onChange={(value) => value && setYear(value)}
                    allowDeselect={false}
                  />
                  {canUpdate && (
                    <Button type="button" leftSection={<Plus size={16} />} onClick={() => setEntryDialogOpen(true)}>
                      {t("structure:calendars.addEntry")}
                    </Button>
                  )}
                </Group>
              </Group>
              <Card.Section withBorder>
                {entriesQuery.isError ? (
                  <Text size="sm" c="red" ta="center" p="lg">
                    {t("structure:errors.loadFailed")}
                  </Text>
                ) : (
                  <DataTable
                    table={entriesTable}
                    isLoading={entriesQuery.isLoading}
                    emptyMessage={t("structure:calendars.noEntries")}
                  />
                )}
              </Card.Section>
            </Card>
          )}
        </div>
      )}

      {calendarDialogOpen && (
        <CreateCalendarDialog
          onClose={() => setCalendarDialogOpen(false)}
          onCreated={(calendar) => setSelectedId(calendar.id)}
        />
      )}
      {entryDialogOpen && selected && (
        <EntryDialog calendar={selected} onClose={() => setEntryDialogOpen(false)} />
      )}
      {editingEntry && selected && (
        <EntryDialog calendar={selected} entry={editingEntry} onClose={() => setEditingEntry(null)} />
      )}
      {renamingCalendar && selected && (
        <RenameCalendarDialog calendar={selected} onClose={() => setRenamingCalendar(false)} />
      )}

      <ConfirmDialog
        open={deletingCalendar}
        onOpenChange={(open) => !open && setDeletingCalendar(false)}
        title={t("structure:calendars.deleteCalendarTitle")}
        description={t("structure:calendars.deleteCalendarDescription", { name: selected?.name ?? "" })}
        isLoading={deleteCalendarMutation.isPending}
        onConfirm={() => selected && deleteCalendarMutation.mutate(selected)}
      />
      <ConfirmDialog
        open={Boolean(deletingEntry)}
        onOpenChange={(open) => !open && setDeletingEntry(null)}
        title={t("structure:calendars.deleteEntryTitle")}
        description={t("structure:calendars.deleteEntryDescription", { name: deletingEntry?.name ?? "" })}
        isLoading={deleteEntryMutation.isPending}
        onConfirm={() => deletingEntry && deleteEntryMutation.mutate(deletingEntry)}
      />
    </div>
  );
}

function CreateCalendarDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (c: HolidayCalendar) => void }) {
  const { t, i18n } = useTranslation(["structure", "common"]);
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [countryCode, setCountryCode] = useState<string | null>(DEFAULT_COUNTRY_CODE);
  const [submitted, setSubmitted] = useState(false);
  const countries = useMemo(() => countryOptions(i18n.language), [i18n.language]);

  const mutation = useMutation({
    mutationFn: () => createHolidayCalendar({ name: name.trim(), countryCode: countryCode! }),
    onSuccess: (calendar) => {
      queryClient.invalidateQueries({ queryKey: CALENDARS_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("structure:calendars.createSuccess") });
      onCreated(calendar);
      onClose();
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: structureErrorMessage(error, t) }),
  });

  const nameMissing = !name.trim();
  const countryInvalid = !countryCode;

  return (
    <Modal opened onClose={() => !mutation.isPending && onClose()} title={t("structure:calendars.createTitle")}>
      <Text size="sm" c="dimmed" mb="md">
        {t("structure:calendars.createDescription")}
      </Text>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          setSubmitted(true);
          if (!nameMissing && !countryInvalid) mutation.mutate();
        }}
      >
        <Stack gap="md">
          <TextInput
            id="calendar-name"
            data-autofocus
            label={t("structure:calendars.name")}
            maxLength={200}
            value={name}
            onChange={(e) => setName(e.currentTarget.value)}
            error={submitted && nameMissing ? t("common:required") : undefined}
          />
          <Select
            id="calendar-country"
            label={t("structure:calendars.countryCode")}
            data={countries}
            value={countryCode}
            onChange={setCountryCode}
            searchable
            allowDeselect={false}
            nothingFoundMessage={t("structure:calendars.countryNotFound")}
            error={submitted && countryInvalid ? t("structure:calendars.countryCodeInvalid") : undefined}
          />
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

/** Adds a holiday to the calendar, or edits `entry` when given. */
function EntryDialog({ calendar, entry, onClose }: { calendar: HolidayCalendar; entry?: HolidayCalendarEntry; onClose: () => void }) {
  const { t } = useTranslation(["structure", "common"]);
  const queryClient = useQueryClient();
  const [date, setDate] = useState(entry?.date ?? "");
  const [name, setName] = useState(entry?.name ?? "");
  const [recurring, setRecurring] = useState(entry?.isRecurringAnnually ?? false);
  const [halfDay, setHalfDay] = useState(entry?.isHalfDay ?? false);
  const [submitted, setSubmitted] = useState(false);

  const mutation = useMutation({
    mutationFn: () => {
      const input = { date, name: name.trim(), isRecurringAnnually: recurring, isHalfDay: halfDay };
      return entry ? updateHolidayCalendarEntry(calendar.id, entry.id, input) : addHolidayCalendarEntry(calendar.id, input);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [...CALENDARS_QUERY_KEY, calendar.id, "entries"] });
      toast({
        variant: "success",
        title: t("common:success"),
        description: t(entry ? "structure:calendars.entryUpdated" : "structure:calendars.entrySuccess"),
      });
      onClose();
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: structureErrorMessage(error, t) }),
  });

  const nameMissing = !name.trim();
  const dateMissing = !date;

  return (
    <Modal
      opened
      onClose={() => !mutation.isPending && onClose()}
      title={t(entry ? "structure:calendars.editEntryTitle" : "structure:calendars.addEntryTitle")}
    >
      <Text size="sm" c="dimmed" mb="md">
        {calendar.name}
      </Text>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          setSubmitted(true);
          if (!nameMissing && !dateMissing) mutation.mutate();
        }}
      >
        <Stack gap="md">
          <Input.Wrapper
            id="entry-date"
            label={t("structure:calendars.date")}
            error={submitted && dateMissing ? t("common:required") : undefined}
          >
            <DatePicker id="entry-date" value={date} onChange={setDate} holidayCalendarId={calendar.id} />
          </Input.Wrapper>
          <TextInput
            id="entry-name"
            label={t("structure:calendars.entryName")}
            maxLength={200}
            placeholder={t("structure:calendars.entryNamePlaceholder")}
            value={name}
            onChange={(e) => setName(e.currentTarget.value)}
            error={submitted && nameMissing ? t("common:required") : undefined}
          />
          <Checkbox
            id="entry-recurring"
            label={t("structure:calendars.recurringLabel")}
            checked={recurring}
            onChange={(e) => setRecurring(e.currentTarget.checked)}
          />
          <Checkbox
            id="entry-half-day"
            label={t("structure:calendars.halfDayLabel")}
            description={t("structure:calendars.halfDayHint")}
            checked={halfDay}
            onChange={(e) => setHalfDay(e.currentTarget.checked)}
          />
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

function RenameCalendarDialog({ calendar, onClose }: { calendar: HolidayCalendar; onClose: () => void }) {
  const { t } = useTranslation(["structure", "common"]);
  const queryClient = useQueryClient();
  const [name, setName] = useState(calendar.name);
  const [submitted, setSubmitted] = useState(false);

  const mutation = useMutation({
    mutationFn: () => renameHolidayCalendar(calendar.id, name.trim()),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CALENDARS_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("structure:calendars.renameSuccess") });
      onClose();
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: structureErrorMessage(error, t) }),
  });

  const nameMissing = !name.trim();

  return (
    <Modal opened onClose={() => !mutation.isPending && onClose()} title={t("structure:calendars.renameTitle")}>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          setSubmitted(true);
          if (!nameMissing) mutation.mutate();
        }}
      >
        <Stack gap="md">
          <TextInput
            id="calendar-rename"
            data-autofocus
            label={t("structure:calendars.name")}
            maxLength={200}
            value={name}
            onChange={(e) => setName(e.currentTarget.value)}
            error={submitted && nameMissing ? t("common:required") : undefined}
          />
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
