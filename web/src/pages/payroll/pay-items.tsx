import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Calculator, Pencil, Plus } from "lucide-react";
import { ActionIcon, Badge, Button, Card, Checkbox, Group, Skeleton, Stack, Switch, Table, Text, Title } from "@mantine/core";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { listPayItems, setPayItemActive, type PayItemDefinition } from "@/services/payroll-pay-items.service";
import { DirectionBadge } from "./pay-item-direction";
import { PAY_ITEMS_QUERY_KEY, PayItemForm } from "./pay-item-form";

/**
 * Bordro > Ödeme türleri (PAY-FORMULA-4): formülle tanımlanan kazanç kalemlerinin listesi. Okuma `payroll.trace.view`,
 * oluşturma/düzenleme/aktif-pasif `payroll.payitem.manage` ister (düğmeler izinsiz kullanıcıya gösterilmez).
 */
export default function PayItemsPage() {
  const canView = usePermission("payroll.trace.view");
  const canManage = usePermission("payroll.payitem.manage");
  if (!canView && !canManage) return <NoAccess />;
  return <PayItemsList canManage={canManage} />;
}

function PayItemsList({ canManage }: { canManage: boolean }) {
  const { t } = useTranslation(["payroll", "common"]);
  const queryClient = useQueryClient();
  const [includeInactive, setIncludeInactive] = useState(true);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<PayItemDefinition | null>(null);

  const query = useQuery({ queryKey: [...PAY_ITEMS_QUERY_KEY, { includeInactive }], queryFn: () => listPayItems(includeInactive) });
  const items = useMemo(() => query.data ?? [], [query.data]);

  const toggle = useMutation({
    mutationFn: (item: PayItemDefinition) => setPayItemActive(item.id, !(item.isActive ?? true)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PAY_ITEMS_QUERY_KEY }),
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("payroll:payItems.genericError")) }),
  });

  const openCreate = () => {
    setEditing(null);
    setFormOpen(true);
  };
  const openEdit = (item: PayItemDefinition) => {
    setEditing(item);
    setFormOpen(true);
  };

  const flags = (item: PayItemDefinition) => {
    if (item.direction === "deduction") return "—"; // a deduction never touches the tax bases
    const labels: string[] = [];
    if (item.isSgkSubject ?? true) labels.push(t("payroll:payItems.flags.sgkShort"));
    if (item.isIncomeTaxSubject ?? true) labels.push(t("payroll:payItems.flags.incomeTaxShort"));
    if (item.isStampTaxSubject ?? true) labels.push(t("payroll:payItems.flags.stampTaxShort"));
    return labels.length > 0 ? labels.join(" · ") : t("payroll:payItems.flags.none");
  };

  return (
    <div className="w-full space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Calculator size={24} /> {t("payroll:payItems.title")}
          </Title>
          <Text c="dimmed">{t("payroll:payItems.subtitle")}</Text>
        </div>
        {canManage && (
          <Button leftSection={<Plus size={16} />} onClick={openCreate}>
            {t("payroll:payItems.new")}
          </Button>
        )}
      </Group>

      <Card withBorder padding="lg">
        <Checkbox
          mb="md"
          label={t("payroll:payItems.showInactive")}
          checked={includeInactive}
          onChange={(event) => setIncludeInactive(event.currentTarget.checked)}
        />

        {query.isError ? (
          <Text size="sm" c="red" ta="center" p="lg">
            {getApiErrorMessage(query.error, t("payroll:payItems.genericError"))}
          </Text>
        ) : query.isLoading ? (
          <Skeleton h={96} />
        ) : items.length === 0 ? (
          <Stack align="center" gap="sm" p="lg" data-testid="pay-items-empty">
            <Text size="sm" c="dimmed" ta="center" maw={560}>
              {t("payroll:payItems.empty")}
            </Text>
            {canManage && (
              <Button leftSection={<Plus size={16} />} onClick={openCreate}>
                {t("payroll:payItems.new")}
              </Button>
            )}
          </Stack>
        ) : (
          <Table.ScrollContainer minWidth={640}>
            <Table verticalSpacing="xs" horizontalSpacing="sm">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t("payroll:payItems.columns.code")}</Table.Th>
                  <Table.Th>{t("payroll:payItems.columns.name")}</Table.Th>
                  <Table.Th>{t("payroll:payItems.directionColumn")}</Table.Th>
                  <Table.Th>{t("payroll:payItems.columns.amountBasis")}</Table.Th>
                  <Table.Th>{t("payroll:payItems.columns.taxFlags")}</Table.Th>
                  <Table.Th>{t("payroll:payItems.columns.status")}</Table.Th>
                  <Table.Th />
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {items.map((item) => {
                  const active = item.isActive ?? true;
                  return (
                    <Table.Tr key={item.id}>
                      <Table.Td>
                        <Text ff="monospace" size="sm">
                          {item.code}
                        </Text>
                      </Table.Td>
                      <Table.Td>{item.name}</Table.Td>
                      <Table.Td>
                        <DirectionBadge direction={item.direction ?? "earning"} />
                      </Table.Td>
                      <Table.Td>{t(`payroll:payItems.basis.${item.amountBasis}`, { defaultValue: item.amountBasis })}</Table.Td>
                      <Table.Td>{flags(item)}</Table.Td>
                      <Table.Td>
                        <Badge color={active ? "green" : "gray"} variant="light">
                          {active ? t("payroll:payItems.status.active") : t("payroll:payItems.status.inactive")}
                        </Badge>
                      </Table.Td>
                      <Table.Td>
                        {canManage && (
                          <Group gap="xs" justify="flex-end" wrap="nowrap">
                            <Switch
                              size="xs"
                              checked={active}
                              aria-label={t("payroll:payItems.toggleActive", { code: item.code })}
                              disabled={toggle.isPending && toggle.variables?.id === item.id}
                              onChange={() => toggle.mutate(item)}
                            />
                            <ActionIcon variant="subtle" aria-label={t("payroll:payItems.edit", { code: item.code })} onClick={() => openEdit(item)}>
                              <Pencil size={16} />
                            </ActionIcon>
                          </Group>
                        )}
                      </Table.Td>
                    </Table.Tr>
                  );
                })}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        )}
      </Card>

      {canManage && <PayItemForm opened={formOpen} onClose={() => setFormOpen(false)} item={editing} />}
    </div>
  );
}
