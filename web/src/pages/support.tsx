import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useLocation } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useDebouncedValue } from "@mantine/hooks";
import { LifeBuoy, Search, ArrowLeft } from "lucide-react";
import {
  Badge,
  Button,
  Card,
  Group,
  Loader,
  Select,
  Stack,
  Text,
  Textarea,
  TextInput,
  Title,
  type BadgeProps,
} from "@mantine/core";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateTime } from "@/lib/dates";
import { useAuthStore } from "@/store/auth.store";
import {
  SUPPORT_TICKET_CATEGORIES,
  createSupportTicket,
  addSupportTicketComment,
  getSupportTicket,
  getHelpArticle,
  listMySupportTickets,
  searchHelpArticles,
  type SupportTicketCategory,
  type SupportTicketStatus,
  type SupportTicket,
  type PortalTicketStatus,
} from "@/services/support.service";

type BadgeStyle = Pick<BadgeProps, "variant" | "color">;

const STATUS_BADGE: Record<SupportTicketStatus, BadgeStyle> = {
  open: { variant: "light", color: "gray" },
  answered: { variant: "filled", color: "green" },
  closed: { variant: "outline", color: "gray" },
};

const PORTAL_STATUS_BADGE: Record<PortalTicketStatus, BadgeStyle> = {
  new: { variant: "light", color: "gray" },
  triaged: { variant: "light", color: "blue" },
  in_progress: { variant: "filled", color: "blue" },
  waiting_requester: { variant: "light", color: "orange" },
  waiting_internal: { variant: "light", color: "yellow" },
  resolved: { variant: "filled", color: "green" },
  closed: { variant: "outline", color: "gray" },
  cancelled: { variant: "outline", color: "red" },
};

/** Ana sayfa: sol Yardım Merkezi araması + makale görünümü, sağ Desteğe bağlan formu + Taleplerim (11.3.13). */
export default function SupportPage() {
  const { t } = useTranslation(["support", "navigation", "common"]);

  return (
    <div className="max-w-5xl p-6 md:p-10">
      <Group gap="xs" mb="lg">
        <LifeBuoy size={28} className="text-orange-500" />
        <Title order={2}>{t("navigation:nav.support")}</Title>
      </Group>
      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <HelpCenter />
        <Stack gap="lg">
          <ContactSupportCard />
          <MyTicketsCard />
        </Stack>
      </div>
    </div>
  );
}

function HelpCenter() {
  const { t } = useTranslation("support");
  const location = useLocation();
  const [term, setTerm] = useState("");
  const [debouncedTerm] = useDebouncedValue(term, 300);
  const [selectedSlug, setSelectedSlug] = useState<string | null>(null);

  const searchQuery = useQuery({
    queryKey: ["support", "articles", debouncedTerm, location.pathname],
    queryFn: () => searchHelpArticles({ q: debouncedTerm || undefined, route: location.pathname }),
  });

  const articleQuery = useQuery({
    queryKey: ["support", "article", selectedSlug],
    queryFn: () => getHelpArticle(selectedSlug!),
    enabled: selectedSlug !== null,
  });

  return (
    <Card withBorder padding="lg">
      <Stack gap="md">
        <Title order={4}>{t("helpCenter.title")}</Title>
        {selectedSlug ? (
          <Stack gap="sm">
            <Button
              variant="subtle"
              size="xs"
              leftSection={<ArrowLeft size={14} />}
              onClick={() => setSelectedSlug(null)}
              className="self-start"
            >
              {t("helpCenter.backToList")}
            </Button>
            {articleQuery.isLoading ? (
              <Loader size="sm" />
            ) : articleQuery.data ? (
              <Stack gap="xs">
                <Title order={5}>{articleQuery.data.title}</Title>
                <Text size="sm" style={{ whiteSpace: "pre-wrap" }}>
                  {articleQuery.data.body}
                </Text>
              </Stack>
            ) : null}
          </Stack>
        ) : (
          <>
            <TextInput
              placeholder={t("helpCenter.searchPlaceholder")}
              leftSection={<Search size={16} />}
              value={term}
              onChange={(e) => setTerm(e.currentTarget.value)}
            />
            {searchQuery.isLoading ? (
              <Loader size="sm" />
            ) : searchQuery.data && searchQuery.data.length > 0 ? (
              <Stack gap={4} component="ul" className="list-none p-0">
                {searchQuery.data.map((article) => (
                  <Card
                    key={article.slug}
                    component="li"
                    withBorder
                    padding="sm"
                    className="cursor-pointer hover:bg-gray-50 dark:hover:bg-white/5"
                    onClick={() => setSelectedSlug(article.slug)}
                  >
                    <Group justify="space-between" wrap="nowrap">
                      <Text size="sm">{article.title}</Text>
                      <Badge variant="light" size="sm">
                        {t(`helpCenter.categories.${article.category}`, {
                          defaultValue: article.category,
                        })}
                      </Badge>
                    </Group>
                  </Card>
                ))}
              </Stack>
            ) : (
              <Text size="sm" c="dimmed">
                {t("helpCenter.empty")}
              </Text>
            )}
          </>
        )}
      </Stack>
    </Card>
  );
}

