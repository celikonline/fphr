import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FileText, Plus, Trash2 } from "lucide-react";
import {
  ActionIcon,
  Badge,
  Button,
  Card,
  Checkbox,
  Group,
  Modal,
  NumberInput,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip,
} from "@mantine/core";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  createFormTemplate,
  deactivateFormTemplate,
  listFormTemplates,
  reactivateFormTemplate,
  FORM_FIELD_TYPES,
  FORM_TEMPLATE_TYPES,
  type FormFieldInput,
  type FormFieldType,
  type FormTemplateType,
} from "@/services/recruitment.service";

const TEMPLATES_KEY = ["recruitment", "form-templates"] as const;

interface DraftField {
  key: string;
  label: string;
  fieldType: FormFieldType;
  isRequired: boolean;
  isSensitive: boolean;
  optionsText: string;
  weight: number | undefined;
}

function newDraftField(): DraftField {
  return { key: crypto.randomUUID(), label: "", fieldType: "paragraph", isRequired: false, isSensitive: false, optionsText: "", weight: undefined };
}

export default function RecruitmentFormTemplatesPage() {
  const canRead = usePermission("recruitment.form_templates.read");
  if (!canRead) return <NoAccess />;
  return <RecruitmentFormTemplates />;
}

function RecruitmentFormTemplates() {
  const { t } = useTranslation(["recruitment", "common"]);
  const queryClient = useQueryClient();
  const canManage = usePermission("recruitment.form_templates.manage");

  const { data, isLoading, isError, error } = useQuery({ queryKey: TEMPLATES_KEY, queryFn: () => listFormTemplates() });

  const [createOpen, setCreateOpen] = useState(false);
  const [name, setName] = useState("");
  const [templateType, setTemplateType] = useState<FormTemplateType>("feedback");
  const [fields, setFields] = useState<DraftField[]>([newDraftField()]);

  const invalidate = () => queryClient.invalidateQueries({ queryKey: TEMPLATES_KEY });

  const createMutation = useMutation({
    mutationFn: () => {
      const input: FormFieldInput[] = fields.map((f) => ({
        label: f.label,
        fieldType: f.fieldType,
        isRequired: f.isRequired,
        isSensitive: f.isSensitive,
        options: f.fieldType === "dropdown" ? f.optionsText.split(",").map((o) => o.trim()).filter(Boolean) : null,
        weight: f.fieldType === "scoreCard" ? (f.weight ?? 0) : null,
      }));
      return createFormTemplate(name, templateType, input);
    },
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("recruitment:formTemplates.createSuccess") });
      setCreateOpen(false);
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("recruitment:genericError")) }),
  });

  const deactivateMutation = useMutation({
    mutationFn: deactivateFormTemplate,
    onSuccess: () => invalidate(),
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("recruitment:genericError")) }),
  });

  const reactivateMutation = useMutation({
    mutationFn: reactivateFormTemplate,
    onSuccess: () => invalidate(),
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("recruitment:genericError")) }),
  });

  function openCreate() {
    setName("");
    setTemplateType("feedback");
    setFields([newDraftField()]);
    setCreateOpen(true);
  }

  function updateField(key: string, patch: Partial<DraftField>) {
    setFields((prev) => prev.map((f) => (f.key === key ? { ...f, ...patch } : f)));
  }

  const weightSum = fields.filter((f) => f.fieldType === "scoreCard").reduce((sum, f) => sum + (f.weight ?? 0), 0);

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <FileText size={24} /> {t("recruitment:formTemplates.title")}
          </Title>
          <Text c="dimmed">{t("recruitment:formTemplates.subtitle")}</Text>
        </div>
        {canManage && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={openCreate}>
            {t("recruitment:formTemplates.addTemplate")}
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
            {t("recruitment:formTemplates.empty")}
          </Text>
        ) : (
          <Table striped highlightOnHover verticalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("recruitment:formTemplates.name")}</Table.Th>
                <Table.Th>{t("recruitment:formTemplates.type")}</Table.Th>
                <Table.Th>{t("recruitment:formTemplates.fieldCount")}</Table.Th>
                <Table.Th>{t("recruitment:requisitions.status")}</Table.Th>
                {canManage && <Table.Th ta="right">{t("common:actions")}</Table.Th>}
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {data.map((template) => (
                <Table.Tr key={template.id}>
                  <Table.Td>
                    <Text fw={500}>{template.name}</Text>
                  </Table.Td>
                  <Table.Td>{t(`recruitment:formTemplates.types.${template.templateType}`)}</Table.Td>
                  <Table.Td>{template.fields.length}</Table.Td>
                  <Table.Td>
                    <Badge color={template.isActive ? "green" : "gray"} variant="light">
                      {template.isActive ? t("recruitment:formTemplates.active") : t("recruitment:formTemplates.inactive")}
                    </Badge>
                  </Table.Td>
                  {canManage && (
                    <Table.Td>
                      <Group justify="flex-end">
                        {template.isActive ? (
                          <Button size="xs" variant="subtle" color="red" onClick={() => deactivateMutation.mutate(template.id)}>
                            {t("recruitment:formTemplates.deactivate")}
                          </Button>
                        ) : (
                          <Button size="xs" variant="subtle" onClick={() => reactivateMutation.mutate(template.id)}>
                            {t("recruitment:formTemplates.reactivate")}
                          </Button>
                        )}
                      </Group>
                    </Table.Td>
                  )}
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Card>

      <Modal opened={createOpen} onClose={() => setCreateOpen(false)} title={t("recruitment:formTemplates.dialogCreateTitle")} size="lg">
        <Stack gap="md">
          <TextInput label={t("recruitment:formTemplates.name")} value={name} onChange={(e) => setName(e.currentTarget.value)} data-autofocus />
          <Select
            label={t("recruitment:formTemplates.type")}
            data={FORM_TEMPLATE_TYPES.map((v) => ({ value: v, label: t(`recruitment:formTemplates.types.${v}`) }))}
            value={templateType}
            onChange={(value) => value && setTemplateType(value as FormTemplateType)}
            allowDeselect={false}
          />

          {templateType === "scorecard" && (
            <Text size="sm" c={weightSum === 100 ? "dimmed" : "red"}>
              {t("recruitment:formTemplates.weightSum", { sum: weightSum })}
            </Text>
          )}

          <Stack gap="sm">
            {fields.map((field, index) => (
              <Card key={field.key} withBorder padding="sm">
                <Stack gap="xs">
                  <Group align="flex-end" gap="xs" wrap="nowrap">
                    <TextInput
                      className="flex-1"
                      label={index === 0 ? t("recruitment:formTemplates.fieldLabel") : undefined}
                      value={field.label}
                      onChange={(e) => updateField(field.key, { label: e.currentTarget.value })}
                    />
                    <Select
                      label={index === 0 ? t("recruitment:formTemplates.fieldType") : undefined}
                      data={FORM_FIELD_TYPES.map((v) => ({ value: v, label: t(`recruitment:formTemplates.fieldTypes.${v}`) }))}
                      value={field.fieldType}
                      onChange={(value) => value && updateField(field.key, { fieldType: value as FormFieldType })}
                      allowDeselect={false}
                    />
                    <Tooltip label={t("common:delete")}>
                      <ActionIcon
                        variant="subtle"
                        color="red"
                        disabled={fields.length === 1}
                        onClick={() => setFields((prev) => prev.filter((f) => f.key !== field.key))}
                      >
                        <Trash2 size={16} />
                      </ActionIcon>
                    </Tooltip>
                  </Group>
                  {field.fieldType === "dropdown" && (
                    <TextInput
                      label={t("recruitment:formTemplates.optionsCommaSeparated")}
                      value={field.optionsText}
                      onChange={(e) => updateField(field.key, { optionsText: e.currentTarget.value })}
                    />
                  )}
                  {field.fieldType === "scoreCard" && (
                    <NumberInput
                      label={t("recruitment:formTemplates.weight")}
                      min={0}
                      max={100}
                      value={field.weight}
                      onChange={(value) => updateField(field.key, { weight: typeof value === "number" ? value : undefined })}
                    />
                  )}
                  <Group gap="lg">
                    <Checkbox
                      label={t("recruitment:formTemplates.required")}
                      checked={field.isRequired}
                      onChange={(e) => updateField(field.key, { isRequired: e.currentTarget.checked })}
                    />
                    <Checkbox
                      label={t("recruitment:formTemplates.sensitive")}
                      checked={field.isSensitive}
                      onChange={(e) => updateField(field.key, { isSensitive: e.currentTarget.checked })}
                    />
                  </Group>
                </Stack>
              </Card>
            ))}
          </Stack>

          <Button type="button" variant="default" leftSection={<Plus size={14} />} onClick={() => setFields((prev) => [...prev, newDraftField()])}>
            {t("recruitment:formTemplates.addField")}
          </Button>

          <Group justify="flex-end" gap="sm">
            <Button type="button" variant="default" onClick={() => setCreateOpen(false)} disabled={createMutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button
              type="button"
              disabled={createMutation.isPending || !name.trim() || fields.some((f) => !f.label.trim())}
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
