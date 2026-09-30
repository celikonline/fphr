import { Fragment, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Sparkles } from "lucide-react";
import {
  Badge,
  Button,
  Card,
  Checkbox,
  Group,
  Modal,
  NumberInput,
  Select,
  Skeleton,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import { ImportanceChip } from "@/components/onboarding/importance-chip";
import { useRoleLabel } from "@/components/onboarding/onboarding-task-table";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  ONBOARDING_IMPORTANCES,
  ONBOARDING_STAGES,
  RESPONSIBLE_ROLES,
  createOnboardingTemplate,
  listOnboardingCategories,
  listOnboardingTemplates,
  seedDefaultOnboardingTemplates,
  setOnboardingTemplateActive,
  updateOnboardingTemplate,
  type OnboardingCategory,
  type OnboardingImportance,
  type OnboardingStage,
  type OnboardingTaskTemplate,
} from "@/services/onboarding.service";

const TEMPLATES_KEY = ["onboarding", "templates"] as const;
/** Server limit (OnboardingLimits.TaskTemplateTitleMaxLength). */
const TITLE_MAX_LENGTH = 200;

function useNotifyError() {
  const { t } = useTranslation(["onboarding", "common"]);
  return (error: unknown) =>
    toast({
      variant: "destructive",
      title: t("common:error"),
      description: getApiErrorMessage(error, t("onboarding:actionFailed")),
    });
}

