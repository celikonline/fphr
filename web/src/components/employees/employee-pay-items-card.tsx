import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { ActionIcon, Button, Card, Group, Skeleton, Table, Text, Title } from "@mantine/core";
import { Plus, Trash2 } from "lucide-react";
import { usePermission } from "@/components/permission-guard";
import { formatAssignmentRange } from "@/pages/payroll/period-pay-items";
import { PAY_ITEM_ASSIGNMENTS_QUERY_KEY, PayItemAssignModal, PayItemAssignmentDeleteConfirm } from "@/pages/payroll/pay-item-assign-modal";
import { listPayItemAssignments, type PayItemAssignment } from "@/services/payroll-pay-items.service";
import type { EmployeeDetail } from "@/services/employees.service";

/** Employee card > Bordro: the employee's recurring pay item assignments (read-only; add/remove with `payroll.payitem.manage`). */
export function EmployeePayItemsCard({ employee }: { employee: Pick<EmployeeDetail, "id" | "displayName"> }) {
  const { t, i18n } = useTranslation(["payroll"]);
  const canManage = usePermission("payroll.payitem.manage");
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState<PayItemAssignment | null>(null);
  const { data, isLoading, isError } = useQuery({
    queryKey: [...PAY_ITEM_ASSIGNMENTS_QUERY_KEY, "employee", employee.id],
    queryFn: () => listPayItemAssignments({ employeeId: employee.id }),
    retry: false,
  });
  const now = new Date();

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" align="flex-start" mb="sm" wrap="wrap">
        <Title order={4}>{t("payroll:assignments.employeeTitle")}</Title>
        {canManage && (
          <Button size="xs" leftSection={<Plus size={14} />} onClick={() => setAdding(true)}>
            {t("payroll:assignments.add")}
          </Button>
        )}
      </Group>
      {isLoading ? (
        <Skeleton h={64} />
      ) : isError || !data ? (
        <Text size="sm" c="red">
          {t("payroll:assignments.loadError")}
        </Text>
      ) : data.length === 0 ? (
        <Text size="sm" c="dimmed" data-testid="employee-assignments-empty">
          {t("payroll:assignments.employeeEmpty")}
        </Text>
      ) : (
        <Table.ScrollContainer minWidth={480}>
          <Table verticalSpacing="xs" horizontalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("payroll:assignments.payItem")}</Table.Th>
                <Table.Th ta="right">{t("payroll:assignments.value")}</Table.Th>
                <Table.Th>{t("payroll:assignments.range")}</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {data.map((item) => (
                <Table.Tr key={item.id}>
                  <Table.Td>
                    {item.code}
                    {item.name ? ` - ${item.name}` : ""}
                  </Table.Td>
                  <Table.Td ta="right">{item.value != null ? new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 4 }).format(item.value) : t("payroll:assignments.defaultValue")}</Table.Td>
                  <Table.Td>{formatAssignmentRange(item, t("payroll:assignments.openEnded"))}</Table.Td>
                  <Table.Td>
                    {canManage && (
                      <ActionIcon variant="subtle" color="red" aria-label={t("payroll:assignments.removeLabel", { code: item.code })} onClick={() => setRemoving(item)}>
                        <Trash2 size={16} />
                      </ActionIcon>
                    )}
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      <PayItemAssignModal
        opened={adding}
        onClose={() => setAdding(false)}
        fixedEmployee={{ id: employee.id, displayName: employee.displayName }}
        defaultYear={now.getFullYear()}
        defaultMonth={now.getMonth() + 1}
      />
      <PayItemAssignmentDeleteConfirm assignment={removing} onClose={() => setRemoving(null)} />
    </Card>
  );
}
