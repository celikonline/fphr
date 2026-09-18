import { useTranslation } from "react-i18next";
import { Card, Group, Stack, Text, Divider } from "@mantine/core";
import { formatMoney } from "../../lib/money";
import type { OvertimeCostKpi } from "../../services/analytics.service";

/**
 * Story: Displays a summary of weekly/monthly overtime cost data from `OvertimeCostKpi` (S-17).
 *
 * **Example usage** (not wired into a page yet):
 * ```tsx
 * import { getOvertimeCostKpi } from "../../services/analytics.service";
 * import { WeeklyOvertimeCostCard } from "./weekly-overtime-cost-card";
 *
 * const [data, setData] = useState<OvertimeCostKpi | null>(null);
 * const [error, setError] = useState<string | null>(null);
 *
 * useEffect(() => {
 *   getOvertimeCostKpi({ year: new Date().getFullYear(), month: new Date().getMonth() + 1 })
 *     .then(setData)
 *     .catch((err: any) => setError(err.message));
 * }, []);
 *
 * if (error) return <div>Error loading overtime cost</div>;
 * if (!data) return <div>Loading...</div>;
 *
 * return <WeeklyOvertimeCostCard data={data} />;
 * ```
 */

export interface WeeklyOvertimeCostCardProps {
  /** Overtime cost KPI data from `getOvertimeCostKpi()`. */
  data: OvertimeCostKpi;
  className?: string;
}

export function WeeklyOvertimeCostCard({ data, className }: WeeklyOvertimeCostCardProps) {
  const { i18n } = useTranslation();
  const year = data.year;
  const month = data.month;

  // Format month name using locale (e.g. "September 2026" in Turkish)
  const monthName = new Intl.DateTimeFormat(i18n.language, { month: "long", year: "numeric" }).format(
    new Date(year, month - 1, 1)
  );

  return (
    <Card withBorder padding="md" className={className}>
      <Stack gap="md">
        {/* Header: period */}
        <div>
          <Text fw={600} size="md">
            {monthName} - Fazla Mesai Maliyeti
          </Text>
        </div>

        {/* Costs per currency */}
        {data.totals.length > 0 ? (
          <Stack gap="sm">
            {data.totals.map((total) => (
              <Group key={total.currency} justify="space-between" align="center">
                <Text size="sm" c="dimmed">
                  Toplam Maliyet ({total.currency})
                </Text>
                <Text fw={600} size="sm">
                  {formatMoney(total.amount, total.currency, i18n.language)}
                </Text>
              </Group>
            ))}
          </Stack>
        ) : (
          <Text size="sm" c="dimmed">
            Veri bulunamadı
          </Text>
        )}

        <Divider />

        {/* Employee count summary */}
        <Group justify="space-between" grow>
          <div>
            <Text size="xs" c="dimmed">
              Hesaplamaya Dahil Çalışanlar
            </Text>
            <Text fw={600} size="lg">
              {data.employeeCount}
            </Text>
          </div>
          {data.excludedEmployeeCount > 0 && (
            <div>
              <Text size="xs" c="dimmed">
                Hariç Tutulanlar
              </Text>
              <Text fw={600} size="lg" c="orange">
                {data.excludedEmployeeCount}
              </Text>
            </div>
          )}
        </Group>
      </Stack>
    </Card>
  );
}
