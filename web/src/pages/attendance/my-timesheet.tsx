import { useCallback, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { ActionIcon, Badge, Card, Group, SimpleGrid, Skeleton, Stack, Text, Title, Tooltip } from "@mantine/core";
import { CalendarClock, ChevronLeft, ChevronRight } from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly, toDateOnly } from "@/lib/dates";
import { IncalculableHint } from "@/components/attendance/incalculable-hint";
import { getMyAttendanceDays, getMyTimesheetSummary, parseAnomalyFlags, type AttendanceDay } from "@/services/attendance.service";

function startOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

function endOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth() + 1, 0);
}

/** TimeOnly "HH:mm:ss(.fff)" → "HH:mm". */
function formatTime(value: string | null | undefined): string {
  return value ? value.slice(0, 5) : "—";
}

/** Minutes → "Xs Ydk" (0 → "—"). */
function formatWorkedMinutes(minutes: number): string {
  if (minutes <= 0) return "—";
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return rest === 0 ? `${hours}s` : `${hours}s ${rest}dk`;
}

/** Puantajım (11.3.6 self-servis): the caller's own recorded attendance days for a month, with anomaly chips. */
export default function MyTimesheetPage() {
  const canView = usePermission("attendance.my.timesheet");
  if (!canView) return <NoAccess />;
  return <MyTimesheet />;
}

function MyTimesheet() {
  const { t, i18n } = useTranslation(["attendance", "common"]);
  const [month, setMonth] = useState(() => startOfMonth(new Date()));

  const from = toDateOnly(startOfMonth(month));
  const to = toDateOnly(endOfMonth(month));

  const summaryQuery = useQuery({
    queryKey: ["attendance", "timesheet", "me", "summary", from, to],
    queryFn: () => getMyTimesheetSummary({ from, to }),
  });
  const daysQuery = useQuery({
    queryKey: ["attendance", "timesheet", "me", "days", from, to],
    queryFn: () => getMyAttendanceDays({ from, to }),
  });

  const monthLabel = useMemo(
    () => new Intl.DateTimeFormat(i18n.language, { month: "long", year: "numeric" }).format(month),
    [month, i18n.language],
  );

  const duration = useCallback(
    (minutes: number) => {
      const hours = Math.floor(minutes / 60);
      const rest = minutes % 60;
      if (hours === 0) return t("attendance:duration.minutesOnly", { m: rest });
      if (rest === 0) return t("attendance:duration.hoursOnly", { h: hours });
      return t("attendance:duration.hoursMinutes", { h: hours, m: rest });
    },
    [t],
  );

  const sortedDays = useMemo(
    () => [...(daysQuery.data ?? [])].sort((a, b) => (a.date < b.date ? -1 : a.date > b.date ? 1 : 0)),
    [daysQuery.data],
  );

  return (
    <div className="w-full space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <CalendarClock className="h-6 w-6" /> {t("attendance:my.timesheetTitle")}
          </Title>
          <Text c="dimmed">{t("attendance:my.timesheetSubtitle")}</Text>
        </div>
        <Group gap="xs" wrap="nowrap">
          <Tooltip label={t("attendance:previousMonth")}>
            <ActionIcon variant="default" aria-label={t("attendance:previousMonth")} onClick={() => setMonth((m) => new Date(m.getFullYear(), m.getMonth() - 1, 1))}>
              <ChevronLeft className="h-4 w-4" />
            </ActionIcon>
          </Tooltip>
          <Text fw={500} tt="capitalize" miw={140} ta="center">
            {monthLabel}
          </Text>
          <Tooltip label={t("attendance:nextMonth")}>
            <ActionIcon variant="default" aria-label={t("attendance:nextMonth")} onClick={() => setMonth((m) => new Date(m.getFullYear(), m.getMonth() + 1, 1))}>
              <ChevronRight className="h-4 w-4" />
            </ActionIcon>
          </Tooltip>
        </Group>
      </Group>

      <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="md">
        <Card withBorder padding="lg">
          <Text size="xs" c="dimmed">
            {t("attendance:summary.workedDays")}
          </Text>
          {summaryQuery.isLoading ? <Skeleton h={28} mt={4} /> : <Text size="xl" fw={700}>{summaryQuery.data?.workedDays ?? 0}</Text>}
        </Card>
        <Card withBorder padding="lg">
          <Text size="xs" c="dimmed">
            {t("attendance:summary.workedHours")}
          </Text>
          {summaryQuery.isLoading ? <Skeleton h={28} mt={4} /> : <Text size="xl" fw={700}>{duration(summaryQuery.data?.workedMinutes ?? 0)}</Text>}
        </Card>
      </SimpleGrid>

      <Card withBorder padding="lg">
        <Title order={2} size="h4" mb="md">
          {t("attendance:my.dailyListTitle")}
        </Title>
        {daysQuery.isLoading ? (
          <Stack gap="xs">
            <Skeleton h={56} />
            <Skeleton h={56} />
            <Skeleton h={56} />
          </Stack>
        ) : daysQuery.isError ? (
          <Text size="sm" c="red">
            {getApiErrorMessage(daysQuery.error, t("attendance:genericError"))}
          </Text>
        ) : sortedDays.length === 0 ? (
          <Text size="sm" c="dimmed">
            {t("attendance:empty")}
          </Text>
        ) : (
          <Stack gap="xs">
            {sortedDays.map((day) => (
              <TimesheetDayRow key={day.id} day={day} formatDate={(value) => formatDateOnly(value, i18n.language, { weekday: "short", day: "2-digit", month: "short" })} />
            ))}
          </Stack>
        )}
      </Card>
    </div>
  );
}

function TimesheetDayRow({ day, formatDate }: { day: AttendanceDay; formatDate: (value: string) => string }) {
  const { t } = useTranslation(["attendance"]);
  const flags = parseAnomalyFlags(day.anomalies);

  return (
    <div className="flex flex-col gap-2 rounded-md border border-[var(--mantine-color-default-border)] p-3 sm:flex-row sm:items-center sm:justify-between">
      <div>
        <Text fw={500} tt="capitalize">
          {formatDate(day.date)}
        </Text>
        <Text size="sm" c="dimmed">
          {t("attendance:day.checkIn")}: {formatTime(day.checkIn)} · {t("attendance:day.checkOut")}: {formatTime(day.checkOut)}
        </Text>
        <Text size="sm" c="dimmed">
          {t("attendance:day.worked")}: {formatWorkedMinutes(day.workedMinutes)}
        </Text>
      </div>
      <IncalculableHint reason={day.incalculableReason} />
      {flags.length > 0 && (
        <Group gap={4} wrap="wrap">
          {flags.map((flag) => (
            <Badge key={flag} color={day.resolutionState === "unresolved" ? "red" : "gray"} variant="light">
              {t(`attendance:anomaly.${flag}`)}
            </Badge>
          ))}
        </Group>
      )}
    </div>
  );
}
