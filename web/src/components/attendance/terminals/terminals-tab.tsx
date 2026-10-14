import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Badge, Button, Card, Group, Modal, Skeleton, Table, Text } from "@mantine/core";
import { KeyRound, LifeBuoy, Pencil, Plus } from "lucide-react";
import AttendanceEmptyState from "@/components/attendance/attendance-empty-state";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import ApiKeyDialog from "@/components/attendance/terminals/api-key-dialog";
import TerminalForm from "@/components/attendance/terminals/terminal-form";
import {
  createTerminal,
  listTerminalGroups,
  listTerminals,
  rotateTerminalApiKey,
  setTerminalActive,
  updateTerminal,
  type Terminal,
  type TerminalInput,
} from "@/services/attendance-terminals.service";

const TERMINALS_KEY = ["attendance", "terminals"];
const GROUPS_KEY = ["attendance", "terminal-groups"];

/** Terminal listesi + oluşturma/düzenleme + API anahtarı yenileme + aktif/pasif. */
export default function TerminalsTab({ canManage }: { canManage: boolean }) {
  const { t, i18n } = useTranslation(["terminals", "common"]);
  const queryClient = useQueryClient();
  // undefined = form kapalı, null = yeni terminal, Terminal = düzenleme.
  const [editing, setEditing] = useState<Terminal | null | undefined>(undefined);
  const [rotating, setRotating] = useState<Terminal | null>(null);
  // Ham anahtar yalnız bu state'te; diyalog kapanınca null'lanır.
  const [revealedKey, setRevealedKey] = useState<string | null>(null);

  const terminals = useQuery({ queryKey: TERMINALS_KEY, queryFn: listTerminals });
  const groups = useQuery({ queryKey: GROUPS_KEY, queryFn: listTerminalGroups });
  const groupName = (id?: string | null) =>
    groups.data?.find((g) => g.id === id)?.name ?? t("terminals:terminal.noGroup");
  const refresh = () => queryClient.invalidateQueries({ queryKey: TERMINALS_KEY });
  const fail = (fallback: string) => (e: unknown) =>
    toast({ variant: "destructive", description: getApiErrorMessage(e, fallback) });

  const save = useMutation({
    mutationFn: async (input: TerminalInput) => {
      if (editing) {
        await updateTerminal(editing.id, input);
        return null;
      }
      return createTerminal(input);
    },
    // gcTime 0 + reset(): the raw key must not stay in the mutation cache after it is handed to the dialog.
    gcTime: 0,
    onSuccess: async (created) => {
      toast({ variant: "success", description: t("terminals:terminal.saved") });
      setEditing(undefined);
      if (created) setRevealedKey(created.apiKey);
      queueMicrotask(() => save.reset());
      await refresh();
    },
    onError: fail(t("terminals:terminal.saveFailed")),
  });

  const rotate = useMutation({
    mutationFn: (id: string) => rotateTerminalApiKey(id),
    gcTime: 0,
    onSuccess: (result) => {
      setRotating(null);
      setRevealedKey(result.apiKey);
      queueMicrotask(() => rotate.reset());
    },
    onError: fail(t("terminals:apiKey.rotateFailed")),
  });

  const toggle = useMutation({
    mutationFn: (terminal: Terminal) => setTerminalActive(terminal.id, !terminal.isActive),
    onSuccess: refresh,
    onError: fail(t("terminals:terminal.statusFailed")),
  });

  const formatSeen = (iso?: string | null) =>
    iso ? new Date(iso).toLocaleString(i18n.language) : t("terminals:terminal.never");

  if (terminals.isPending) return <Skeleton h={120} />;
  if (terminals.isError) return <Alert color="red">{t("terminals:terminal.loadFailed")}</Alert>;

  return (
    <div className="space-y-4">
      {canManage && terminals.data.length > 0 && (
        <Group justify="flex-end">
          <Button leftSection={<Plus size={16} />} onClick={() => setEditing(null)}>
            {t("terminals:terminal.new")}
          </Button>
        </Group>
      )}
      {terminals.data.length === 0 ? (
        <Card withBorder padding="xl">
          <AttendanceEmptyState
            title={t("terminals:terminal.empty")}
            description={t("terminals:terminal.emptyGuide")}
            action={canManage ? (
              <Button leftSection={<Plus size={16} />} onClick={() => setEditing(null)}>{t("terminals:terminal.new")}</Button>
            ) : (
              <Button component={Link} to="/app/support" variant="light" leftSection={<LifeBuoy size={16} />}>
                {t("terminals:contactSupport")}
              </Button>
            )}
          />
        </Card>
      ) : (
        <Table.ScrollContainer minWidth={720}>
          <Table verticalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("terminals:terminal.name")}</Table.Th>
                <Table.Th>{t("terminals:terminal.group")}</Table.Th>
                <Table.Th>{t("terminals:terminal.direction")}</Table.Th>
                <Table.Th>{t("terminals:terminal.status")}</Table.Th>
                <Table.Th>{t("terminals:terminal.lastSeen")}</Table.Th>
                {canManage && <Table.Th>{t("common:actions")}</Table.Th>}
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {terminals.data.map((row) => (
                <Table.Tr key={row.id}>
                  <Table.Td>
                    <Text fw={500}>{row.name}</Text>
                    <Text size="xs" c="dimmed">
                      {row.code}
                    </Text>
                  </Table.Td>
                  <Table.Td>{groupName(row.terminalGroupId)}</Table.Td>
                  <Table.Td>{t(`terminals:directionMode.${row.directionMode}`)}</Table.Td>
                  <Table.Td>
                    <Group gap={6}>
                      <Badge color={row.isOnline ? "green" : "gray"} variant="light">
                        {row.isOnline ? t("terminals:presence.online") : t("terminals:presence.offline")}
                      </Badge>
                      {row.lastSuspiciousAtUtc && (
                        <Badge color="orange" variant="light">
                          {t("terminals:terminal.suspicious", {
                            date: new Date(row.lastSuspiciousAtUtc).toLocaleString(i18n.language),
                          })}
                        </Badge>
                      )}
                      {!row.isActive && (
                        <Badge color="red" variant="outline">
                          {t("terminals:terminal.inactive")}
                        </Badge>
                      )}
                    </Group>
                  </Table.Td>
                  <Table.Td>{formatSeen(row.lastSeenAtUtc)}</Table.Td>
                  {canManage && (
                    <Table.Td>
                      <Group gap="xs" wrap="nowrap">
                        <Button
                          size="compact-sm"
                          variant="default"
                          leftSection={<Pencil size={14} />}
                          onClick={() => setEditing(row)}
                        >
                          {t("terminals:terminal.edit")}
                        </Button>
                        <Button
                          size="compact-sm"
                          variant="default"
                          leftSection={<KeyRound size={14} />}
                          onClick={() => setRotating(row)}
                        >
                          {t("terminals:apiKey.generate")}
                        </Button>
                        <Button
                          size="compact-sm"
                          variant="default"
                          loading={toggle.isPending && toggle.variables?.id === row.id}
                          onClick={() => toggle.mutate(row)}
                        >
                          {row.isActive ? t("terminals:terminal.deactivate") : t("terminals:terminal.activate")}
                        </Button>
                      </Group>
                    </Table.Td>
                  )}
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}

      {editing !== undefined && (
        <TerminalForm
          terminal={editing}
          groups={groups.data ?? []}
          saving={save.isPending}
          onSubmit={(input) => save.mutate(input)}
          onClose={() => setEditing(undefined)}
        />
      )}
      <Modal opened={rotating !== null} onClose={() => setRotating(null)} title={t("terminals:apiKey.generate")}>
        <Text size="sm">{t("terminals:apiKey.rotateConfirm")}</Text>
        <Group justify="flex-end" mt="md">
          <Button variant="default" onClick={() => setRotating(null)}>
            {t("common:cancel")}
          </Button>
          <Button color="red" loading={rotate.isPending} onClick={() => rotating && rotate.mutate(rotating.id)}>
            {t("terminals:apiKey.rotateAction")}
          </Button>
        </Group>
      </Modal>
      <ApiKeyDialog apiKey={revealedKey} onClose={() => setRevealedKey(null)} />
    </div>
  );
}
