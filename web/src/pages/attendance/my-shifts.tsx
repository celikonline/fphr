import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { ActionIcon, Badge, Card, Group, Skeleton, Stack, Text, Title, Tooltip } from "@mantine/core";
import { CalendarRange, ChevronLeft, ChevronRight, Moon } from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly, toDateOnly } from "@/lib/dates";
import { getMyShiftAssignments, type ShiftAssignment } from "@/services/attendance.service";

function startOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

function endOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth() + 1, 0);
}

/** TimeOnly "HH:mm:ss(.fff)" → "HH:mm". */
function formatTime(value: string): string {
  return value.slice(0, 5);
}

/** Vardiyam (11.3.6 self-servis, package A5): the caller's own shift assignments for a month. */
export default function MyShiftsPage() {
  const canView = usePermission("attendance.shift.self");
  if (!canView) return <NoAccess />;
  return <MyShifts />;
}

function MyShifts() {
  const { t, i18n } = useTranslation(["attendance", "common"]);
  const [month, setMonth] = useState(() => startOfMonth(new Date()));

  const from = toDateOnly(startOfMonth(month));
  const to = toDateOnly(endOfMonth(month));

  const query = useQuery({
    queryKey: ["attendance", "shifts", "me", from, to],
    queryFn: () => getMyShiftAssignments({ from, to }),
  });

  const monthLabel = useMemo(
    () => new Intl.DateTimeFormat(i18n.language, { month: "long", year: "numeric" }).format(month),
    [month, i18n.language],
  );

  const sortedShifts = useMemo(
    () => [...(query.data ?? [])].sort((a, b) => (a.date < b.date ? -1 : a.date > b.date ? 1 : 0)),
    [query.data],
  );

  return (
    <div className="mx-auto max-w-4xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <CalendarRange className="h-6 w-6" /> {t("attendance:my.shiftTitle")}
          </Title>
          <Text c="dimmed">{t("attendance:my.shiftSubtitle")}</Text>
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

      <Card withBorder padding="lg">
        {query.isLoading ? (
          <Stack gap="xs">
            <Skeleton h={56} />
            <Skeleton h={56} />
            <Skeleton h={56} />
          </Stack>
        ) : query.isError ? (
          <Text size="sm" c="red">
            {getApiErrorMessage(query.error, t("attendance:genericError"))}
          </Text>
        ) : sortedShifts.length === 0 ? (
          <Text size="sm" c="dimmed">
            {t("attendance:shift.empty")}
          </Text>
        ) : (
          <Stack gap="xs">
            {sortedShifts.map((shift) => (
              <ShiftRow
                key={shift.id}
                shift={shift}
                formatDate={(value) => formatDateOnly(value, i18n.language, { weekday: "short", day: "2-digit", month: "short" })}
              />
            ))}
          </Stack>
        )}
      </Card>
    </div>
  );
}

function ShiftRow({ shift, formatDate }: { shift: ShiftAssignment; formatDate: (value: string) => string }) {
  const { t } = useTranslation(["attendance"]);

  return (
    <div className="flex flex-col gap-2 rounded-md border border-[var(--mantine-color-default-border)] p-3 sm:flex-row sm:items-center sm:justify-between">
      <div>
        <Group gap={6} wrap="wrap">
          <Text fw={500} tt="capitalize">
            {formatDate(shift.date)}
          </Text>
          {shift.isNight && (
            <Badge variant="light" color="indigo" leftSection={<Moon className="h-3 w-3" />}>
              {t("attendance:shift.night")}
            </Badge>
          )}
          <Badge variant={shift.status === "published" ? "filled" : "outline"} color={shift.status === "published" ? "green" : "gray"}>
            {t(`attendance:shift.status.${shift.status}`)}
          </Badge>
        </Group>
        <Text size="sm" c="dimmed">
          {formatTime(shift.start)} – {formatTime(shift.end)} · {t("attendance:shift.break")}: {shift.breakMinutes} dk
        </Text>
        {shift.note && (
          <Text size="sm" c="dimmed">
            {t("attendance:shift.note")}: {shift.note}
          </Text>
        )}
      </div>
    </div>
  );
}
