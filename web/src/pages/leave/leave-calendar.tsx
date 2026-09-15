import { useMemo, useState, type CSSProperties } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { CalendarDays, ChevronLeft, ChevronRight } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import OrgUnitSelect from "@/components/employees/org-unit-select";
import { useOrgUnits } from "@/components/employees/use-org-units";
import { useDays, useLeaveName } from "@/components/leave/leave-shared";
import { useAccessSetting } from "@/hooks/use-access-setting";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly, parseDateOnly, toDateOnly } from "@/lib/dates";
import { cn } from "@/lib/utils";
import { getLeaveCalendar, type LeaveCalendarEntry } from "@/services/leave.service";

/** Pending entries: striped, lighter than the solid approved bars. */
const PENDING_STYLE: CSSProperties = {
  backgroundImage: "repeating-linear-gradient(135deg, rgb(245 158 11 / 0.55) 0 5px, rgb(245 158 11 / 0.2) 5px 10px)",
};

function startOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

interface CalendarRow {
  employeeId: string;
  employeeName: string;
  /** Entries per lane, so overlapping requests of one employee do not cover each other. */
  lanes: { entry: LeaveCalendarEntry; start: number; end: number }[][];
}

/** İzinler → Takvim: month view of approved and pending leave (leave.my.calendar + Calendar.ShowMenu). */
export default function LeaveCalendarPage() {
  const canView = usePermission("leave.my.calendar");
  const menuEnabled = useAccessSetting("showCalendarMenu");
  if (!canView || !menuEnabled) return <NoAccess />;
  return <LeaveCalendar />;
}

