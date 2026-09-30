import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import {
  ActionIcon,
  Badge,
  Group,
  Skeleton,
  Stack,
  Table,
  Text,
  Title,
  Tooltip,
} from "@mantine/core";
import { ChevronLeft, ChevronRight, FileSpreadsheet, Lock } from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import OrgUnitSelect from "@/components/employees/org-unit-select";
import MatrixLegend, { MatrixChip } from "@/components/attendance/matrix-legend";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly, parseDateOnly, toDateOnly } from "@/lib/dates";
import {
  getMonthlyMatrix,
  type MonthlyMatrixCell,
} from "@/services/attendance-monthly-matrix.service";
import { monthStartOf, shiftMonth } from "./monthly-timesheet-helpers";

/** Yatay kaydırmada çalışan sütunu sabit kalır. */
const STICKY_CELL_STYLE = {
  position: "sticky",
  left: 0,
  zIndex: 1,
  backgroundColor: "var(--mantine-color-body)",
} as const;
const MATRIX_KEY = ["attendance", "monthly-matrix"] as const;

/** "Aylık puantaj matrisi" (Z3.4): satır = çalışan, sütun = ayın günleri; hücre = renk + kısa kod. */
export default function MonthlyMatrixPage() {
  const canView = usePermission("attendance.daily.view");
  if (!canView) return <NoAccess />;
  return <MonthlyMatrixView />;
}

function MonthlyMatrixView() {
  const { t, i18n } = useTranslation(["attendance-daily", "attendance"]);
  const [monthStart, setMonthStart] = useState(() => monthStartOf(new Date()));
  const [orgUnitId, setOrgUnitId] = useState<string | null>(null);

  const monthIso = toDateOnly(monthStart);
  const monthLabel = formatDateOnly(monthIso, i18n.language, { month: "long", year: "numeric" });
  const daysInMonth = new Date(monthStart.getFullYear(), monthStart.getMonth() + 1, 0).getDate();
  const weekdayFormat = useMemo(
    () => new Intl.DateTimeFormat(i18n.language, { weekday: "short" }),
    [i18n.language]
  );
  const dayNumbers = useMemo(
    () => Array.from({ length: daysInMonth }, (_, i) => i + 1),
    [daysInMonth]
  );

  const matrix = useQuery({
    queryKey: [...MATRIX_KEY, monthIso, orgUnitId],
    queryFn: () => getMonthlyMatrix(monthIso, orgUnitId),
  });

  const rows = useMemo(
    () =>
      [...(matrix.data?.rows ?? [])].sort((a, b) => a.employeeName.localeCompare(b.employeeName)),
    [matrix.data]
  );

  return (
    <div className="max-w-full space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" wrap="wrap">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <FileSpreadsheet size={24} /> {t("attendance-daily:monthlyMatrix.title")}
          </Title>
          <Text c="dimmed">{t("attendance-daily:monthlyMatrix.subtitle")}</Text>
        </div>
        {matrix.data?.isLocked && (
          <Badge
            data-testid="matrix-locked-badge"
            color="yellow"
            variant="light"
            size="lg"
            leftSection={<Lock size={14} />}
          >
            {t("attendance-daily:monthlyMatrix.locked")}
          </Badge>
        )}
      </Group>

      <Group justify="space-between" align="center" wrap="wrap" gap="sm">
        <OrgUnitSelect
          value={orgUnitId}
          onChange={setOrgUnitId}
          emptyLabel={t("attendance:teamTimesheet.allUnits")}
          className="w-64"
        />
        <Group gap="xs" wrap="nowrap">
          <Tooltip label={t("attendance:previousMonth")}>
            <ActionIcon
              variant="default"
              aria-label={t("attendance:previousMonth")}
              onClick={() => setMonthStart((d) => shiftMonth(d, -1))}
            >
              <ChevronLeft className="h-4 w-4" />
            </ActionIcon>
          </Tooltip>
          <Text fw={500} miw={180} ta="center">
            {monthLabel}
          </Text>
          <Tooltip label={t("attendance:nextMonth")}>
            <ActionIcon
              variant="default"
              aria-label={t("attendance:nextMonth")}
              onClick={() => setMonthStart((d) => shiftMonth(d, 1))}
            >
              <ChevronRight className="h-4 w-4" />
            </ActionIcon>
          </Tooltip>
        </Group>
      </Group>

      <MatrixLegend />

      {matrix.isLoading ? (
        <Stack gap="xs">
          <Skeleton h={36} />
          <Skeleton h={36} />
          <Skeleton h={36} />
        </Stack>
      ) : matrix.isError ? (
        <Text size="sm" c="red">
          {getApiErrorMessage(matrix.error, t("attendance:genericError"))}
        </Text>
      ) : rows.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("attendance-daily:monthlyMatrix.empty")}
        </Text>
      ) : (
        <div className="overflow-x-auto">
          <Table
            withTableBorder
            withColumnBorders
            verticalSpacing={4}
            horizontalSpacing={4}
            aria-label={`${t("attendance-daily:monthlyMatrix.title")} - ${monthLabel}`}
          >
            <Table.Thead>
              <Table.Tr>
                <Table.Th scope="col" miw={180} style={STICKY_CELL_STYLE}>
                  {t("attendance:teamTimesheet.employee")}
                </Table.Th>
                {dayNumbers.map((day) => (
                  <Table.Th key={day} scope="col" ta="center">
                    <div>{day}</div>
                    <Text size="xs" c="dimmed" fw={400}>
                      {weekdayFormat.format(
                        new Date(monthStart.getFullYear(), monthStart.getMonth(), day)
                      )}
                    </Text>
                  </Table.Th>
                ))}
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {rows.map((row) => {
                const byDay = new Map<number, MonthlyMatrixCell>(
                  row.cells.map((cell) => [parseDateOnly(cell.date).getDate(), cell])
                );
                return (
                  <Table.Tr key={row.employeeId}>
                    <Table.Th scope="row" fw={500} style={STICKY_CELL_STYLE}>
                      {row.employeeName}
                    </Table.Th>
                    {dayNumbers.map((day) => {
                      const cell = byDay.get(day);
                      const code = cell?.code ?? "none";
                      const dateIso =
                        cell?.date ??
                        toDateOnly(new Date(monthStart.getFullYear(), monthStart.getMonth(), day));
                      return (
                        <Table.Td key={day} ta="center">
                          <MatrixChip
                            code={code}
                            testId={`matrix-cell-${row.employeeId}-${dateIso}`}
                            ariaLabelPrefix={`${row.employeeName}, ${formatDateOnly(dateIso, i18n.language)}`}
                          />
                        </Table.Td>
                      );
                    })}
                  </Table.Tr>
                );
              })}
            </Table.Tbody>
          </Table>
        </div>
      )}
    </div>
  );
}
