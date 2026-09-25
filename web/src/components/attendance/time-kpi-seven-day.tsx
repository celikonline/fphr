import { useMemo } from "react";
import { useTranslation } from "react-i18next";
import { BarChart } from "@mantine/charts";
import { Card, Stack, Table, Text, Title } from "@mantine/core";
import { formatDateOnly, toDateOnly } from "@/lib/dates";
import type { DailyAttendanceTotals } from "@/services/attendance-daily.service";

export const SEVEN_DAY_SPAN = 7;

type SeriesKey = "arrived" | "late" | "earlyLeave" | "absent" | "onLeave";

/**
 * Categorical identity per status (dataviz: fixed hue per entity, distinguishable in light/dark and for color-vision
 * deficiency - teal/orange/violet/red/blue; the details table below the chart carries every value as text too).
 */
const SERIES: { name: SeriesKey; color: string }[] = [
  { name: "arrived", color: "teal.6" },
  { name: "late", color: "orange.6" },
  { name: "earlyLeave", color: "violet.6" },
  { name: "absent", color: "red.7" },
  { name: "onLeave", color: "blue.6" },
];

/** The API omits days with no data; the chart still shows the last 7 calendar days with zeros. */
export function fillSevenDays(rows: DailyAttendanceTotals[], today: Date = new Date()): DailyAttendanceTotals[] {
  const byDate = new Map(rows.map((r) => [r.date, r]));
  return Array.from({ length: SEVEN_DAY_SPAN }, (_, i) => {
    const d = new Date(today.getFullYear(), today.getMonth(), today.getDate() - (SEVEN_DAY_SPAN - 1 - i));
    const date = toDateOnly(d);
    return byDate.get(date) ?? { date, planned: 0, arrived: 0, late: 0, earlyLeave: 0, absent: 0, onLeave: 0 };
  });
}

export function TimeKpiSevenDay({ rows }: { rows: DailyAttendanceTotals[] }) {
  const { t, i18n } = useTranslation(["attendance-daily"]);
  const days = useMemo(() => fillSevenDays(rows), [rows]);
  const label = (date: string) => formatDateOnly(date, i18n.language, { day: "2-digit", month: "short" });
  const label7d = t("attendance-daily:timeDashboard.sevenDay.title");

  const data = days.map((d) => ({ day: label(d.date), ...d }));
  const series = SERIES.map((s) => ({ ...s, label: t(`attendance-daily:timeDashboard.status.${s.name}`) }));

  return (
    <Card withBorder padding="md">
      <Stack gap="sm">
        <Title order={3} size="h4">
          {label7d}
        </Title>
        <BarChart h={260} data={data} dataKey="day" series={series} withLegend tickLine="y" aria-hidden="true" />
        <Table.ScrollContainer minWidth={420}>
          <Table striped withTableBorder aria-label={label7d}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("attendance-daily:timeDashboard.date")}</Table.Th>
                <Table.Th>{t("attendance-daily:timeDashboard.planned")}</Table.Th>
                {series.map((s) => (
                  <Table.Th key={s.name}>{s.label}</Table.Th>
                ))}
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {days.map((d) => (
                <Table.Tr key={d.date}>
                  <Table.Td>{label(d.date)}</Table.Td>
                  <Table.Td>{d.planned}</Table.Td>
                  {SERIES.map((s) => (
                    <Table.Td key={s.name}>{d[s.name]}</Table.Td>
                  ))}
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
        <Text size="xs" c="dimmed">
          {t("attendance-daily:timeDashboard.sevenDay.hint")}
        </Text>
      </Stack>
    </Card>
  );
}
