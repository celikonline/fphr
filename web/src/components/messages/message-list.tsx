import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Badge, Button, Group, Modal, Skeleton, Stack, Table, Text } from "@mantine/core";
import type { HrMessage, HrMessagesPage } from "@/services/hr-messages.service";
import { HR_MESSAGES_KEY } from "@/services/hr-messages.service";

const PAGE_SIZE = 20;

export function ImportanceChip({ importance }: { importance: HrMessage["importance"] }) {
  const { t } = useTranslation("messages");
  return (
    <Badge color={importance === "high" ? "red" : "gray"} variant="light">
      {t(`importance.${importance}`)}
    </Badge>
  );
}

function ChannelBadges({ channels }: { channels: HrMessage["channels"] }) {
  const { t } = useTranslation("messages");
  return (
    <Group gap={4}>
      {channels.map((c) => (
        <Badge key={c} variant="outline" size="sm">
          {t(`channels.${c}`)}
        </Badge>
      ))}
    </Group>
  );
}

interface MessageListProps {
  scope: "inbox" | "outbox";
  fetchPage: (page: number, pageSize: number) => Promise<HrMessagesPage>;
}

/** Paged inbox/outbox table with a detail dialog. Recipients are summarised by count (the API returns no names). */
export default function MessageList({ scope, fetchPage }: MessageListProps) {
  const { t, i18n } = useTranslation("messages");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<HrMessage | null>(null);
  const query = useQuery({
    queryKey: [...HR_MESSAGES_KEY, scope, page],
    queryFn: () => fetchPage(page, PAGE_SIZE),
    placeholderData: (previous) => previous,
  });

  if (query.isLoading) return <Skeleton height={160} />;
  if (query.isError) return <Text c="red">{t("list.loadError")}</Text>;
  const data = query.data;
  const items = data?.items ?? [];
  if (items.length === 0)
    return <Text c="dimmed">{t(scope === "inbox" ? "list.emptyInbox" : "list.emptyOutbox")}</Text>;

  const totalPages = Math.max(1, Math.ceil((data?.totalCount ?? 0) / PAGE_SIZE));
  const fmt = (iso: string) => new Date(iso).toLocaleString(i18n.language);

  return (
    <Stack gap="sm">
      <Table highlightOnHover>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>{t("list.subject")}</Table.Th>
            <Table.Th>{t("list.importance")}</Table.Th>
            <Table.Th>{t("list.channelsCol")}</Table.Th>
            <Table.Th>{t("list.recipientsCol")}</Table.Th>
            <Table.Th>{t("list.date")}</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {items.map((m) => (
            <Table.Tr key={m.id} style={{ cursor: "pointer" }} onClick={() => setSelected(m)}>
              <Table.Td>{m.subject}</Table.Td>
              <Table.Td>
                <ImportanceChip importance={m.importance} />
              </Table.Td>
              <Table.Td>
                <ChannelBadges channels={m.channels} />
              </Table.Td>
              <Table.Td>{t("list.recipients", { count: m.recipientCount })}</Table.Td>
              <Table.Td>{fmt(m.sentAt)}</Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
      {totalPages > 1 && (
        <Group justify="flex-end">
          <Button
            variant="default"
            size="xs"
            disabled={page <= 1}
            onClick={() => setPage((p) => p - 1)}
          >
            {t("list.prev")}
          </Button>
          <Text size="sm">
            {page} / {totalPages}
          </Text>
          <Button
            variant="default"
            size="xs"
            disabled={page >= totalPages}
            onClick={() => setPage((p) => p + 1)}
          >
            {t("list.next")}
          </Button>
        </Group>
      )}
      <Modal
        opened={selected !== null}
        onClose={() => setSelected(null)}
        title={selected?.subject ?? t("list.detail")}
      >
        {selected && (
          <Stack gap="sm">
            <Group gap="xs">
              <ImportanceChip importance={selected.importance} />
              <ChannelBadges channels={selected.channels} />
            </Group>
            <Text size="xs" c="dimmed">
              {fmt(selected.sentAt)} · {t("list.recipients", { count: selected.recipientCount })}
            </Text>
            <Text style={{ whiteSpace: "pre-wrap" }}>{selected.body}</Text>
          </Stack>
        )}
      </Modal>
    </Stack>
  );
}