/** Görev şablonları (onboarding.templates.manage): aşamaya göre liste, ekle/düzenle, aktif/pasif, önerilenleri yükle. */
export default function OnboardingTemplates() {
  const { t } = useTranslation(["onboarding", "common"]);
  const queryClient = useQueryClient();
  const notifyError = useNotifyError();
  const roleLabel = useRoleLabel();
  const [editing, setEditing] = useState<
    OnboardingTaskTemplate | { stage: OnboardingStage } | null
  >(null);
  const query = useQuery({ queryKey: TEMPLATES_KEY, queryFn: listOnboardingTemplates });
  const categoriesQuery = useQuery({
    queryKey: ["onboarding", "categories"],
    queryFn: listOnboardingCategories,
  });
  const categories = categoriesQuery.data ?? [];
  const refresh = () => void queryClient.invalidateQueries({ queryKey: TEMPLATES_KEY });

  const seedMutation = useMutation({
    mutationFn: seedDefaultOnboardingTemplates,
    onSuccess: (count) => {
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("onboarding:templates.seeded", { count }),
      });
      refresh();
    },
    onError: notifyError,
  });
  const activeMutation = useMutation({
    mutationFn: ({ id, active }: { id: string; active: boolean }) =>
      setOnboardingTemplateActive(id, active),
    onSuccess: refresh,
    onError: notifyError,
  });

  const templates = query.data ?? [];

  if (query.isLoading) return <Skeleton height={200} />;
  if (query.isError) return <Text c="red">{t("onboarding:loadError")}</Text>;

  return (
    <Stack gap="lg">
      {templates.length === 0 && (
        <Card withBorder>
          <Group justify="space-between" wrap="wrap">
            <Text size="sm">{t("onboarding:templates.emptyHint")}</Text>
            <Button
              leftSection={<Sparkles size={16} />}
              loading={seedMutation.isPending}
              onClick={() => seedMutation.mutate()}
            >
              {t("onboarding:templates.seed")}
            </Button>
          </Group>
        </Card>
      )}

      {ONBOARDING_STAGES.map((stage) => {
        const rows = templates.filter((template) => template.stage === stage);
        return (
          <Stack key={stage} gap="sm">
            <Group justify="space-between">
              <Title order={5}>{t(`onboarding:stages.${stage}`)}</Title>
              <Button
                size="xs"
                variant="light"
                leftSection={<Plus size={14} />}
                onClick={() => setEditing({ stage })}
              >
                {t("onboarding:templates.add")}
              </Button>
            </Group>
            {rows.length === 0 ? (
              <Text size="sm" c="dimmed">
                {t("onboarding:templates.stageEmpty")}
              </Text>
            ) : (
              <Table.ScrollContainer minWidth={640}>
                <Table striped>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>{t("onboarding:fields.task")}</Table.Th>
                      <Table.Th>{t("onboarding:fields.responsible")}</Table.Th>
                      <Table.Th>{t("onboarding:fields.offset")}</Table.Th>
                      <Table.Th>{t("onboarding:fields.active")}</Table.Th>
                      <Table.Th />
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {groupByCategory(rows, categories).map((group) => (
                      <Fragment key={group.key}>
                        <Table.Tr>
                          <Table.Td colSpan={5} bg="var(--mantine-color-gray-light)">
                            <Text size="xs" fw={700}>
                              {group.name ?? t("onboarding:categories.uncategorized")}
                            </Text>
                          </Table.Td>
                        </Table.Tr>
                        {group.items.map((template) => (
                          <Table.Tr key={template.id} opacity={template.isActive ? 1 : 0.55}>
                            <Table.Td>
                              <Group gap={6} wrap="nowrap">
                                <Text size="sm">{template.title}</Text>
                                <ImportanceChip importance={template.importance} />
                                {template.requiresEDocument && (
                                  <Badge size="xs" variant="outline">
                                    {t("onboarding:fields.eDocument")}
                                  </Badge>
                                )}
                                {template.isMandatory && (
                                  <Badge size="xs" color="red" variant="outline">
                                    {t("onboarding:fields.mandatory")}
                                  </Badge>
                                )}
                              </Group>
                            </Table.Td>
                            <Table.Td>{roleLabel(template.responsibleRoleCode)}</Table.Td>
                            <Table.Td>{offsetLabel(template.dueDateOffsetDays, t)}</Table.Td>
                            <Table.Td>
                              <Switch
                                aria-label={t("onboarding:fields.active")}
                                checked={template.isActive}
                                onChange={(e) =>
                                  activeMutation.mutate({
                                    id: template.id,
                                    active: e.currentTarget.checked,
                                  })
                                }
                              />
                            </Table.Td>
                            <Table.Td>
                              <Button
                                size="compact-sm"
                                variant="subtle"
                                onClick={() => setEditing(template)}
                              >
                                {t("onboarding:templates.edit")}
                              </Button>
                            </Table.Td>
                          </Table.Tr>
                        ))}
                      </Fragment>
                    ))}
                  </Table.Tbody>
                </Table>
              </Table.ScrollContainer>
            )}
          </Stack>
        );
      })}

      {editing && (
        <TemplateDialog
          template={"id" in editing ? editing : null}
          stage={editing.stage}
          categories={categories}
          onClose={() => setEditing(null)}
          onSaved={refresh}
        />
      )}
    </Stack>
  );
}

interface TemplateGroup {
  key: string;
  name: string | null;
  items: OnboardingTaskTemplate[];
}

/** Groups templates by category in category sort order; uncategorized (or unknown category) last. */
export function groupByCategory(
  templates: OnboardingTaskTemplate[],
  categories: OnboardingCategory[]
): TemplateGroup[] {
  const known = [...categories].sort((a, b) => a.sortOrder - b.sortOrder);
  const groups: TemplateGroup[] = known
    .map((category) => ({
      key: category.id,
      name: category.name,
      items: templates.filter((template) => template.categoryId === category.id),
    }))
    .filter((group) => group.items.length > 0);
  const knownIds = new Set(known.map((category) => category.id));
  const rest = templates.filter(
    (template) => !template.categoryId || !knownIds.has(template.categoryId)
  );
  return rest.length > 0 ? [...groups, { key: "none", name: null, items: rest }] : groups;
}

function offsetLabel(
  days: number,
  t: (key: string, options?: Record<string, unknown>) => string
): string {
  if (days === 0) return t("onboarding:offset.sameDay");
  return days < 0
    ? t("onboarding:offset.before", { count: -days })
    : t("onboarding:offset.after", { count: days });
}

interface TemplateDialogProps {
  template: OnboardingTaskTemplate | null;
  stage: OnboardingStage;
  categories: OnboardingCategory[];
  onClose: () => void;
  onSaved: () => void;
}

