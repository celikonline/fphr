import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Badge, Card, Group, NumberInput, Skeleton, Switch, Table, Text, Title } from "@mantine/core";
import { formatMoney } from "@/lib/money";
import { getPeriodComparison, type ComparisonLine } from "@/services/payroll.service";

const DEFAULT_THRESHOLD = 5;

/**
 * "Önceki ayla karşılaştırma": the period against the month before it - totals and each employee's gross/net change.
 * Rows past the deviation threshold (default 5%) are flagged so they can be reviewed before approving.
 */
export function PeriodComparison({ periodId }: { periodId: string }) {
  const { t, i18n } = useTranslation(["payroll"]);
  const [threshold, setThreshold] = useState<number | string>(DEFAULT_THRESHOLD);
  const [onlyExceeding, setOnlyExceeding] = useState(true);
  const thresholdValue = typeof threshold === "number" ? threshold : DEFAULT_THRESHOLD;

  const { data, isLoading } = useQuery({
    queryKey: ["payroll", "periods", periodId, "comparison", thresholdValue],
    queryFn: () => getPeriodComparison(periodId, thresholdValue),
    placeholderData: (previous) => previous,
  });

  if (isLoading || !data) {
    return <Skeleton h={120} />;
  }

  const money = (amount: number | null | undefined) => (amount == null ? "—" : formatMoney(amount, "TRY", i18n.language));
  const percent = (value: number | null | undefined) => (value == null ? "—" : `${value > 0 ? "+" : ""}${value.toFixed(2)}%`);
  const lines = onlyExceeding ? data.lines.filter((line) => line.exceedsThreshold) : data.lines;
  const previousLabel = new Intl.DateTimeFormat(i18n.language, { month: "long", year: "numeric" }).format(
    new Date(data.previousYear, data.previousMonth - 1, 1),
  );

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" align="flex-start" wrap="wrap" mb="sm">
        <div>
          <Title order={4}>{t("payroll:comparison.title")}</Title>
          <Text size="xs" c="dimmed">
            {data.previousPeriodId ? t("payroll:comparison.against", { period: previousLabel }) : t("payroll:comparison.noPrevious", { period: previousLabel })}
          </Text>
        </div>
        <Group gap="md" align="flex-end">
          <NumberInput
            label={t("payroll:comparison.threshold")}
            value={threshold}
            onChange={setThreshold}
            min={0}
            max={1000}
            step={1}
            decimalScale={2}
            suffix="%"
            w={110}
          />
          <Switch label={t("payroll:comparison.onlyExceeding")} checked={onlyExceeding} onChange={(event) => setOnlyExceeding(event.currentTarget.checked)} />
        </Group>
      </Group>

      <Table.ScrollContainer minWidth={520}>
        <Table verticalSpacing="xs" horizontalSpacing="sm" mb="md">
          <Table.Thead>
            <Table.Tr>
              <Table.Th />
              <Table.Th ta="right">{t("payroll:comparison.headcount")}</Table.Th>
              <Table.Th ta="right">{t("payroll:comparison.gross")}</Table.Th>
              <Table.Th ta="right">{t("payroll:comparison.net")}</Table.Th>
              <Table.Th ta="right">{t("payroll:comparison.employerCost")}</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {(["current", "previous"] as const).map((side) => (
              <Table.Tr key={side}>
                <Table.Td fw={600}>{t(`payroll:comparison.${side}`)}</Table.Td>
                <Table.Td ta="right">{data[side].headcount}</Table.Td>
                <Table.Td ta="right">{money(data[side].gross)}</Table.Td>
                <Table.Td ta="right">{money(data[side].net)}</Table.Td>
                <Table.Td ta="right">{money(data[side].employerCost)}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>

      <Text size="sm" fw={600} mb="xs">
        {t("payroll:comparison.exceeding", { count: data.exceedingCount })}
      </Text>
      {lines.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("payroll:comparison.empty")}
        </Text>
      ) : (
        <Table.ScrollContainer minWidth={640}>
          <Table verticalSpacing="xs" horizontalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("payroll:comparison.employee")}</Table.Th>
                <Table.Th ta="right">{t("payroll:comparison.previousGross")}</Table.Th>
                <Table.Th ta="right">{t("payroll:comparison.currentGross")}</Table.Th>
                <Table.Th ta="right">{t("payroll:comparison.grossChange")}</Table.Th>
                <Table.Th ta="right">{t("payroll:comparison.netChange")}</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {lines.map((line) => (
                <ComparisonRow key={line.employeeId} line={line} money={money} percent={percent} />
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
    </Card>
  );
}

function ComparisonRow({
  line,
  money,
  percent,
}: {
  line: ComparisonLine;
  money: (amount: number | null | undefined) => string;
  percent: (value: number | null | undefined) => string;
}) {
  const { t } = useTranslation(["payroll"]);
  return (
    <Table.Tr>
      <Table.Td>{line.employeeName}</Table.Td>
      <Table.Td ta="right">{money(line.previousGross)}</Table.Td>
      <Table.Td ta="right">{money(line.currentGross)}</Table.Td>
      <Table.Td ta="right">{percent(line.grossChangePercent)}</Table.Td>
      <Table.Td ta="right">{percent(line.netChangePercent)}</Table.Td>
      <Table.Td>
        {line.kind === "new" && (
          <Badge color="blue" variant="light">
            {t("payroll:comparison.kind.new")}
          </Badge>
        )}
        {line.kind === "left" && (
          <Badge color="gray" variant="light">
            {t("payroll:comparison.kind.left")}
          </Badge>
        )}
        {line.kind === "both" && line.exceedsThreshold && (
          <Badge color="orange" variant="light">
            {t("payroll:comparison.kind.deviation")}
          </Badge>
        )}
      </Table.Td>
    </Table.Tr>
  );
}
