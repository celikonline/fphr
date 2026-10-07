import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import DOMPurify from "dompurify";
import { Maximize2, Minimize2 } from "lucide-react";
import {
  ActionIcon,
  Badge,
  Button,
  Group,
  Modal,
  Skeleton,
  Stack,
  Table,
  Text,
  UnstyledButton,
} from "@mantine/core";
import type { HrMessage, HrMessagesPage } from "@/services/hr-messages.service";
import { HR_MESSAGES_KEY } from "@/services/hr-messages.service";

const PAGE_SIZE = 20;

const RICH_BODY_PATTERN = /^\s*<(?:p|br|strong|b|em|i|u|s|h[1-3]|ul|ol|li|blockquote|a|img)\b/i;

function MessageBody({ body }: { body: string }) {
  if (!RICH_BODY_PATTERN.test(body)) {
    return <Text style={{ whiteSpace: "pre-wrap", overflowWrap: "anywhere" }}>{body}</Text>;
  }

  const sanitized = DOMPurify.sanitize(body, {
    ALLOWED_TAGS: ["p", "br", "strong", "b", "em", "i", "u", "s", "blockquote", "h1", "h2", "h3", "ul", "ol", "li", "a", "img"],
    ALLOWED_ATTR: ["href", "src", "alt", "title", "target", "rel"],
    ALLOW_DATA_ATTR: false,
    ALLOWED_URI_REGEXP: /^(?:(?:https?|mailto):|data:image\/(?:png|gif|jpeg|webp);)/i,
  });
  return <div style={{ overflowWrap: "anywhere" }} dangerouslySetInnerHTML={{ __html: sanitized }} />;
}

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
  // Full screen is a per-opening choice: closing the dialog returns to the normal size.
  const [fullScreen, setFullScreen] = useState(false);
  const closeDetail = () => {
    setSelected(null);
    setFullScreen(false);
  };
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
            <Table.Tr key={m.id}>
              <Table.Td>
                <UnstyledButton
                  aria-label={t("list.open", { subject: m.subject })}
                  onClick={() => setSelected(m)}
                  style={{ textAlign: "left", fontWeight: 500 }}
                >
                  {m.subject}
                </UnstyledButton>
              </Table.Td>
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
      <Modal.Root opened={selected !== null} onClose={closeDetail} fullScreen={fullScreen}>
        <Modal.Overlay />
        <Modal.Content>
          <Modal.Header>
            <Modal.Title style={{ overflowWrap: "anywhere" }}>
              {selected?.subject ?? t("list.detail")}
            </Modal.Title>
            <ActionIcon
              variant="subtle"
              color="gray"
              size="lg"
              aria-label={t(fullScreen ? "list.exitFullScreen" : "list.enterFullScreen")}
              title={t(fullScreen ? "list.exitFullScreen" : "list.enterFullScreen")}
              onClick={() => setFullScreen((value) => !value)}
            >
              {fullScreen ? <Minimize2 size={18} /> : <Maximize2 size={18} />}
            </ActionIcon>
            <Modal.CloseButton aria-label={t("list.close")} />
          </Modal.Header>
          <Modal.Body>
            {selected && (
              <Stack gap="sm">
                <Group gap="xs">
                  <ImportanceChip importance={selected.importance} />
                  <ChannelBadges channels={selected.channels} />
                </Group>
                <Text size="xs" c="dimmed">
                  {fmt(selected.sentAt)} · {t("list.recipients", { count: selected.recipientCount })}
                </Text>
                <MessageBody body={selected.body} />
              </Stack>
            )}
          </Modal.Body>
        </Modal.Content>
      </Modal.Root>
    </Stack>
  );
}
