import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import { Alert, Button, Card, FileInput, Group, List, Table, Text, Title } from "@mantine/core";
import { Receipt } from "lucide-react";
import { usePermission } from "@/components/permission-guard";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatMoney } from "@/lib/money";
import { matchPayrollBankReceipts, type PayrollPeriodStatus } from "@/services/payroll.service";

/**
 * Dekont eşleştirme: uploads a bank return file (generic CSV - IBAN, amount, optional status; works with any bank's own export
 * turned into this shape) and matches it against the period's expected payments. Only ever marks a payslip's payment confirmed;
 * mismatches, rejections and unmatched employees are reported so a bookkeeper can re-upload a corrected file, not written anywhere.
 */
export function PeriodReceiptMatching({ periodId, status }: { periodId: string; status: PayrollPeriodStatus }) {
  const { t, i18n } = useTranslation(["payroll"]);
  const canMatch = usePermission("payroll.bankfile.export");
  const [file, setFile] = useState<File | null>(null);
  const match = useMutation({
    mutationFn: async () => {
      const csvContent = await file!.text();
      return matchPayrollBankReceipts(periodId, csvContent);
    },
  });

  if (status === "open" || !canMatch) {
    return null;
  }

  const money = (value: number) => formatMoney(value, "TRY", i18n.language);
  const data = match.data;

  return (
    <Card withBorder padding="lg">
      <Title order={4} mb={4}>
        {t("payroll:receiptMatching.title")}
      </Title>
      <Text size="xs" c="dimmed" mb="sm">
        {t("payroll:receiptMatching.subtitle")}
      </Text>

      <Group align="flex-end" gap="sm" wrap="wrap">
        <FileInput
          placeholder={t("payroll:receiptMatching.filePlaceholder")}
          accept=".csv,text/csv,text/plain"
          value={file}
          onChange={setFile}
          clearable
          style={{ flex: 1, minWidth: 220 }}
        />
        <Button leftSection={<Receipt size={14} />} disabled={!file} loading={match.isPending} onClick={() => match.mutate()}>
          {t("payroll:receiptMatching.match")}
        </Button>
      </Group>

      {match.isError && (
        <Alert color="red" mt="sm">
          {getApiErrorMessage(match.error, t("payroll:receiptMatching.error"))}
        </Alert>
      )}

      {data && (
        <div className="mt-4 space-y-3">
          <Text size="sm">{t("payroll:receiptMatching.confirmed", { count: data.confirmedCount })}</Text>

          {data.amountMismatches.length > 0 && (
            <Alert color="yellow" title={t("payroll:receiptMatching.mismatchTitle")}>
              <Table verticalSpacing={4} horizontalSpacing="sm">
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t("payroll:receiptMatching.employee")}</Table.Th>
                    <Table.Th ta="right">{t("payroll:receiptMatching.expected")}</Table.Th>
                    <Table.Th ta="right">{t("payroll:receiptMatching.received")}</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {data.amountMismatches.map((line) => (
                    <Table.Tr key={line.employeeId}>
                      <Table.Td>{line.employeeName}</Table.Td>
                      <Table.Td ta="right">{money(line.expectedAmount)}</Table.Td>
                      <Table.Td ta="right">{money(line.receiptAmount)}</Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Alert>
          )}

          {data.rejected.length > 0 && (
            <Alert color="red" title={t("payroll:receiptMatching.rejectedTitle")}>
              <List size="sm">
                {data.rejected.map((line) => (
                  <List.Item key={line.employeeId}>
                    {line.employeeName} ({money(line.amount)})
                  </List.Item>
                ))}
              </List>
            </Alert>
          )}

          {data.unmatched.length > 0 && (
            <Alert color="gray" title={t("payroll:receiptMatching.unmatchedTitle")}>
              <List size="sm">
                {data.unmatched.map((line) => (
                  <List.Item key={line.employeeId}>
                    {line.employeeName} ({money(line.amount)})
                  </List.Item>
                ))}
              </List>
            </Alert>
          )}

          {data.unrecognizedRowCount > 0 && (
            <Text size="xs" c="dimmed">
              {t("payroll:receiptMatching.unrecognized", { count: data.unrecognizedRowCount })}
            </Text>
          )}
        </div>
      )}
    </Card>
  );
}
