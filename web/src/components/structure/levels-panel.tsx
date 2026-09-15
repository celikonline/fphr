import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ArrowDown, ArrowUp, Layers, Lock, MoreHorizontal, Pencil, Plus, Trash2 } from "lucide-react";
import {
  ActionIcon,
  Button,
  Card,
  Divider,
  Group,
  Menu,
  Modal,
  NavLink,
  Radio,
  SimpleGrid,
  Stack,
  Text,
  TextInput,
  ThemeIcon,
  Tooltip,
} from "@mantine/core";
import ConfirmDialog from "@/components/confirm-dialog";
import { toast } from "@/hooks/use-toast";
import { structureErrorMessage } from "@/components/structure/errors";
import {
  COMPANY_TYPE_CODE,
  activateUnitType,
  createUnitType,
  deleteUnitType,
  renameUnitType,
  reorderUnitTypes,
  type OrgUnitType,
} from "@/services/structure.service";

export const UNIT_TYPES_QUERY_KEY = ["organization", "unit-types"] as const;

interface LevelsPanelProps {
  /** All levels (active and inactive). */
  types: OrgUnitType[];
  selectedTypeId: string | null;
  onSelect: (typeId: string) => void;
  canManage: boolean;
  unitCounts: Record<string, number>;
}

type AddState = { insertAfter: OrgUnitType } | null;

export default function LevelsPanel({ types, selectedTypeId, onSelect, canManage, unitCounts }: LevelsPanelProps) {
  const { t } = useTranslation(["structure", "common"]);
  const queryClient = useQueryClient();

  const active = types.filter((x) => x.isActive).sort((a, b) => a.sortOrder - b.sortOrder);
  const inactive = types.filter((x) => !x.isActive).sort((a, b) => a.sortOrder - b.sortOrder);

  const [addState, setAddState] = useState<AddState>(null);
  const [renameTarget, setRenameTarget] = useState<OrgUnitType | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<OrgUnitType | null>(null);

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: UNIT_TYPES_QUERY_KEY });
    queryClient.invalidateQueries({ queryKey: ["organization", "units"] });
  };
  const onError = (error: unknown, statusKeys?: Partial<Record<number, string>>) =>
    toast({ variant: "destructive", title: t("common:error"), description: structureErrorMessage(error, t, undefined, statusKeys) });

  const reorderMutation = useMutation({
    mutationFn: reorderUnitTypes,
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("structure:levels.reorderSuccess") });
    },
    onError: (error) => onError(error),
  });

  const deleteMutation = useMutation({
    mutationFn: deleteUnitType,
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("structure:levels.deleteSuccess") });
      setDeleteTarget(null);
    },
    onError: (error) => {
      onError(error, { 409: "structure:errors.levelInUse", 422: "structure:errors.levelSystemImmutable" });
      setDeleteTarget(null);
    },
  });

  function move(level: OrgUnitType, direction: -1 | 1) {
    const ids = active.map((x) => x.id);
    const index = ids.indexOf(level.id);
    const target = index + direction;
    if (index < 0 || target < 0 || target >= ids.length) return;
    [ids[index], ids[target]] = [ids[target], ids[index]];
    reorderMutation.mutate(ids);
  }

  const isCompany = (level: OrgUnitType) => level.code === COMPANY_TYPE_CODE;

  return (
    <Card withBorder h="fit-content">
      <Group gap="xs">
        <Layers size={16} />
        <Text fw={600}>{t("structure:levels.title")}</Text>
      </Group>
      <Text size="xs" c="dimmed" mb="sm">
        {t("structure:levels.subtitle")}
      </Text>
      <Stack component="ol" gap={0}>
        {active.map((level, index) => {
          const selected = level.id === selectedTypeId;
          const locked = isCompany(level);
          // Company is pinned first: it can't move and nothing can move above it.
          const canMoveUp = !locked && index > 1;
          const canMoveDown = !locked && index < active.length - 1;
          return (
            <li key={level.id}>
              <Group gap={4} wrap="nowrap">
                <NavLink
                  component="button"
                  type="button"
                  active={selected}
                  onClick={() => onSelect(level.id)}
                  style={{ flex: 1, minWidth: 0, borderRadius: "var(--mantine-radius-md)" }}
                  leftSection={
                    <ThemeIcon size={24} radius="xl" variant="light" color="gray">
                      <Text size="xs" fw={500}>
                        {index + 1}
                      </Text>
                    </ThemeIcon>
                  }
                  label={
                    <Group gap={6} wrap="nowrap">
                      <Text size="sm" fw={selected ? 600 : 400} truncate="end">
                        {level.name}
                      </Text>
                      {locked && (
                        <Lock
                          size={12}
                          style={{ flexShrink: 0, color: "var(--mantine-color-dimmed)" }}
                          aria-label={t("structure:levels.system")}
                        />
                      )}
                    </Group>
                  }
                  rightSection={
                    <Text size="xs" c="dimmed">
                      {unitCounts[level.id] ?? 0}
                    </Text>
                  }
                />
                {canManage && (
                  <Menu position="bottom-end">
                    <Tooltip label={t("common:actions")}>
                      <Menu.Target>
                        <ActionIcon variant="subtle" color="gray" aria-label={t("common:actions")}>
                          <MoreHorizontal size={16} />
                        </ActionIcon>
                      </Menu.Target>
                    </Tooltip>
                    <Menu.Dropdown>
                      <Menu.Item leftSection={<Pencil size={16} />} onClick={() => setRenameTarget(level)}>
                        {t("structure:levels.rename")}
                      </Menu.Item>
                      <Menu.Item
                        leftSection={<ArrowUp size={16} />}
                        disabled={!canMoveUp || reorderMutation.isPending}
                        onClick={() => move(level, -1)}
                      >
                        {t("structure:levels.moveUp")}
                      </Menu.Item>
                      <Menu.Item
                        leftSection={<ArrowDown size={16} />}
                        disabled={!canMoveDown || reorderMutation.isPending}
                        onClick={() => move(level, 1)}
                      >
                        {t("structure:levels.moveDown")}
                      </Menu.Item>
                      <Menu.Divider />
                      <Menu.Item
                        color="red"
                        leftSection={<Trash2 size={16} />}
                        disabled={locked}
                        onClick={() => setDeleteTarget(level)}
                      >
                        {t("common:delete")}
                      </Menu.Item>
                    </Menu.Dropdown>
                  </Menu>
                )}
              </Group>
              {canManage && (
                <Divider
                  ml="lg"
                  my={2}
                  labelPosition="center"
                  label={
                    <Tooltip label={t("structure:levels.addAfter", { name: level.name })}>
                      <ActionIcon
                        variant="default"
                        radius="xl"
                        size={24}
                        aria-label={t("structure:levels.addAfter", { name: level.name })}
                        onClick={() => setAddState({ insertAfter: level })}
                      >
                        <Plus size={12} />
                      </ActionIcon>
                    </Tooltip>
                  }
                />
              )}
            </li>
          );
        })}
      </Stack>

      {addState && (
        <AddLevelDialog
          insertAfter={addState.insertAfter}
          inactive={inactive}
          onClose={() => setAddState(null)}
          onDone={(created) => {
            invalidate();
            onSelect(created.id);
            setAddState(null);
          }}
        />
      )}

      {renameTarget && (
        <RenameLevelDialog level={renameTarget} onClose={() => setRenameTarget(null)} onDone={invalidate} />
      )}

      <ConfirmDialog
        open={deleteTarget !== null}
        onOpenChange={(open) => !open && setDeleteTarget(null)}
        title={t("structure:levels.deleteTitle")}
        description={t("structure:levels.deleteConfirm", { name: deleteTarget?.name ?? "" })}
        onConfirm={() => deleteTarget && deleteMutation.mutate(deleteTarget.id)}
        isLoading={deleteMutation.isPending}
      />
    </Card>
  );
}

