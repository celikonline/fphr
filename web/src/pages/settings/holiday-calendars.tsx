import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { CalendarDays, Plus, Repeat } from "lucide-react";
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
import { DataTable, useDataTable } from "@/components/ui/data-table";
import { DatePicker } from "@/components/ui/date-picker";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { structureErrorMessage } from "@/components/structure/errors";
import { toast } from "@/hooks/use-toast";
import { formatDateOnly } from "@/lib/dates";
import {
  addHolidayCalendarEntry,
  createHolidayCalendar,
  listHolidayCalendarEntries,
  listHolidayCalendars,
  type HolidayCalendar,
  type HolidayCalendarEntry,
} from "@/services/structure.service";

const CALENDARS_QUERY_KEY = ["organization", "holiday-calendars"] as const;
const ALL_YEARS = "all";

export default function HolidayCalendarsPage() {
  const canRead = usePermission("organization.holiday_calendars.read");
  if (!canRead) return <NoAccess />;
  return <HolidayCalendars />;
}

function HolidayCalendars() {
  const { t, i18n } = useTranslation(["structure", "common"]);
  const canCreate = usePermission("organization.holiday_calendars.create");
  const canUpdate = usePermission("organization.holiday_calendars.update");

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
        <Text size="sm" fw={500}>
          {row.original.name}
        </Text>
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
  ];

  // Client-side sorting over the fully loaded entry list; rows arrive pre-sorted by date.
  const entriesTable = useDataTable({
    columns: entryColumns,
    data: entries,
    getRowId: (entry) => entry.id,
  });

  const [calendarDialogOpen, setCalendarDialogOpen] = useState(false);
  const [entryDialogOpen, setEntryDialogOpen] = useState(false);

  const yearOptions = [
    { value: ALL_YEARS, label: t("structure:calendars.allYears") },
    ...years.map((y) => ({ value: y, label: y })),
  ];

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
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
        <AddEntryDialog calendar={selected} onClose={() => setEntryDialogOpen(false)} />
      )}
    </div>
  );
}

function CreateCalendarDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (c: HolidayCalendar) => void }) {
  const { t } = useTranslation(["structure", "common"]);
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [countryCode, setCountryCode] = useState("TR");
  const [submitted, setSubmitted] = useState(false);

  const mutation = useMutation({
    mutationFn: () => createHolidayCalendar({ name: name.trim(), countryCode: countryCode.trim().toUpperCase() }),
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
  const countryInvalid = !/^[A-Za-z]{2}$/.test(countryCode.trim());

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
          <TextInput
            id="calendar-country"
            label={t("structure:calendars.countryCode")}
            maxLength={2}
            placeholder="TR"
            value={countryCode}
            onChange={(e) => setCountryCode(e.currentTarget.value)}
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

function AddEntryDialog({ calendar, onClose }: { calendar: HolidayCalendar; onClose: () => void }) {
  const { t } = useTranslation(["structure", "common"]);
  const queryClient = useQueryClient();
  const [date, setDate] = useState("");
  const [name, setName] = useState("");
  const [recurring, setRecurring] = useState(false);
  const [submitted, setSubmitted] = useState(false);

  const mutation = useMutation({
    mutationFn: () => addHolidayCalendarEntry(calendar.id, { date, name: name.trim(), isRecurringAnnually: recurring }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [...CALENDARS_QUERY_KEY, calendar.id, "entries"] });
      toast({ variant: "success", title: t("common:success"), description: t("structure:calendars.entrySuccess") });
      onClose();
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: structureErrorMessage(error, t) }),
  });

  const nameMissing = !name.trim();
  const dateMissing = !date;

  return (
    <Modal opened onClose={() => !mutation.isPending && onClose()} title={t("structure:calendars.addEntryTitle")}>
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