function ContactSupportCard() {
  const { t } = useTranslation("support");
  const location = useLocation();
  const queryClient = useQueryClient();
  const [subject, setSubject] = useState("");
  const [body, setBody] = useState("");
  const [category, setCategory] = useState<SupportTicketCategory>("general");

  const mutation = useMutation({
    mutationFn: () =>
      createSupportTicket({ subject, body, category, sourceRoute: location.pathname }),
    onSuccess: () => {
      toast({ variant: "success", description: t("contact.submitSuccess") });
      setSubject("");
      setBody("");
      setCategory("general");
      queryClient.invalidateQueries({ queryKey: ["support", "tickets", "mine"] });
    },
    onError: (error) => {
      toast({
        variant: "destructive",
        description: getApiErrorMessage(error, t("contact.submitError")),
      });
    },
  });

  return (
    <Card withBorder padding="lg">
      <Stack gap="sm">
        <Title order={4}>{t("contact.title")}</Title>
        <Text size="sm" c="dimmed">
          {t("contact.description")}
        </Text>
        <TextInput
          label={t("contact.subject")}
          placeholder={t("contact.subjectPlaceholder")}
          value={subject}
          onChange={(e) => setSubject(e.currentTarget.value)}
        />
        <Select
          label={t("contact.category")}
          data={SUPPORT_TICKET_CATEGORIES.map((c) => ({
            value: c,
            label: t(`contact.categories.${c}`),
          }))}
          value={category}
          onChange={(value) => setCategory((value as SupportTicketCategory) ?? "general")}
          allowDeselect={false}
        />
        <Textarea
          label={t("contact.body")}
          placeholder={t("contact.bodyPlaceholder")}
          minRows={4}
          autosize
          value={body}
          onChange={(e) => setBody(e.currentTarget.value)}
        />
        <Button
          onClick={() => mutation.mutate()}
          loading={mutation.isPending}
          disabled={subject.trim().length === 0 || body.trim().length === 0}
          className="self-start"
        >
          {t("contact.submit")}
        </Button>
      </Stack>
    </Card>
  );
}

function MyTicketsCard() {
  const { t } = useTranslation("support");
  const user = useAuthStore((state) => state.user);
  const { data, isLoading, isError } = useQuery({
    queryKey: ["support", "tickets", "mine", user?.tenant?.id, user?.id],
    queryFn: listMySupportTickets,
    enabled: !!user,
    refetchInterval: 20_000,
  });

  return (
    <Card withBorder padding="lg">
      <Stack gap="sm">
        <Title order={4}>{t("myTickets.title")}</Title>
        {isError && (
          <Text size="sm" c="red" role="alert">
            {t("myTickets.loadError")}
          </Text>
        )}
        {isLoading ? (
          <Loader size="sm" />
        ) : data && data.length > 0 ? (
          <Stack gap="xs">
            {data.map((ticket) => (
              <TicketCard key={`${user?.tenant?.id}:${user?.id}:${ticket.id}`} ticket={ticket} />
            ))}
          </Stack>
        ) : !isError ? (
          <Text size="sm" c="dimmed">
            {t("myTickets.empty")}
          </Text>
        ) : null}
      </Stack>
    </Card>
  );
}

