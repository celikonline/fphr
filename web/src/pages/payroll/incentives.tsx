import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BadgeCheck, FilePlus2, Pencil, Plus, Undo2, UserPlus } from "lucide-react";
import { ActionIcon, Badge, Button, Card, Checkbox, Group, Skeleton, Stack, Switch, Table, Tabs, Text, Title, Tooltip } from "@mantine/core";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { listIncentiveDefinitions, setIncentiveDefinitionActive, type IncentiveDefinition } from "@/services/payroll-incentives.service";
import { IncentiveAssignModal } from "./incentive-assign-modal";
import { IncentiveLegacyConvert } from "./incentive-legacy-convert";
import { IncentiveApproveModal, type IncentiveApprovalMode } from "./incentive-approve-modal";
import { INCENTIVES_QUERY_KEY, IncentiveForm } from "./incentive-form";
import { IncentiveTemplateModal } from "./incentive-template-modal";

/**
 * Bordro > Teşvikler (PAY-INCENTIVE-4): kiracıya özel teşvik modelleri. Okuma `payroll.trace.view`, oluşturma/düzenleme/aktif-pasif
 * `payroll.incentive.manage`, onay ve onay geri alma `payroll.incentive.approve` ister (düğmeler izinsiz kullanıcıya gösterilmez).
 */
export default function IncentivesPage() {
  const canView = usePermission("payroll.trace.view");
  const canManage = usePermission("payroll.incentive.manage");
  const canApprove = usePermission("payroll.incentive.approve");
  if (!canView && !canManage) return <NoAccess />;
  return <IncentivesPageBody canManage={canManage} canApprove={canApprove} />;
}

/** The definitions list, plus (with `payroll.incentive.manage`) the "Eski profil kodları" migration tab. */
function IncentivesPageBody({ canManage, canApprove }: { canManage: boolean; canApprove: boolean }) {
  const { t } = useTranslation(["payroll"]);
  if (!canManage) return <IncentivesList canManage={canManage} canApprove={canApprove} />;
  return (
    <Tabs defaultValue="definitions" keepMounted={false}>
      <Tabs.List px="md" pt="xs">
        <Tabs.Tab value="definitions">{t("payroll:incentiveLegacy.definitionsTab")}</Tabs.Tab>
        <Tabs.Tab value="legacy">{t("payroll:incentiveLegacy.tab")}</Tabs.Tab>
      </Tabs.List>
      <Tabs.Panel value="definitions">
        <IncentivesList canManage={canManage} canApprove={canApprove} />
      </Tabs.Panel>
      <Tabs.Panel value="legacy">
        <div className="w-full space-y-6 p-6 md:p-10">
          <Title order={2} size="h3">
            {t("payroll:incentiveLegacy.title")}
          </Title>
          <Card withBorder padding="lg">
            <IncentiveLegacyConvert />
          </Card>
        </div>
      </Tabs.Panel>
    </Tabs>
  );
}

