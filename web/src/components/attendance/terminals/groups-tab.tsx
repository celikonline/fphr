import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Card, Group, Skeleton, Table, Text, TextInput } from "@mantine/core";
import { LifeBuoy, Plus } from "lucide-react";
import AttendanceEmptyState from "@/components/attendance/attendance-empty-state";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  createTerminalGroup,
  deleteTerminalGroup,
  listTerminalGroups,
  renameTerminalGroup,
} from "@/services/attendance-terminals.service";

const GROUPS_KEY = ["attendance", "terminal-groups"];

/** Terminal grubu yönetimi: ekle, yeniden adlandır (satır içi), sil (onaylı). */
export default function TerminalGroupsTab({ canManage }: { canManage: boolean }) {
  const { t } = useTranslation(["terminals", "common"]);
  const queryClient = useQueryClient();
  const [newName, setNewName] = useState("");
  const [editId, setEditId] = useState<string | null>(null);
  const [editName, setEditName] = useState("");
  const [deleteId, setDeleteId] = useState<string | null>(null);
  const newGroupInput = useRef<HTMLInputElement>(null);

  const groups = useQuery({ queryKey: GROUPS_KEY, queryFn: listTerminalGroups });
  const refresh = () => queryClient.invalidateQueries({ queryKey: GROUPS_KEY });
  const fail = (fallback: string) => (e: unknown) =>
    toast({ variant: "destructive", description: getApiErrorMessage(e, fallback) });

  const create = useMutation({
    mutationFn: () => createTerminalGroup(newName.trim()),
    onSuccess: async () => {
      setNewName("");
      toast({ variant: "success", description: t("terminals:group.saved") });
      await refresh();
    },
    onError: fail(t("terminals:group.saveFailed")),
  });
  const rename = useMutation({
    mutationFn: (id: string) => renameTerminalGroup(id, editName.trim()),
    onSuccess: async () => {
      setEditId(null);
      toast({ variant: "success", description: t("terminals:group.saved") });
      await refresh();
    },
    onError: fail(t("terminals:group.saveFailed")),
  });
  const remove = useMutation({
    mutationFn: (id: string) => deleteTerminalGroup(id),
    onSuccess: async () => {
      setDeleteId(null);
      toast({ variant: "success", description: t("terminals:group.deleted") });
      await refresh();
    },
    onError: fail(t("terminals:group.deleteFailed")),
  });

  if (groups.isPending) return <Skeleton h={120} />;
  if (groups.isError) return <Alert color="red">{t("terminals:group.loadFailed")}</Alert>;

  return (
    <div className="space-y-4">
      {canManage && (
        <Group align="flex-end">
          <TextInput
            ref={newGroupInput}
            label={t("terminals:group.name")}
            value={newName}
            maxLength={100}
            onChange={(e) => setNewName(e.currentTarget.value)}
          />
          <Button
            leftSection={<Plus size={16} />}
            disabled={!newName.trim()}
            loading={create.isPending}
            onClick={() => create.mutate()}
          >
            {t("terminals:group.new")}
          </Button>
        </Group>
      )}
      {groups.data.length === 0 ? (
        <Card withBorder padding="xl">
          <AttendanceEmptyState
            title={t("terminals:group.empty")}
            description={t("terminals:group.emptyGuide")}
            action={canManage ? (
              <Button variant="light" onClick={() => newGroupInput.current?.focus()}>{t("terminals:group.focusCreate")}</Button>
            ) : (
              <Button component={Link} to="/app/support" variant="light" leftSection={<LifeBuoy size={16} />}>
                {t("terminals:contactSupport")}
              </Button>
            )}
          />
        </Card>
      ) : (
        <Table verticalSpacing="sm">
          <Table.Tbody>
            {groups.data.map((g) => (
              <Table.Tr key={g.id}>
                <Table.Td>
                  {editId === g.id ? (
                    <TextInput
                      aria-label={t("terminals:group.name")}
                      data-editing
                      value={editName}
                      maxLength={100}
                      onChange={(e) => setEditName(e.currentTarget.value)}
                    />
                  ) : (
                    g.name
                  )}
                </Table.Td>
                {canManage && (
                  <Table.Td>
                    <Group gap="xs" justify="flex-end" wrap="nowrap">
                      {editId === g.id ? (
                        <>
                          <Button
                            size="compact-sm"
                            disabled={!editName.trim()}
                            loading={rename.isPending}
                            onClick={() => rename.mutate(g.id)}
                          >
                            {t("common:save")}
                          </Button>
                          <Button size="compact-sm" variant="default" onClick={() => setEditId(null)}>
                            {t("common:cancel")}
                          </Button>
                        </>
                      ) : deleteId === g.id ? (
                        <>
                          <Text size="sm">{t("terminals:group.deleteConfirm")}</Text>
                          <Button
                            size="compact-sm"
                            color="red"
                            loading={remove.isPending}
                            onClick={() => remove.mutate(g.id)}
                          >
                            {t("terminals:group.deleteYes")}
                          </Button>
                          <Button size="compact-sm" variant="default" onClick={() => setDeleteId(null)}>
                            {t("common:cancel")}
                          </Button>
                        </>
                      ) : (
                        <>
                          <Button
                            size="compact-sm"
                            variant="default"
                            onClick={() => {
                              setEditId(g.id);
                              setEditName(g.name);
                            }}
                          >
                            {t("terminals:group.rename")}
                          </Button>
                          <Button size="compact-sm" variant="default" color="red" onClick={() => setDeleteId(g.id)}>
                            {t("terminals:group.delete")}
                          </Button>
                        </>
                      )}
                    </Group>
                  </Table.Td>
                )}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}
    </div>
  );
}
