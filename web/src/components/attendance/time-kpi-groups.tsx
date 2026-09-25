import { useTranslation } from "react-i18next";
import { BarChart } from "@mantine/charts";
import { Card, Group, SegmentedControl, Stack, Text, Title } from "@mantine/core";
import type {
  LateEarlyByUnitRow,
  OvertimeByGroupRow,
  OvertimeGroupBy,
  ShiftAttendanceRate,
} from "@/services/analytics-time.service";

export const GROUP_BY_OPTIONS: OvertimeGroupBy[] = ["branch", "unit", "team", "title"];

const MINUTES_PER_HOUR = 60;
const hours = (minutes: number) => Math.round((minutes / MINUTES_PER_HOUR) * 10) / 10;
const CHART_ROW_HEIGHT = 36;
const CHART_MIN_HEIGHT = 160;
const chartHeight = (rows: number) => Math.max(CHART_MIN_HEIGHT, rows * CHART_ROW_HEIGHT + 60);

export function TimeKpiLateEarly({ rows }: { rows: LateEarlyByUnitRow[] }) {
  const { t } = useTranslation(["attendance-daily"]);
  const data = rows.map((r) => ({ unit: r.orgUnitName, late: r.lateCount, earlyLeave: r.earlyLeaveCount }));
  return (
    <Card withBorder padding="md">
      <Stack gap="sm">
        <Title order={3} size="h4">
          {t("attendance-daily:timeDashboard.lateEarly.title")}
        </Title>
        {rows.length === 0 ? (
          <Text size="sm" c="dimmed">
            {t("attendance-daily:timeDashboard.noData")}
          </Text>
        ) : (
          <BarChart
            h={chartHeight(rows.length)}
            data={data}
            dataKey="unit"
            orientation="vertical"
            withLegend
            series={[
              { name: "late", color: "orange.6", label: t("attendance-daily:timeDashboard.status.late") },
              { name: "earlyLeave", color: "violet.6", label: t("attendance-daily:timeDashboard.status.earlyLeave") },
            ]}
          />
        )}
      </Stack>
    </Card>
  );
}

interface OvertimeProps {
  rows: OvertimeByGroupRow[];
  groupBy: OvertimeGroupBy;
  onGroupByChange: (value: OvertimeGroupBy) => void;
}

export function TimeKpiOvertime({ rows, groupBy, onGroupByChange }: OvertimeProps) {
  const { t } = useTranslation(["attendance-daily"]);
  const data = rows.map((r) => ({ group: r.groupName, hours: hours(r.overtimeMinutes) }));
  return (
    <Card withBorder padding="md">
      <Stack gap="sm">
        <Group justify="space-between" align="center">
          <Title order={3} size="h4">
            {t("attendance-daily:timeDashboard.overtime.title")}
          </Title>
          <SegmentedControl
            size="xs"
            value={groupBy}
            onChange={(v) => onGroupByChange(v as OvertimeGroupBy)}
            data={GROUP_BY_OPTIONS.map((g) => ({ value: g, label: t(`attendance-daily:timeDashboard.groupBy.${g}`) }))}
            aria-label={t("attendance-daily:timeDashboard.overtime.groupBy")}
          />
        </Group>
        {rows.length === 0 ? (
          <Text size="sm" c="dimmed">
            {t("attendance-daily:timeDashboard.noData")}
          </Text>
        ) : (
          <BarChart
            h={chartHeight(rows.length)}
            data={data}
            dataKey="group"
            orientation="vertical"
            unit=" h"
            series={[{ name: "hours", color: "indigo.6", label: t("attendance-daily:timeDashboard.overtime.hours") }]}
          />
        )}
      </Stack>
    </Card>
  );
}

export function TimeKpiRate({ data, locale }: { data: ShiftAttendanceRate; locale: string }) {
  const { t } = useTranslation(["attendance-daily"]);
  const pct = new Intl.NumberFormat(locale, { style: "percent", maximumFractionDigits: 1 }).format(data.rate);
  return (
    <Card withBorder padding="md">
      <Stack gap={4}>
        <Text size="sm" c="dimmed">
          {t("attendance-daily:timeDashboard.rate.title")}
        </Text>
        <Text fw={700} size="xl">
          {pct}
        </Text>
        <Text size="xs" c="dimmed">
          {t("attendance-daily:timeDashboard.rate.detail", { worked: data.workedShiftDays, planned: data.plannedShiftDays })}
        </Text>
      </Stack>
    </Card>
  );
}