function AddLevelDialog({
  insertAfter,
  inactive,
  onClose,
  onDone,
}: {
  insertAfter: OrgUnitType;
  inactive: OrgUnitType[];
  onClose: () => void;
  onDone: (created: OrgUnitType) => void;
}) {
  const { t } = useTranslation(["structure", "common"]);
  const [mode, setMode] = useState<"new" | "activate">("new");
  const [name, setName] = useState("");
  const [activateId, setActivateId] = useState<string>(inactive[0]?.id ?? "");

  const mutation = useMutation({
    mutationFn: () =>
      mode === "new"
        ? createUnitType({ name: name.trim(), insertAfterTypeId: insertAfter.id })
        : activateUnitType(activateId, insertAfter.id),
    onSuccess: (created) => {
      toast({ variant: "success", title: t("common:success"), description: t("structure:levels.addSuccess") });
      onDone(created);
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: structureErrorMessage(error, t) }),
  });

  const valid = mode === "new" ? name.trim().length > 0 : activateId !== "";

  return (
    <Modal opened onClose={onClose} title={t("structure:levels.addTitle")}>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (valid) mutation.mutate();
        }}
      >
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("structure:levels.addDescription", { name: insertAfter.name })}
          </Text>

          {inactive.length > 0 && (
            <Radio.Group value={mode} onChange={(v) => setMode(v as "new" | "activate")}>
              <Stack gap="xs">
                <Radio id="level-mode-activate" value="activate" label={t("structure:levels.modeActivate")} />
                <Radio id="level-mode-new" value="new" label={t("structure:levels.modeNew")} />
              </Stack>
            </Radio.Group>
          )}

          {mode === "activate" ? (
            <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="xs">
              {inactive.map((level) => {
                const picked = activateId === level.id;
                return (
                  <Button
                    key={level.id}
                    type="button"
                    variant={picked ? "light" : "default"}
                    justify="flex-start"
                    fw={picked ? 500 : 400}
                    aria-pressed={picked}
                    onClick={() => setActivateId(level.id)}
                  >
                    {level.name}
                  </Button>
                );
              })}
            </SimpleGrid>
          ) : (
            <TextInput
              id="level-name"
              data-autofocus
              maxLength={200}
              label={t("structure:levels.name")}
              placeholder={t("structure:levels.namePlaceholder")}
              value={name}
              onChange={(e) => setName(e.currentTarget.value)}
            />
          )}

          <Group justify="flex-end" gap="sm">
            <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={!valid || mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}

function RenameLevelDialog({ level, onClose, onDone }: { level: OrgUnitType; onClose: () => void; onDone: () => void }) {
  const { t } = useTranslation(["structure", "common"]);
  const [name, setName] = useState(level.name);

  const mutation = useMutation({
    mutationFn: () => renameUnitType(level.id, name.trim()),
    onSuccess: () => {
      toast({ variant: "success", title: t("common:success"), description: t("structure:levels.renameSuccess") });
      onDone();
      onClose();
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: structureErrorMessage(error, t) }),
  });

  return (
    <Modal opened onClose={onClose} title={t("structure:levels.renameTitle")}>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (name.trim()) mutation.mutate();
        }}
      >
        <Stack gap="md">
          <TextInput
            id="level-rename"
            data-autofocus
            maxLength={200}
            label={t("structure:levels.name")}
            value={name}
            onChange={(e) => setName(e.currentTarget.value)}
          />
          <Group justify="flex-end" gap="sm">
            <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={!name.trim() || mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
