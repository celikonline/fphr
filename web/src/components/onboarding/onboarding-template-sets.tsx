import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowDown, ArrowUp, Plus } from "lucide-react";
import {
  ActionIcon,
  Button,
  Checkbox,
  Group,
  Modal,
  SegmentedControl,
  Skeleton,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
} from "@mantine/core";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  ONBOARDING_STAGES,
  createOnboardingTemplateSet,
  listOnboardingTemplateSets,
  listOnboardingTemplates,
  updateOnboardingTemplateSet,
  type OnboardingStage,
  type OnboardingTaskTemplate,
  type OnboardingTemplateSet,
} from "@/services/onboarding.service";

const SETS_KEY = ["onboarding", "template-sets"] as const;
const TEMPLATES_KEY = ["onboarding", "templates"] as const;
const NAME_MAX_LENGTH = 200;

/** Moves the item at `index` by `delta` positions; returns a new array (unchanged when out of range). */
export function moveItem<T>(items: readonly T[], index: number, delta: number): T[] {
  const target = index + delta;
  if (target < 0 || target >= items.length) return [...items];
  const next = [...items];
  [next[index], next[target]] = [next[target], next[index]];
  return next;
}

/** İsimli şablon setleri (onboarding.templates.manage): oluştur/düzenle, sıralı görev seçimi, aktif/pasif. */
export default function OnboardingTemplateSets() {
  const { t } = useTranslation(["onboarding", "common"]);
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<OnboardingTemplateSet | "new" | null>(null);
  const sets = useQuery({ queryKey: SETS_KEY, queryFn: listOnboardingTemplateSets });
  const templates = useQuery({ queryKey: TEMPLATES_KEY, queryFn: listOnboardingTemplates });
  const refresh = () => void queryClient.invalidateQueries({ queryKey: SETS_KEY });
  const activeMutation = useMutation({
    mutationFn: (set: OnboardingTemplateSet) =>
      updateOnboardingTemplateSet(set.id, {
        name: set.name,
        templateIds: set.templateIds,
        isActive: !set.isActive,
      }),
    onSuccess: refresh,
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("onboarding:actionFailed")),
      }),
  });

  if (sets.isLoading || templates.isLoading) return <Skeleton height={160} />;
  if (sets.isError || templates.isError) return <Text c="red">{t("onboarding:loadError")}</Text>;
  const rows = sets.data ?? [];

  return (
    <Stack gap="md">
      <Group justify="space-between">
        <Text size="sm" c="dimmed">
          {t("onboarding:sets.hint")}
        </Text>
        <Button size="xs" leftSection={<Plus size={14} />} onClick={() => setEditing("new")}>
          {t("onboarding:sets.add")}
        </Button>
      </Group>
      {rows.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("onboarding:sets.empty")}
        </Text>
      ) : (
        <Table striped>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>{t("onboarding:sets.name")}</Table.Th>
              <Table.Th>{t("onboarding:fields.stage")}</Table.Th>
              <Table.Th>{t("onboarding:sets.taskCount")}</Table.Th>
              <Table.Th>{t("onboarding:fields.active")}</Table.Th>
              <Table.Th />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.map((set) => (
              <Table.Tr key={set.id} opacity={set.isActive ? 1 : 0.55}>
                <Table.Td>{set.name}</Table.Td>
                <Table.Td>{t(`onboarding:stages.${set.stage}`)}</Table.Td>
                <Table.Td>{set.templateIds.length}</Table.Td>
                <Table.Td>
                  <Switch
                    aria-label={t("onboarding:fields.active")}
                    checked={set.isActive}
                    onChange={() => activeMutation.mutate(set)}
                  />
                </Table.Td>
                <Table.Td>
                  <Button size="compact-sm" variant="subtle" onClick={() => setEditing(set)}>
                    {t("onboarding:templates.edit")}
                  </Button>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}
      {editing && (
        <SetDialog
          set={editing === "new" ? null : editing}
          templates={templates.data ?? []}
          onClose={() => setEditing(null)}
          onSaved={refresh}
        />
      )}
    </Stack>
  );
}

