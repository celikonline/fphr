import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { CalendarDays, Plus, Repeat } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import { DatePicker } from "@/components/ui/date-picker";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { structureErrorMessage } from "@/components/structure/errors";
import { toast } from "@/hooks/use-toast";
import { formatDateOnly } from "@/lib/dates";
import { cn } from "@/lib/utils";
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
      meta: { label: t("structure:calendars.entryName"), cellClassName: "font-medium" },
      sortingFn: (a, b) => a.original.name.localeCompare(b.original.name, locale),
    },
    {
      id: "recurring",
      accessorKey: "isRecurringAnnually",
      header: t("structure:calendars.recurring"),
      meta: { label: t("structure:calendars.recurring"), headerClassName: "w-40" },
      sortingFn: (a, b) => Number(a.original.isRecurringAnnually) - Number(b.original.isRecurringAnnually),
      cell: ({ row }) =>
        row.original.isRecurringAnnually ? (
          <Badge variant="outline" className="gap-1">
            <Repeat className="h-3 w-3" /> {t("structure:calendars.everyYear")}
          </Badge>
        ) : (
          <span className="text-sm text-muted-foreground">{t("structure:calendars.oneTime")}</span>
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

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-bold">
            <CalendarDays className="h-6 w-6" /> {t("structure:holidayCalendarsTitle")}
          </h1>
          <p className="text-muted-foreground">{t("structure:calendars.subtitle")}</p>
        </div>
        {canCreate && (
          <Button type="button" onClick={() => setCalendarDialogOpen(true)}>
            <Plus className="mr-2 h-4 w-4" /> {t("structure:calendars.addCalendar")}
          </Button>
        )}
      </div>

      {calendarsQuery.isLoading ? (
        <div className="grid gap-6 lg:grid-cols-[18rem_1fr]">
          <Skeleton className="h-64 w-full" />
          <Skeleton className="h-64 w-full" />
        </div>
      ) : calendarsQuery.isError ? (
        <Card>
          <CardContent className="p-6 text-center text-sm text-destructive">{t("structure:errors.loadFailed")}</CardContent>
        </Card>
      ) : calendars.length === 0 ? (
        <Card>
          <CardContent className="p-6 text-center text-sm text-muted-foreground">{t("structure:calendars.empty")}</CardContent>
        </Card>
      ) : (
        <div className="grid items-start gap-6 lg:grid-cols-[18rem_1fr]">
          <Card>
            <CardHeader className="pb-3">
              <CardTitle className="text-base">{t("structure:calendars.listTitle")}</CardTitle>
            </CardHeader>
            <CardContent className="space-y-1 px-3 pb-4">
              {calendars.map((calendar) => (
                <button
                  key={calendar.id}
                  type="button"
                  onClick={() => setSelectedId(calendar.id)}
                  className={cn(
                    "flex w-full items-center gap-2 rounded-md border px-3 py-2 text-left text-sm transition-colors",
                    calendar.id === selected?.id ? "border-primary bg-primary/5 font-semibold" : "border-transparent hover:bg-muted",
                  )}
                >
                  <span className="min-w-0 flex-1 truncate">{calendar.name}</span>
                  <span className="text-xs text-muted-foreground">{calendar.countryCode}</span>
                  {calendar.isSystemDefined && <Badge variant="secondary">{t("structure:calendars.system")}</Badge>}
                </button>
              ))}
            </CardContent>
          </Card>

          {selected && (
            <Card>
              <CardHeader className="flex flex-row flex-wrap items-center justify-between gap-3 space-y-0 pb-4">
                <div>
                  <CardTitle className="flex items-center gap-2 text-lg">
                    {selected.name}
                    {selected.isSystemDefined && <Badge variant="secondary">{t("structure:calendars.system")}</Badge>}
                  </CardTitle>
                  <p className="text-xs text-muted-foreground">
                    {t("structure:calendars.country", { code: selected.countryCode })}
                  </p>
                </div>
                <div className="flex items-center gap-2">
                  <Select value={year} onValueChange={setYear}>
                    <SelectTrigger className="h-9 w-32">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={ALL_YEARS}>{t("structure:calendars.allYears")}</SelectItem>
                      {years.map((y) => (
                        <SelectItem key={y} value={y}>
                          {y}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  {canUpdate && (
                    <Button type="button" onClick={() => setEntryDialogOpen(true)}>
                      <Plus className="mr-2 h-4 w-4" /> {t("structure:calendars.addEntry")}
                    </Button>
                  )}
                </div>
              </CardHeader>
              <CardContent className="p-0">
                {entriesQuery.isError ? (
                  <p className="p-6 text-center text-sm text-destructive">{t("structure:errors.loadFailed")}</p>
                ) : (
                  <DataTable
                    table={entriesTable}
                    isLoading={entriesQuery.isLoading}
                    emptyMessage={t("structure:calendars.noEntries")}
                  />
                )}
              </CardContent>
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
    <Dialog open onOpenChange={(open) => !open && !mutation.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("structure:calendars.createTitle")}</DialogTitle>
          <DialogDescription>{t("structure:calendars.createDescription")}</DialogDescription>
        </DialogHeader>
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            setSubmitted(true);
            if (!nameMissing && !countryInvalid) mutation.mutate();
          }}
        >
          <div>
            <Label htmlFor="calendar-name">{t("structure:calendars.name")}</Label>
            <Input id="calendar-name" autoFocus maxLength={200} value={name} onChange={(e) => setName(e.target.value)} />
            {submitted && nameMissing && <p className="mt-1 text-xs text-destructive">{t("common:required")}</p>}
          </div>
          <div>
            <Label htmlFor="calendar-country">{t("structure:calendars.countryCode")}</Label>
            <Input
              id="calendar-country"
              maxLength={2}
              placeholder="TR"
              value={countryCode}
              onChange={(e) => setCountryCode(e.target.value)}
            />
            {submitted && countryInvalid && (
              <p className="mt-1 text-xs text-destructive">{t("structure:calendars.countryCodeInvalid")}</p>
            )}
          </div>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
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
    <Dialog open onOpenChange={(open) => !open && !mutation.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("structure:calendars.addEntryTitle")}</DialogTitle>
          <DialogDescription>{calendar.name}</DialogDescription>
        </DialogHeader>
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            setSubmitted(true);
            if (!nameMissing && !dateMissing) mutation.mutate();
          }}
        >
          <div>
            <Label htmlFor="entry-date">{t("structure:calendars.date")}</Label>
            <DatePicker id="entry-date" value={date} onChange={setDate} />
            {submitted && dateMissing && <p className="mt-1 text-xs text-destructive">{t("common:required")}</p>}
          </div>
          <div>
            <Label htmlFor="entry-name">{t("structure:calendars.entryName")}</Label>
            <Input
              id="entry-name"
              maxLength={200}
              placeholder={t("structure:calendars.entryNamePlaceholder")}
              value={name}
              onChange={(e) => setName(e.target.value)}
            />
            {submitted && nameMissing && <p className="mt-1 text-xs text-destructive">{t("common:required")}</p>}
          </div>
          <div className="flex items-center gap-2">
            <Checkbox id="entry-recurring" checked={recurring} onCheckedChange={(checked) => setRecurring(checked === true)} />
            <Label htmlFor="entry-recurring" className="font-normal">
              {t("structure:calendars.recurringLabel")}
            </Label>
          </div>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