function TemplateDialog({ template, stage, categories, onClose, onSaved }: TemplateDialogProps) {
  const { t } = useTranslation(["onboarding", "common"]);
  const notifyError = useNotifyError();
  const roleLabel = useRoleLabel();
  const [title, setTitle] = useState(template?.title ?? "");
  const [role, setRole] = useState<string>(template?.responsibleRoleCode ?? "hr");
  const [offset, setOffset] = useState<number>(template?.dueDateOffsetDays ?? 0);
  const [mandatory, setMandatory] = useState(template?.isMandatory ?? false);
  const [importance, setImportance] = useState<OnboardingImportance>(
    template?.importance ?? "normal"
  );
  const [requiresEDocument, setRequiresEDocument] = useState(template?.requiresEDocument ?? false);
  const [categoryId, setCategoryId] = useState<string | null>(template?.categoryId ?? null);
  const categoryOptions = categories
    .filter((category) => category.isActive || category.id === categoryId)
    .map((category) => ({ value: category.id, label: category.name }));

  const roleOptions = [...new Set<string>([...RESPONSIBLE_ROLES, role])].map((code) => ({
    value: code,
    label: roleLabel(code),
  }));
  const dialogTitle = `${t(template ? "onboarding:templates.editTitle" : "onboarding:templates.addTitle")} · ${t(
    `onboarding:stages.${stage}`
  )}`;

  const mutation = useMutation({
    mutationFn: () => {
      const input = {
        stage,
        title: title.trim(),
        responsibleRoleCode: role,
        dueDateOffsetDays: offset,
        isMandatory: mandatory,
        importance,
        requiresEDocument,
        categoryId,
      };
      return template
        ? updateOnboardingTemplate(template.id, input)
        : createOnboardingTemplate(input);
    },
    onSuccess: () => {
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("onboarding:templates.saved"),
      });
      onSaved();
      onClose();
    },
    onError: notifyError,
  });

  return (
    <Modal opened onClose={onClose} title={dialogTitle} centered>
      <Stack gap="md">
        <TextInput
          id="onboarding-template-title"
          label={t("onboarding:fields.task")}
          value={title}
          maxLength={TITLE_MAX_LENGTH}
          required
          onChange={(e) => setTitle(e.currentTarget.value)}
        />
        <Select
          id="onboarding-template-role"
          label={t("onboarding:fields.responsible")}
          data={roleOptions}
          value={role}
          allowDeselect={false}
          onChange={(value) => value && setRole(value)}
        />
        <NumberInput
          id="onboarding-template-offset"
          label={t("onboarding:fields.offset")}
          description={t(
            stage === "onboarding"
              ? "onboarding:templates.offsetHintHire"
              : "onboarding:templates.offsetHintExit"
          )}
          value={offset}
          allowDecimal={false}
          onChange={(value) => setOffset(typeof value === "number" ? value : 0)}
        />
        <Select
          id="onboarding-template-importance"
          label={t("onboarding:fields.importance")}
          data={ONBOARDING_IMPORTANCES.map((value) => ({
            value,
            label: t(`onboarding:importance.${value}`),
          }))}
          value={importance}
          allowDeselect={false}
          onChange={(value) => value && setImportance(value as OnboardingImportance)}
        />
        <Select
          id="onboarding-template-category"
          label={t("onboarding:fields.category")}
          data={categoryOptions}
          value={categoryId}
          clearable
          placeholder={t("onboarding:categories.uncategorized")}
          onChange={setCategoryId}
        />
        <Checkbox
          label={t("onboarding:fields.mandatory")}
          checked={mandatory}
          onChange={(e) => setMandatory(e.currentTarget.checked)}
        />
        <Checkbox
          label={t("onboarding:fields.eDocument")}
          checked={requiresEDocument}
          onChange={(e) => setRequiresEDocument(e.currentTarget.checked)}
        />
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={onClose} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button
            onClick={() => mutation.mutate()}
            disabled={!title.trim()}
            loading={mutation.isPending}
          >
            {t("common:save")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
