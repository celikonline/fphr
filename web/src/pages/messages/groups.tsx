import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Button,
  Group,
  Modal,
  Skeleton,
  Stack,
  Table,
  Text,
  TextInput,
  Tooltip,
} from "@mantine/core";
import { Pencil, Trash2 } from "lucide-react";
import { ConfirmModal } from "@/components/ui/confirm-modal";
import SelectionField from "@/components/messages/selection-field";
import { isSelectionEmpty } from "@/components/messages/selection-summary";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  MESSAGE_GROUPS_KEY,
  createMessageGroup,
  deleteMessageGroup,
  listMessageGroups,
  updateMessageGroup,
  type MessageGroup,
} from "@/services/hr-messages.service";
import type { EmployeeSelection } from "@/types";

function MemberCount({ group }: { group: MessageGroup }) {
  const { t } = useTranslation("messages");
  const s = group.selection;
  if (s?.filter) {
    return (
      <Tooltip label={t("groups.dynamicHint")} multiline w={260}>
        <Text span size="sm">
          {t("groups.dynamic")}
        </Text>
      </Tooltip>
    );
  }
  return <>{s?.employeeIds?.length ?? 0}</>;
}

export default function GroupsTab() {
  const { t } = useTranslation("messages");
  const queryClient = useQueryClient();
  const [deleting, setDeleting] = useState<MessageGroup | null>(null);
  const [editing, setEditing] = useState<MessageGroup | "new" | null>(null);
  const groups = useQuery({ queryKey: MESSAGE_GROUPS_KEY, queryFn: listMessageGroups });

  const remove = useMutation({
    mutationFn: deleteMessageGroup,
    onSuccess: () => {
      toast({ title: t("groups.deleted") });
      void queryClient.invalidateQueries({ queryKey: MESSAGE_GROUPS_KEY });
    },
    onError: (error) =>
      toast({ title: getApiErrorMessage(error, t("groups.deleteError")), variant: "destructive" }),
  });

  if (groups.isLoading) return <Skeleton height={120} />;
  if (groups.isError) return <Text c="red">{t("groups.loadError")}</Text>;
  const items = groups.data ?? [];

  return (
    <Stack gap="sm">
      <Group justify="flex-end">
        <Button onClick={() => setEditing("new")}>{t("groups.new")}</Button>
      </Group>
      {items.length === 0 ? (
        <Text c="dimmed">{t("groups.empty")}</Text>
      ) : (
        <Table>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>{t("groups.name")}</Table.Th>
              <Table.Th>{t("groups.members")}</Table.Th>
              <Table.Th />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {items.map((g) => (
              <Table.Tr key={g.id}>
                <Table.Td>{g.name}</Table.Td>
                <Table.Td>
                  <MemberCount group={g} />
                </Table.Td>
                <Table.Td>
                  <Group gap="xs" justify="flex-end">
                    <ActionIcon
                      variant="subtle"
                      aria-label={t("groups.edit")}
                      onClick={() => setEditing(g)}
                    >
                      <Pencil size={16} />
                    </ActionIcon>
                    <ActionIcon
                      variant="subtle"
                      color="red"
                      aria-label={t("groups.delete")}
                      onClick={() => setDeleting(g)}
                    >
                      <Trash2 size={16} />
                    </ActionIcon>
                  </Group>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}
      <ConfirmModal
        opened={deleting !== null}
        onClose={() => setDeleting(null)}
        title={t("groups.delete")}
        message={t("groups.confirmDelete", { name: deleting?.name ?? "" })}
        confirmLabel={t("groups.delete")}
        danger
        onConfirm={async () => {
          if (deleting) await remove.mutateAsync(deleting.id).catch(() => undefined);
        }}
      />
      {editing && (
        <GroupDialog
          key={editing === "new" ? "new" : editing.id}
          group={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
        />
      )}
    </Stack>
  );
}

function GroupDialog({ group, onClose }: { group: MessageGroup | null; onClose: () => void }) {
  const { t } = useTranslation("messages");
  const queryClient = useQueryClient();
  const [name, setName] = useState(group?.name ?? "");
  const [selection, setSelection] = useState<EmployeeSelection | null>(group?.selection ?? null);

  const save = useMutation({
    mutationFn: async () => {
      const input = { name: name.trim(), selection: selection as EmployeeSelection };
      if (group) await updateMessageGroup(group.id, input);
      else await createMessageGroup(input);
    },
    onSuccess: () => {
      toast({ title: t("groups.saved") });
      void queryClient.invalidateQueries({ queryKey: MESSAGE_GROUPS_KEY });
      onClose();
    },
    onError: (error) =>
      toast({ title: getApiErrorMessage(error, t("groups.saveError")), variant: "destructive" }),
  });

  const noMembers = isSelectionEmpty(selection);
  const canSave = name.trim().length > 0 && !noMembers && !save.isPending;

  return (
    <Modal
      opened
      onClose={onClose}
      title={group ? t("groups.editTitle") : t("groups.createTitle")}
      size="lg"
    >
      <Stack gap="sm">
        <TextInput
          label={t("groups.name")}
          value={name}
          maxLength={200}
          onChange={(e) => setName(e.currentTarget.value)}
          required
        />
        <Text size="sm" fw={500}>
          {t("groups.members")}
        </Text>
        <SelectionField value={selection} onChange={setSelection} />
        {noMembers && (
          <Text size="xs" c="dimmed">
            {t("groups.needMembers")}
          </Text>
        )}
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            {t("groups.cancel")}
          </Button>
          <Button disabled={!canSave} loading={save.isPending} onClick={() => save.mutate()}>
            {t("groups.save")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
