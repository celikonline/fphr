import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Receipt } from "lucide-react";
import { Badge, Card, Group, Skeleton, Stack, Table, Text, Title } from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { usePermission } from "@/components/permission-guard";
import { formatDateOnly } from "@/lib/dates";
import { enumLabel } from "@/lib/i18n-enum";
import {
  getEmployeePayments,
  listMyPayments,
  type PaymentOverviewRow,
} from "@/services/expense.service";
import type { EmployeeDetail } from "@/services/employees.service";

const CLAIMS_VIEW_PERMISSION = "expense.claims.view";
const ADVANCES_VIEW_PERMISSION = "expense.advances.view";

const STATUS_COLOR: Record<string, string> = {
  draft: "gray",
  submitted: "blue",
  approved: "teal",
  rejected: "red",
  paid: "green",
};

type Translate = (key: string, options?: Record<string, unknown>) => string;

/**
 * "Ödemeler" tab's Harcama/Avans block: the employee's expense-claim + advance-request history, the same
 * `PaymentOverviewRow` projection as "Kendi hesabım > Ödemeler" (11.3.8 design decision - reads only the Expense
 * schema). HR/Finance need both `expense.claims.view` and `expense.advances.view`; a Manager (claims.view only)
 * does not see this block on another employee's card yet.
 */
export function ExpensePaymentsSection({
  employee,
  isOwnCard,
}: {
  employee: EmployeeDetail;
  isOwnCard: boolean;
}) {
  const { t, i18n } = useTranslation(["employees", "expense"]);
  const isMobile = useMediaQuery("(max-width: 390px)");
  const canViewClaims = usePermission(CLAIMS_VIEW_PERMISSION);
  const canViewAdvances = usePermission(ADVANCES_VIEW_PERMISSION);
  const canView = isOwnCard || (canViewClaims && canViewAdvances);

  const { data: rows = [], isLoading } = useQuery({
    queryKey: ["employees", "expense-payments", employee.id, isOwnCard],
    queryFn: () => (isOwnCard ? listMyPayments() : getEmployeePayments(employee.id)),
    enabled: canView,
  });

  if (!canView) {
    return null;
  }

  const date = (value: string) => formatDateOnly(value, i18n.language);
  const amount = (value: number, currency: string) =>
    `${value.toLocaleString(i18n.language)} ${currency}`;

  return (
    <Card withBorder padding="lg">
      <Group gap="xs" mb="md">
        <Receipt size={16} style={{ color: "var(--mantine-color-dimmed)" }} />
        <Title order={4}>{t("employees:payments.expense.title")}</Title>
      </Group>
      {isLoading ? (
        <Skeleton h={128} />
      ) : rows.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("employees:payments.expense.empty")}
        </Text>
      ) : isMobile ? (
        <ExpensePaymentsMobileList rows={rows} formatDate={date} formatAmount={amount} t={t} />
      ) : (
        <ExpensePaymentsTable rows={rows} formatDate={date} formatAmount={amount} t={t} />
      )}
    </Card>
  );
}

function ExpensePaymentsTable({
  rows,
  formatDate,
  formatAmount,
  t,
}: {
  rows: PaymentOverviewRow[];
  formatDate: (value: string) => string;
  formatAmount: (value: number, currency: string) => string;
  t: Translate;
}) {
  return (
    <Table.ScrollContainer minWidth={560}>
      <Table verticalSpacing="sm">
        <Table.Thead>
          <Table.Tr>
            <Table.Th>{t("expense:payments.col.kind")}</Table.Th>
            <Table.Th>{t("expense:payments.col.date")}</Table.Th>
            <Table.Th>{t("expense:payments.col.description")}</Table.Th>
            <Table.Th ta="right">{t("expense:payments.col.amount")}</Table.Th>
            <Table.Th>{t("expense:payments.col.status")}</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {rows.map((row) => (
            <Table.Tr key={`${row.kind}-${row.sourceId}`}>
              <Table.Td>{t(`expense:payments.kind.${row.kind}`)}</Table.Td>
              <Table.Td className="whitespace-nowrap">{formatDate(row.date)}</Table.Td>
              <Table.Td>{row.description ?? "—"}</Table.Td>
              <Table.Td ta="right" fw={500} className="whitespace-nowrap">
                {formatAmount(row.amount, row.currency)}
              </Table.Td>
              <Table.Td>
                <Badge color={STATUS_COLOR[row.status.toLowerCase()] ?? "gray"} variant="light">
                  {enumLabel(t, "expense:status", row.status)}
                </Badge>
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Table.ScrollContainer>
  );
}

/** ≤390px kart listesi: masaüstü tablonun aynı kolonları, kart biçiminde (payments.tsx'teki desenle aynı). */
function ExpensePaymentsMobileList({
  rows,
  formatDate,
  formatAmount,
  t,
}: {
  rows: PaymentOverviewRow[];
  formatDate: (value: string) => string;
  formatAmount: (value: number, currency: string) => string;
  t: Translate;
}) {
  return (
    <Stack gap="sm">
      {rows.map((row) => (
        <Card key={`${row.kind}-${row.sourceId}`} withBorder padding="sm" radius="md">
          <Group justify="space-between" align="flex-start" wrap="nowrap">
            <Badge color="gray" variant="light">
              {t(`expense:payments.kind.${row.kind}`)}
            </Badge>
            <Badge color={STATUS_COLOR[row.status.toLowerCase()] ?? "gray"} variant="light">
              {enumLabel(t, "expense:status", row.status)}
            </Badge>
          </Group>
          <Text size="sm" truncate="end" mt={6}>
            {row.description ?? "—"}
          </Text>
          <Group justify="space-between" mt={4}>
            <Text size="xs" c="dimmed">
              {formatDate(row.date)}
            </Text>
            <Text size="sm" fw={500}>
              {formatAmount(row.amount, row.currency)}
            </Text>
          </Group>
        </Card>
      ))}
    </Stack>
  );
}
