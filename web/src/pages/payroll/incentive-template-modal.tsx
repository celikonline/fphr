import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Badge, Button, Group, Modal, Skeleton, Stack, Text } from "@mantine/core";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { createIncentiveFromTemplate, listIncentiveTemplates, type IncentiveTemplate } from "@/services/payroll-incentives.service";
import { INCENTIVES_QUERY_KEY } from "./incentive-form";

const TEMPLATES_QUERY_KEY = [...INCENTIVES_QUERY_KEY, "templates"] as const;

interface IncentiveTemplateModalProps {
  opened: boolean;
  onClose: () => void;
}

/** "Şablondan ekle": the in-code catalog. A template can be added once per tenant (already added ones are disabled). */
export function IncentiveTemplateModal({ opened, onClose }: IncentiveTemplateModalProps) {
  const { t } = useTranslation(["payroll"]);
  return (
    <Modal opened={opened} onClose={onClose} size="44rem" title={t("payroll:incentives.templates.title")}>
      {opened && <TemplateList onClose={onClose} />}
    </Modal>
  );
}

function TemplateList({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation(["payroll", "common"]);
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const query = useQuery({ queryKey: TEMPLATES_QUERY_KEY, queryFn: listIncentiveTemplates });
  const templates = query.data ?? [];

  const add = useMutation({
    mutationFn: (template: IncentiveTemplate) => createIncentiveFromTemplate(template.templateKey),
    onSuccess: async (_id, template) => {
      await queryClient.invalidateQueries({ queryKey: INCENTIVES_QUERY_KEY });
      toast({ title: t("payroll:incentives.templates.added", { name: template.name }) });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("payroll:incentives.templates.addError"))),
  });

  return (
    <Stack gap="sm">
      <Text size="sm" c="dimmed">
        {t("payroll:incentives.templates.intro")}
      </Text>
      <Alert color="yellow" title={t("payroll:incentives.templates.reviewTitle")}>
        {t("payroll:incentives.templates.reviewRequired")}
      </Alert>
      {query.isError ? (
        <Text size="sm" c="red">
          {getApiErrorMessage(query.error, t("payroll:incentives.templates.loadError"))}
        </Text>
      ) : query.isLoading ? (
        <Skeleton h={96} />
      ) : (
        templates.map((template) => {
          const added = template.alreadyAdded ?? false;
          const historical = template.validTo != null && new Date(`${template.validTo}T23:59:59`) < new Date();
          return (
            <Group key={template.templateKey} justify="space-between" wrap="nowrap" align="flex-start" data-testid={`incentive-template-${template.templateKey}`}>
              <div>
                <Group gap="xs">
                  <Text fw={500}>{template.name}</Text>
                  <Text ff="monospace" size="xs" c="dimmed">
                    {template.code}
                  </Text>
                  {(template.preApproved ?? false) && (
                    <Badge color="green" variant="light">
                      {t("payroll:incentives.templates.preApproved")}
                    </Badge>
                  )}
                  {added && (
                    <Badge color="gray" variant="light">
                      {t("payroll:incentives.templates.alreadyAdded")}
                    </Badge>
                  )}
                  {historical && <Badge color="gray" variant="outline">{t("payroll:incentives.templates.historical")}</Badge>}
                </Group>
                <Text size="xs" c="dimmed">
                  {t(`payroll:incentives.kind.${template.kind}`, { defaultValue: template.kind })}
                  {template.lawReference ? ` · ${template.lawReference}` : ""}
                  {template.declarationCode ? ` · ${t("payroll:incentives.columns.declarationCode")}: ${template.declarationCode}` : ""}
                </Text>
                {template.validTo && <Text size="xs" c="dimmed">{t("payroll:incentives.templates.validUntil", { date: template.validTo })}</Text>}
              </div>
              <Button
                size="xs"
                variant="light"
                disabled={added || add.isPending}
                loading={add.isPending && add.variables?.templateKey === template.templateKey}
                aria-label={t("payroll:incentives.templates.addAria", { name: template.name })}
                onClick={() => {
                  setError(null);
                  add.mutate(template);
                }}
              >
                {t("payroll:incentives.templates.add")}
              </Button>
            </Group>
          );
        })
      )}
      <Text size="xs" c="dimmed">
        {t("payroll:incentives.templates.validFromNote")}
      </Text>
      {error && <Alert color="red">{error}</Alert>}
      <Group justify="flex-end">
        <Button variant="default" onClick={onClose}>
          {t("common:close")}
        </Button>
      </Group>
    </Stack>
  );
}
