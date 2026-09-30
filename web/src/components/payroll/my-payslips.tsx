import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Badge, Card, Skeleton, Table, Text, Title } from "@mantine/core";
import { formatMoney } from "@/lib/money";
import { listMyPayslips, type MyPayslip } from "@/services/payslip-ack.service";
import { PayslipAcknowledge } from "./payslip-acknowledge";

export function payslipAckState(payslip: MyPayslip): "pending" | "accepted" | "rejected" | "acknowledged" | "awaiting" {
  if (payslip.objectionStatus) return payslip.objectionStatus;
  return payslip.acknowledgedAt ? "acknowledged" : "awaiting";
}

const STATE_COLOR = {
  awaiting: "gray",
  acknowledged: "green",
  pending: "yellow",
  accepted: "blue",
  rejected: "red",
} as const;

/** "Bordrolarım": çalışanın yayınlanmış bordroları, onay/itiraz durumu ve eylemleri. */
export function MyPayslips() {
  const { t, i18n } = useTranslation(["payroll"]);
  const { data, isLoading } = useQuery({
    queryKey: ["my-payslips"],
    queryFn: listMyPayslips,
    retry: false,
  });

  const period = (p: MyPayslip) =>
    new Intl.DateTimeFormat(i18n.language, { month: "long", year: "numeric" }).format(
      new Date(p.year, p.month - 1, 1)
    );

  return (
    <Card withBorder padding="lg">
      <Title order={4} mb="md">
        {t("payroll:myPayslips.title")}
      </Title>
      {isLoading ? (
        <Skeleton h={96} />
      ) : !data || data.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("payroll:myPayslips.empty")}
        </Text>
      ) : (
        <Table.ScrollContainer minWidth={560}>
          <Table verticalSpacing="xs" horizontalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("payroll:myPayslips.columns.period")}</Table.Th>
                <Table.Th ta="right">{t("payroll:myPayslips.columns.gross")}</Table.Th>
                <Table.Th ta="right">{t("payroll:myPayslips.columns.net")}</Table.Th>
                <Table.Th>{t("payroll:myPayslips.columns.status")}</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {data.map((p) => {
                const state = payslipAckState(p);
                return (
                  <Table.Tr key={p.payslipId}>
                    <Table.Td>{period(p)}</Table.Td>
                    <Table.Td ta="right">{formatMoney(p.grossPay, p.currency ?? "TRY", i18n.language)}</Table.Td>
                    <Table.Td ta="right">{formatMoney(p.netPay, p.currency ?? "TRY", i18n.language)}</Table.Td>
                    <Table.Td>
                      <Badge color={STATE_COLOR[state]} variant="light">
                        {t(`payroll:myPayslips.state.${state}`)}
                      </Badge>
                      {state === "pending" ? (
                        <Text size="xs" c="dimmed">
                          {t("payroll:myPayslips.pendingHint")}
                        </Text>
                      ) : null}
                    </Table.Td>
                    <Table.Td>
                      <PayslipAcknowledge payslip={p} />
                    </Table.Td>
                  </Table.Tr>
                );
              })}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
    </Card>
  );
}