interface SetDialogProps {
  set: OnboardingTemplateSet | null;
  templates: OnboardingTaskTemplate[];
  onClose: () => void;
  onSaved: () => void;
}

export function SetDialog({ set, templates, onClose, onSaved }: SetDialogProps) {
  const { t } = useTranslation(["onboarding", "common"]);
  const [name, setName] = useState(set?.name ?? "");
  const [stage, setStage] = useState<OnboardingStage>(set?.stage ?? "onboarding");
  const [selected, setSelected] = useState<string[]>(set?.templateIds ?? []);
  const byId = new Map(templates.map((template) => [template.id, template]));
  // Only same-stage, active templates can join a set (server enforces the same rule).
  const candidates = templates.filter(
    (template) => template.stage === stage && (template.isActive || selected.includes(template.id))
  );
  const selectedRows = selected.map((id) => byId.get(id)).filter((x) => x !== undefined);

  const changeStage = (value: OnboardingStage) => {
    setStage(value);
    setSelected([]);
  };
  const toggle = (id: string, checked: boolean) =>
    setSelected((current) => (checked ? [...current, id] : current.filter((x) => x !== id)));

  const mutation = useMutation({
    mutationFn: () =>
      set
        ? updateOnboardingTemplateSet(set.id, {
            name: name.trim(),
            templateIds: selected,
            isActive: set.isActive,
          })
        : createOnboardingTemplateSet({ stage, name: name.trim(), templateIds: selected }),
    onSuccess: () => {
      onSaved();
      onClose();
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("onboarding:actionFailed")),
      }),
  });

  return (
    <Modal
      opened
      onClose={onClose}
      centered
      size="lg"
      title={t(set ? "onboarding:sets.editTitle" : "onboarding:sets.addTitle")}
    >
      <Stack gap="md">
        <TextInput
          label={t("onboarding:sets.name")}
          value={name}
          maxLength={NAME_MAX_LENGTH}
          required
          onChange={(e) => setName(e.currentTarget.value)}
        />
        {!set && (
          <SegmentedControl
            value={stage}
            onChange={(value) => changeStage(value as OnboardingStage)}
            data={ONBOARDING_STAGES.map((value) => ({
              value,
              label: t(`onboarding:stages.${value}`),
            }))}
          />
        )}
        <Stack gap={4}>
          <Text size="sm" fw={500}>
            {t("onboarding:sets.pick")}
          </Text>
          {candidates.length === 0 && (
            <Text size="sm" c="dimmed">
              {t("onboarding:sets.noTemplates")}
            </Text>
          )}
          {candidates.map((template) => (
            <Checkbox
              key={template.id}
              label={template.title}
              checked={selected.includes(template.id)}
              onChange={(e) => toggle(template.id, e.currentTarget.checked)}
            />
          ))}
        </Stack>
        {selectedRows.length > 0 && (
          <Stack gap={4}>
            <Text size="sm" fw={500}>
              {t("onboarding:sets.order")}
            </Text>
            {selectedRows.map((template, index) => (
              <Group key={template.id} gap="xs" wrap="nowrap">
                <Text size="sm" w={24}>
                  {index + 1}.
                </Text>
                <Text size="sm" style={{ flex: 1 }}>
                  {template.title}
                </Text>
                <ActionIcon
                  variant="subtle"
                  aria-label={t("onboarding:sets.moveUp", { title: template.title })}
                  disabled={index === 0}
                  onClick={() => setSelected((current) => moveItem(current, index, -1))}
                >
                  <ArrowUp size={14} />
                </ActionIcon>
                <ActionIcon
                  variant="subtle"
                  aria-label={t("onboarding:sets.moveDown", { title: template.title })}
                  disabled={index === selectedRows.length - 1}
                  onClick={() => setSelected((current) => moveItem(current, index, 1))}
                >
                  <ArrowDown size={14} />
                </ActionIcon>
              </Group>
            ))}
          </Stack>
        )}
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={onClose} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button
            onClick={() => mutation.mutate()}
            disabled={!name.trim() || selected.length === 0}
            loading={mutation.isPending}
          >
            {t("common:save")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