function TicketCard({ ticket }: { ticket: SupportTicket }) {
  const { t, i18n } = useTranslation("support");
  const user = useAuthStore((state) => state.user);
  const queryClient = useQueryClient();
  const [showFullHistory, setShowFullHistory] = useState(false);
  const history = useQuery({
    queryKey: ["support", "ticket", user?.tenant?.id, user?.id, ticket.id],
    queryFn: () => getSupportTicket(ticket.id),
    enabled: !!user && showFullHistory,
    refetchInterval: 20_000,
    retry: false,
  });
  const [reply, setReply] = useState("");
  const [idempotencyKey, setIdempotencyKey] = useState<string | null>(null);
  const mutation = useMutation({
    mutationFn: ({ body, key }: { body: string; key: string }) =>
      addSupportTicketComment(ticket.id, body, key),
    onSuccess: () => {
      setReply("");
      setIdempotencyKey(null);
      void queryClient.invalidateQueries({ queryKey: ["support", "tickets", "mine"] });
      void queryClient.invalidateQueries({
        queryKey: ["support", "ticket", user?.tenant?.id, user?.id, ticket.id],
      });
      toast({ variant: "success", description: t("myTickets.replySuccess") });
    },
  });
  const fullPortal =
    history.data?.portal?.syncState === "connected" ? history.data.portal : undefined;
  const portal = ticket.portal;
  const status = portal?.status;
  const canReply =
    portal?.syncState === "connected" &&
    !!portal.ticketId &&
    status !== "resolved" &&
    status !== "closed" &&
    status !== "cancelled";
  const comments = [...(fullPortal?.comments ?? portal?.comments ?? [])].sort((a, b) =>
    a.createdAt.localeCompare(b.createdAt)
  );

  return (
    <Card withBorder padding="sm">
      <Stack gap="sm">
        <Group justify="space-between" wrap="nowrap" align="flex-start">
          <Stack gap={2}>
            <Text size="sm" fw={500}>
              {ticket.subject}
            </Text>
            {portal?.number && (
              <Text size="xs" c="dimmed">
                {portal.number}
              </Text>
            )}
            <Text size="xs" c="dimmed">
              {formatDateTime(ticket.createdAt, i18n.language)}
            </Text>
          </Stack>
          {status ? (
            <Badge {...PORTAL_STATUS_BADGE[status]}>{t(`myTickets.portalStatus.${status}`)}</Badge>
          ) : (
            <Badge {...STATUS_BADGE[ticket.status]}>{t(`myTickets.status.${ticket.status}`)}</Badge>
          )}
        </Group>
        {portal && portal.syncState !== "connected" && (
          <Text size="xs" c="dimmed">
            {t(`myTickets.syncState.${portal.syncState}`)}
          </Text>
        )}
        {ticket.resolution && (
          <Text size="xs" style={{ whiteSpace: "pre-wrap" }}>
            {t("myTickets.resolution")}: {ticket.resolution}
          </Text>
        )}
        {comments.length > 0 && (
          <Stack
            gap="xs"
            component="ol"
            aria-label={t("myTickets.messages")}
            className="list-none p-0 m-0"
          >
            {comments.map((comment) => (
              <Card key={comment.id} component="li" withBorder padding="xs">
                <Group gap="xs">
                  <Text size="xs" fw={500}>
                    {comment.authorName ?? t("myTickets.supportTeam")}
                  </Text>
                  {(comment.source === "customer_agent" || comment.source === "external_agent") && (
                    <Badge size="xs" variant="light">
                      {t(`myTickets.sources.${comment.source}`)}
                    </Badge>
                  )}
                  <Text size="xs" c="dimmed">
                    {formatDateTime(comment.createdAt, i18n.language)}
                  </Text>
                </Group>
                <Text size="sm" style={{ whiteSpace: "pre-wrap", overflowWrap: "anywhere" }}>
                  {comment.body}
                </Text>
              </Card>
            ))}
          </Stack>
        )}
        {ticket.portal?.commentsTruncated && (!fullPortal || fullPortal.commentsTruncated) && (
          <Button
            variant="subtle"
            size="xs"
            className="self-start"
            loading={history.isFetching}
            onClick={() => {
              if (showFullHistory) void history.refetch();
              else setShowFullHistory(true);
            }}
          >
            {t("myTickets.fullConversation")}
          </Button>
        )}
        {showFullHistory && (history.isError || (history.data && !fullPortal)) && (
          <Text size="xs" c="red" role="alert">
            {t("myTickets.historyError")}
          </Text>
        )}
        {canReply && (
          <form
            onSubmit={(event) => {
              event.preventDefault();
              if (mutation.isPending || !reply.trim()) return;
              const key = idempotencyKey ?? crypto.randomUUID();
              setIdempotencyKey(key);
              mutation.mutate({ body: reply.trim(), key });
            }}
          >
            <Stack gap="xs">
              <Textarea
                label={t("myTickets.reply")}
                value={reply}
                minRows={2}
                maxLength={10000}
                disabled={mutation.isPending}
                onChange={(event) => {
                  setReply(event.currentTarget.value);
                  setIdempotencyKey(null);
                  mutation.reset();
                }}
              />
              {mutation.isError && (
                <Text size="xs" c="red" role="alert">
                  {getApiErrorMessage(mutation.error, t("myTickets.replyError"))}
                </Text>
              )}
              <Button
                type="submit"
                size="xs"
                loading={mutation.isPending}
                disabled={mutation.isPending || !reply.trim()}
                className="self-start"
              >
                {t("myTickets.sendReply")}
              </Button>
            </Stack>
          </form>
        )}
      </Stack>
    </Card>
  );
}