function LeaveCalendar() {
  const { t, i18n } = useTranslation(["leave", "common", "employees"]);
  const leaveName = useLeaveName();
  const days = useDays();
  const { nameById } = useOrgUnits();
  const [month, setMonth] = useState(() => startOfMonth(new Date()));
  const [orgUnitId, setOrgUnitId] = useState<string | null>(null);

  // A calendar month is at most 31 days, well within the API's 93-day limit.
  const dates = useMemo(() => {
    const count = new Date(month.getFullYear(), month.getMonth() + 1, 0).getDate();
    return Array.from({ length: count }, (_, i) => new Date(month.getFullYear(), month.getMonth(), i + 1));
  }, [month]);
  const from = toDateOnly(dates[0]);
  const to = toDateOnly(dates[dates.length - 1]);
  const today = toDateOnly(new Date());

  const query = useQuery({
    queryKey: ["leave", "calendar", from, to, orgUnitId],
    queryFn: () => getLeaveCalendar({ from, to, orgUnitId }),
    retry: false,
  });

  const typeLabel = (entry: LeaveCalendarEntry) =>
    entry.isMasked
      ? t("leave:calendar.masked")
      : entry.leaveTypeName
        ? leaveName(entry.leaveTypeName)
        : entry.leaveTypeCode
          ? t(`leave:code.${entry.leaveTypeCode}`)
          : t("leave:calendar.masked");

  const rows = useMemo<CalendarRow[]>(() => {
    const byEmployee = new Map<string, CalendarRow>();
    const sorted = [...(query.data ?? [])].sort((a, b) => a.startDate.localeCompare(b.startDate) || b.endDate.localeCompare(a.endDate));
    for (const entry of sorted) {
      const start = Math.max(0, dayIndex(entry.startDate, from));
      const end = Math.min(dates.length - 1, dayIndex(entry.endDate, from));
      if (end < start) continue;
      const row = byEmployee.get(entry.employeeId) ?? { employeeId: entry.employeeId, employeeName: entry.employeeName, lanes: [] };
      const lane = row.lanes.find((items) => items.every((item) => item.end < start || item.start > end));
      if (lane) lane.push({ entry, start, end });
      else row.lanes.push([{ entry, start, end }]);
      byEmployee.set(entry.employeeId, row);
    }
    return [...byEmployee.values()].sort((a, b) => a.employeeName.localeCompare(b.employeeName, i18n.language));
  }, [query.data, from, dates.length, i18n.language]);

  const gridTemplate: CSSProperties = { gridTemplateColumns: `minmax(9rem, 13rem) repeat(${dates.length}, minmax(1.75rem, 1fr))` };
  const monthLabel = new Intl.DateTimeFormat(i18n.language, { month: "long", year: "numeric" }).format(month);
  const weekday = new Intl.DateTimeFormat(i18n.language, { weekday: "narrow" });
  const isWeekend = (date: Date) => date.getDay() === 0 || date.getDay() === 6;
  const shift = (delta: number) => setMonth((current) => new Date(current.getFullYear(), current.getMonth() + delta, 1));

  const tooltip = (entry: LeaveCalendarEntry) => (
    <div className="space-y-0.5">
      <p className="font-semibold">{entry.employeeName}</p>
      <p>{typeLabel(entry)}</p>
      <p>
        {formatDateOnly(entry.startDate, i18n.language)} – {formatDateOnly(entry.endDate, i18n.language)} · {days(entry.calculatedDays)}{" "}
        {t("leave:days")}
      </p>
      <p>{t(`leave:status.${entry.status}`)}</p>
    </div>
  );

  return (
    <div className="mx-auto max-w-[96rem] space-y-6 p-6 md:p-10">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-bold">
            <CalendarDays className="h-6 w-6" /> {t("leave:calendar.title")}
          </h1>
          <p className="text-muted-foreground">{t("leave:calendar.subtitle")}</p>
        </div>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Button type="button" variant="outline" size="icon" title={t("leave:calendar.previous")} onClick={() => shift(-1)}>
            <ChevronLeft className="h-4 w-4" />
          </Button>
          <Button type="button" variant="outline" size="sm" onClick={() => setMonth(startOfMonth(new Date()))}>
            {t("leave:calendar.today")}
          </Button>
          <Button type="button" variant="outline" size="icon" title={t("leave:calendar.next")} onClick={() => shift(1)}>
            <ChevronRight className="h-4 w-4" />
          </Button>
          <h2 className="ml-2 text-lg font-semibold capitalize">{monthLabel}</h2>
        </div>
        <div className="flex flex-wrap items-center gap-4">
          <div className="flex items-center gap-3 text-xs text-muted-foreground">
            <span className="flex items-center gap-1.5">
              <span className="inline-block h-3 w-5 rounded-sm bg-emerald-600" /> {t("leave:status.approved")}
            </span>
            <span className="flex items-center gap-1.5">
              <span className="inline-block h-3 w-5 rounded-sm border border-amber-500" style={PENDING_STYLE} /> {t("leave:status.pending")}
            </span>
          </div>
          {nameById.size > 0 && (
            <div className="w-64">
              <Label htmlFor="calendar-unit" className="sr-only">
                {t("employees:fields.orgUnit")}
              </Label>
              <OrgUnitSelect id="calendar-unit" value={orgUnitId} onChange={setOrgUnitId} emptyLabel={t("employees:filters.allUnits")} />
            </div>
          )}
        </div>
      </div>

      <Card>
        <CardContent className="p-0">
          {query.isLoading ? (
            <Skeleton className="m-4 h-64" />
          ) : query.isError ? (
            <p className="p-4 text-sm text-destructive">{getApiErrorMessage(query.error, t("leave:genericError"))}</p>
          ) : rows.length === 0 ? (
            <p className="p-4 text-sm text-muted-foreground">{t("leave:calendar.empty")}</p>
          ) : (
            <>
              {/* md+: employees × days grid */}
              <div className="hidden overflow-x-auto md:block">
                <div className="min-w-max text-xs">
                  <div className="grid border-b" style={gridTemplate}>
                    <div className="sticky left-0 z-20 bg-card px-3 py-2 font-medium text-muted-foreground">{t("leave:calendar.employee")}</div>
                    {dates.map((date) => {
                      const key = toDateOnly(date);
                      return (
                        <div
                          key={key}
                          className={cn(
                            "flex flex-col items-center border-l py-1 leading-tight",
                            isWeekend(date) && "bg-muted/60 text-muted-foreground",
                            key === today && "bg-primary/10 font-semibold text-primary",
                          )}
                        >
                          <span>{weekday.format(date)}</span>
                          <span>{date.getDate()}</span>
                        </div>
                      );
                    })}
                  </div>
                  {rows.map((row) => (
                    <div key={row.employeeId} className="grid border-b last:border-b-0" style={{ ...gridTemplate, gridAutoRows: "1.75rem" }}>
                      <div
                        className="sticky left-0 z-20 flex items-center truncate bg-card px-3 text-sm font-medium"
                        style={{ gridColumn: 1, gridRow: `1 / span ${row.lanes.length}` }}
                        title={row.employeeName}
                      >
                        {row.employeeName}
                      </div>
                      {dates.map((date, index) => (
                        <div
                          key={index}
                          aria-hidden
                          className={cn("border-l", isWeekend(date) && "bg-muted/60", toDateOnly(date) === today && "bg-primary/5")}
                          style={{ gridColumn: index + 2, gridRow: `1 / span ${row.lanes.length}` }}
                        />
                      ))}
                      {row.lanes.flatMap((lane, laneIndex) =>
                        lane.map(({ entry, start, end }) => (
                          <Tooltip key={entry.requestId}>
                            <TooltipTrigger asChild>
                              <div
                                tabIndex={0}
                                className={cn(
                                  "relative z-10 mx-0.5 my-1 flex items-center overflow-hidden rounded px-1.5 text-[11px] font-medium",
                                  entry.status === "approved" ? "bg-emerald-600 text-white" : "border border-amber-500 text-amber-950 dark:text-amber-100",
                                )}
                                style={{
                                  gridColumn: `${start + 2} / ${end + 3}`,
                                  gridRow: laneIndex + 1,
                                  ...(entry.status === "pending" ? PENDING_STYLE : {}),
                                }}
                              >
                                <span className="truncate">{typeLabel(entry)}</span>
                              </div>
                            </TooltipTrigger>
                            <TooltipContent>{tooltip(entry)}</TooltipContent>
                          </Tooltip>
                        )),
                      )}
                    </div>
                  ))}
                </div>
              </div>

              {/* Narrow screens: list by day */}
              <div className="divide-y md:hidden">
                {dates.map((date) => {
                  const key = toDateOnly(date);
                  const entries = (query.data ?? [])
                    .filter((entry) => entry.startDate <= key && entry.endDate >= key)
                    .sort((a, b) => a.employeeName.localeCompare(b.employeeName, i18n.language));
                  if (entries.length === 0) return null;
                  return (
                    <div key={key} className={cn("space-y-2 p-3", isWeekend(date) && "bg-muted/40")}>
                      <p className={cn("text-sm font-semibold", key === today && "text-primary")}>
                        {formatDateOnly(key, i18n.language, { weekday: "long", day: "numeric", month: "long" })}
                      </p>
                      <ul className="space-y-1.5">
                        {entries.map((entry) => (
                          <li key={entry.requestId} className="flex items-center gap-2 text-sm">
                            <span
                              className={cn(
                                "inline-block h-3 w-3 shrink-0 rounded-sm",
                                entry.status === "approved" ? "bg-emerald-600" : "border border-amber-500",
                              )}
                              style={entry.status === "pending" ? PENDING_STYLE : undefined}
                            />
                            <span className="font-medium">{entry.employeeName}</span>
                            <span className="truncate text-muted-foreground">
                              {typeLabel(entry)} · {days(entry.calculatedDays)} {t("leave:days")}
                            </span>
                          </li>
                        ))}
                      </ul>
                    </div>
                  );
                })}
              </div>
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

/** Whole days from `origin` to `value` (both DateOnly strings). */
function dayIndex(value: string, origin: string): number {
  return Math.round((parseDateOnly(value).getTime() - parseDateOnly(origin).getTime()) / 86_400_000);
}
