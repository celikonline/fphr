import { useTranslation } from "react-i18next";
import { BarChart } from "@mantine/charts";
import { Card, Group, SegmentedControl, Stack, Table, Text, Title } from "@mantine/core";
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

/** Text alternative for a chart: colour alone never carries the values. */
function ValueTable({ label, headers, rows }: { label: string; headers: string[]; rows: (string | number)[][] }) {
  return (
    <Table.ScrollContainer minWidth={240}>
      <Table striped withTableBorder aria-label={label}>
        <Table.Thead>
          <Table.Tr>
            {headers.map((h) => (
              <Table.Th key={h}>{h}</Table.Th>
            ))}
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {rows.map((cells, i) => (
            <Table.Tr key={i}>
              {cells.map((c, j) => (
                <Table.Td key={j}>{c}</Table.Td>
              ))}
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Table.ScrollContainer>
  );
}

export function TimeKpiLateEarly({ rows }: { rows: LateEarlyByUnitRow[] }) {
  const { t } = useTranslation(["attendance-daily"]);
  const unitName = (r: LateEarlyByUnitRow) => (r.orgUnitId ? r.orgUnitName : t("attendance-daily:timeDashboard.unassigned"));
  const data = rows.map((r) => ({ unit: unitName(r), late: r.lateCount, earlyLeave: r.earlyLeaveCount }));
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
            aria-hidden="true"
          />
        )}
        {rows.length > 0 && (
          <ValueTable
            label={t("attendance-daily:timeDashboard.lateEarly.title")}
            headers={[
              t("attendance-daily:timeDashboard.unit"),
              t("attendance-daily:timeDashboard.status.late"),
              t("attendance-daily:timeDashboard.status.earlyLeave"),
            ]}
            rows={rows.map((r) => [unitName(r), r.lateCount, r.earlyLeaveCount])}
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
            unit={` ${t("attendance-daily:timeDashboard.overtime.hoursUnit")}`}
            series={[{ name: "hours", color: "indigo.6", label: t("attendance-daily:timeDashboard.overtime.hours") }]}
            aria-hidden="true"
          />
        )}
        {rows.length > 0 && (
          <ValueTable
            label={t("attendance-daily:timeDashboard.overtime.title")}
            headers={[t(`attendance-daily:timeDashboard.groupBy.${groupBy}`), t("attendance-daily:timeDashboard.overtime.hours")]}
            rows={data.map((d) => [d.group, d.hours])}
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