function IncentivesList({ canManage, canApprove }: { canManage: boolean; canApprove: boolean }) {
  const { t, i18n } = useTranslation(["payroll", "common"]);
  const queryClient = useQueryClient();
  const [includeInactive, setIncludeInactive] = useState(true);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<IncentiveDefinition | null>(null);
  const [templatesOpen, setTemplatesOpen] = useState(false);
  const [assignOpen, setAssignOpen] = useState(false);
  const [approval, setApproval] = useState<{ item: IncentiveDefinition; mode: IncentiveApprovalMode } | null>(null);

  const query = useQuery({ queryKey: [...INCENTIVES_QUERY_KEY, { includeInactive }], queryFn: () => listIncentiveDefinitions(includeInactive) });
  const items = useMemo(() => query.data ?? [], [query.data]);

  const toggle = useMutation({
    mutationFn: (item: IncentiveDefinition) => setIncentiveDefinitionActive(item.id, !(item.isActive ?? true)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: INCENTIVES_QUERY_KEY }),
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("payroll:incentives.genericError")) }),
  });

  const openCreate = () => {
    setEditing(null);
    setFormOpen(true);
  };
  const openEdit = (item: IncentiveDefinition) => {
    setEditing(item);
    setFormOpen(true);
  };

  const approvalDetails = (item: IncentiveDefinition) => {
    const parts: string[] = [];
    if (item.approvedByName) parts.push(item.approvedByName);
    if (item.approvedAt) parts.push(new Date(item.approvedAt).toLocaleDateString(i18n.language));
    return parts.join(" · ");
  };

  return (
    <div className="w-full space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <BadgeCheck size={24} /> {t("payroll:incentives.title")}
          </Title>
          <Text c="dimmed">{t("payroll:incentives.subtitle")}</Text>
        </div>
        {canManage && (
          <Group gap="xs">
            <Button variant="default" leftSection={<UserPlus size={16} />} onClick={() => setAssignOpen(true)}>
              {t("payroll:incentiveAssignments.add")}
            </Button>
            <Button variant="default" leftSection={<FilePlus2 size={16} />} onClick={() => setTemplatesOpen(true)}>
              {t("payroll:incentives.fromTemplate")}
            </Button>
            <Button leftSection={<Plus size={16} />} onClick={openCreate}>
              {t("payroll:incentives.new")}
            </Button>
          </Group>
        )}
      </Group>

      <Card withBorder padding="lg">
        <Checkbox
          mb="md"
          label={t("payroll:incentives.showInactive")}
          checked={includeInactive}
          onChange={(event) => setIncludeInactive(event.currentTarget.checked)}
        />

        {query.isError ? (
          <Text size="sm" c="red" ta="center" p="lg">
            {getApiErrorMessage(query.error, t("payroll:incentives.genericError"))}
          </Text>
        ) : query.isLoading ? (
          <Skeleton h={96} />
        ) : items.length === 0 ? (
          <Stack align="center" gap="sm" p="lg" data-testid="incentives-empty">
            <Text size="sm" c="dimmed" ta="center" maw={560}>
              {t("payroll:incentives.empty")}
            </Text>
            {canManage && (
              <Group gap="xs">
                <Button variant="default" leftSection={<FilePlus2 size={16} />} onClick={() => setTemplatesOpen(true)}>
                  {t("payroll:incentives.fromTemplate")}
                </Button>
                <Button leftSection={<Plus size={16} />} onClick={openCreate}>
                  {t("payroll:incentives.new")}
                </Button>
              </Group>
            )}
          </Stack>
        ) : (
          <Table.ScrollContainer minWidth={820}>
            <Table verticalSpacing="xs" horizontalSpacing="sm">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t("payroll:incentives.columns.code")}</Table.Th>
                  <Table.Th>{t("payroll:incentives.columns.name")}</Table.Th>
                  <Table.Th>{t("payroll:incentives.columns.kind")}</Table.Th>
                  <Table.Th>{t("payroll:incentives.columns.lawReference")}</Table.Th>
                  <Table.Th>{t("payroll:incentives.columns.declarationCode")}</Table.Th>
                  <Table.Th>{t("payroll:incentives.columns.approval")}</Table.Th>
                  <Table.Th>{t("payroll:incentives.columns.status")}</Table.Th>
                  <Table.Th />
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {items.map((item) => {
                  const active = item.isActive ?? true;
                  const approved = item.isSmmmApproved ?? false;
                  const details = approvalDetails(item);
                  return (
                    <Table.Tr key={item.id}>
                      <Table.Td>
                        <Text ff="monospace" size="sm">
                          {item.code}
                        </Text>
                      </Table.Td>
                      <Table.Td>{item.name}</Table.Td>
                      <Table.Td>
                        <Text size="sm">{t(`payroll:incentives.kind.${item.kind}`, { defaultValue: item.kind })}</Text>
                        {item.category && (
                          <Text size="xs" c="dimmed">
                            {t(`payroll:incentives.category.${item.category}`, { defaultValue: item.category })}
                          </Text>
                        )}
                      </Table.Td>
                      <Table.Td>{item.lawReference ?? "-"}</Table.Td>
                      <Table.Td>
                        <Text ff="monospace" size="sm">
                          {item.declarationCode ?? "-"}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <Tooltip label={item.approvalNote || details} disabled={!item.approvalNote && !details} multiline maw={320}>
                          <Badge color={approved ? "green" : "yellow"} variant="light">
                            {approved ? t("payroll:incentives.approval.approved") : t("payroll:incentives.approval.pending")}
                          </Badge>
                        </Tooltip>
                        {approved && details && (
                          <Text size="xs" c="dimmed">
                            {details}
                          </Text>
                        )}
                        {item.isComplete === false && (
                          <Text size="xs" c="orange">
                            {t("payroll:incentives.approval.incomplete")}
                          </Text>
                        )}
                      </Table.Td>
                      <Table.Td>
                        <Badge color={active ? "green" : "gray"} variant="light">
                          {active ? t("payroll:incentives.status.active") : t("payroll:incentives.status.inactive")}
                        </Badge>
                      </Table.Td>
                      <Table.Td>
                        <Group gap="xs" justify="flex-end" wrap="nowrap">
                          {canApprove && !approved && (
                            <Button size="compact-xs" variant="light" color="green" leftSection={<BadgeCheck size={14} />} onClick={() => setApproval({ item, mode: "approve" })}>
                              {t("payroll:incentives.approve")}
                            </Button>
                          )}
                          {canApprove && approved && (
                            <Button size="compact-xs" variant="light" color="red" leftSection={<Undo2 size={14} />} onClick={() => setApproval({ item, mode: "revoke" })}>
                              {t("payroll:incentives.revoke")}
                            </Button>
                          )}
                          {canManage && (
                            <>
                              <Switch
                                size="xs"
                                checked={active}
                                aria-label={t("payroll:incentives.toggleActive", { code: item.code })}
                                disabled={toggle.isPending && toggle.variables?.id === item.id}
                                onChange={() => toggle.mutate(item)}
                              />
                              <ActionIcon variant="subtle" aria-label={t("payroll:incentives.edit", { code: item.code })} onClick={() => openEdit(item)}>
                                <Pencil size={16} />
                              </ActionIcon>
                            </>
                          )}
                        </Group>
                      </Table.Td>
                    </Table.Tr>
                  );
                })}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        )}
      </Card>

      {canManage && <IncentiveForm opened={formOpen} onClose={() => setFormOpen(false)} item={editing} />}
      {canManage && <IncentiveAssignModal opened={assignOpen} onClose={() => setAssignOpen(false)} defaultYear={new Date().getFullYear()} defaultMonth={new Date().getMonth() + 1} />}
      {canManage && <IncentiveTemplateModal opened={templatesOpen} onClose={() => setTemplatesOpen(false)} />}
      {canApprove && <IncentiveApproveModal opened={approval !== null} onClose={() => setApproval(null)} mode={approval?.mode ?? "approve"} item={approval?.item ?? null} />}
    </div>
  );
}
