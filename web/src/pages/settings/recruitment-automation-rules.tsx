import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Workflow } from "lucide-react";
import { Badge, Button, Card, Group, Modal, Select, Stack, Table, Text, Title } from "@mantine/core";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  createAutomationRule,
  listAutomationRules,
  listFormTemplates,
  APPLICATION_STAGES,
  type ApplicationStage,
} from "@/services/recruitment.service";

const RULES_KEY = ["recruitment", "automation-rules"] as const;

export default function RecruitmentAutomationRulesPage() {
  const canRead = usePermission("recruitment.automation_rules.read");
  if (!canRead) return <NoAccess />;
  return <RecruitmentAutomationRules />;
}

function RecruitmentAutomationRules() {
  const { t } = useTranslation(["recruitment", "common"]);
  const queryClient = useQueryClient();
  const canManage = usePermission("recruitment.automation_rules.manage");

  const { data, isLoading, isError, error } = useQuery({ queryKey: RULES_KEY, queryFn: () => listAutomationRules() });
  const { data: templates } = useQuery({ queryKey: ["recruitment", "form-templates", "active"], queryFn: () => listFormTemplates(true) });
  const templateNameById = new Map((templates ?? []).map((tpl) => [tpl.id, tpl.name]));

  const [createOpen, setCreateOpen] = useState(false);
  const [fromStage, setFromStage] = useState<ApplicationStage>("applied");
  const [toStage, setToStage] = useState<ApplicationStage>("screening");
  const [templateId, setTemplateId] = useState<string | null>(null);

  const invalidate = () => queryClient.invalidateQueries({ queryKey: RULES_KEY });

  const createMutation = useMutation({
    mutationFn: () => createAutomationRule(fromStage, toStage, templateId!),
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("recruitment:automationRules.createSuccess") });
      setCreateOpen(false);
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("recruitment:genericError")) }),
  });

  function openCreate() {
    setFromStage("applied");
    setToStage("screening");
    setTemplateId(null);
    setCreateOpen(true);
  }

  const templateOptions = useMemo(() => (templates ?? []).map((tpl) => ({ value: tpl.id, label: tpl.name })), [templates]);
  const stageOptions = APPLICATION_STAGES.map((s) => ({ value: s, label: t(`recruitment:applications.stages.${s}`) }));

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Workflow size={24} /> {t("recruitment:automationRules.title")}
          </Title>
          <Text c="dimmed">{t("recruitment:automationRules.subtitle")}</Text>
        </div>
        {canManage && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={openCreate}>
            {t("recruitment:automationRules.addRule")}
          </Button>
        )}
      </Group>

      <Card withBorder padding={0}>
        {isLoading ? (
          <Text p="md" size="sm" c="dimmed">
            {t("common:loading")}
          </Text>
        ) : isError ? (
          <Text p="md" size="sm" c="red">
            {getApiErrorMessage(error, t("recruitment:genericError"))}
          </Text>
        ) : !data || data.length === 0 ? (
          <Text p="md" size="sm" c="dimmed">
            {t("recruitment:automationRules.empty")}
          </Text>
        ) : (
          <Table striped highlightOnHover verticalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("recruitment:automationRules.transition")}</Table.Th>
                <Table.Th>{t("recruitment:automationRules.requiredForm")}</Table.Th>
                <Table.Th>{t("recruitment:requisitions.status")}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {data.map((rule) => (
                <Table.Tr key={rule.id}>
                  <Table.Td>
                    {t(`recruitment:applications.stages.${rule.fromStage}`)} → {t(`recruitment:applications.stages.${rule.toStage}`)}
                  </Table.Td>
                  <Table.Td>{templateNameById.get(rule.requiredFormTemplateId) ?? "—"}</Table.Td>
                  <Table.Td>
                    <Badge color={rule.isActive ? "green" : "gray"} variant="light">
                      {rule.isActive ? t("recruitment:formTemplates.active") : t("recruitment:formTemplates.inactive")}
                    </Badge>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Card>

      <Modal opened={createOpen} onClose={() => setCreateOpen(false)} title={t("recruitment:automationRules.dialogCreateTitle")}>
        <Stack gap="md">
          <Select
            label={t("recruitment:automationRules.fromStage")}
            data={stageOptions}
            value={fromStage}
            onChange={(value) => value && setFromStage(value as ApplicationStage)}
            allowDeselect={false}
          />
          <Select
            label={t("recruitment:automationRules.toStage")}
            data={stageOptions}
            value={toStage}
            onChange={(value) => value && setToStage(value as ApplicationStage)}
            allowDeselect={false}
          />
          <Select
            label={t("recruitment:automationRules.requiredForm")}
            data={templateOptions}
            value={templateId}
            onChange={setTemplateId}
            searchable
          />
          <Group justify="flex-end" gap="sm">
            <Button type="button" variant="default" onClick={() => setCreateOpen(false)} disabled={createMutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button
              type="button"
              disabled={createMutation.isPending || !templateId || fromStage === toStage}
              onClick={() => createMutation.mutate()}
            >
              {createMutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </div>
  );
}
