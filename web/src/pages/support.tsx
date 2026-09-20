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
import {
  SUPPORT_TICKET_CATEGORIES,
  createSupportTicket,
  getHelpArticle,
  listMySupportTickets,
  searchHelpArticles,
  type SupportTicketCategory,
  type SupportTicketStatus,
} from "@/services/support.service";

type BadgeStyle = Pick<BadgeProps, "variant" | "color">;

const STATUS_BADGE: Record<SupportTicketStatus, BadgeStyle> = {
  open: { variant: "light", color: "gray" },
  answered: { variant: "filled", color: "green" },
  closed: { variant: "outline", color: "gray" },
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
            <Button variant="subtle" size="xs" leftSection={<ArrowLeft size={14} />} onClick={() => setSelectedSlug(null)} className="self-start">
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
                        {t(`helpCenter.categories.${article.category}`, { defaultValue: article.category })}
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
    mutationFn: () => createSupportTicket({ subject, body, category, sourceRoute: location.pathname }),
    onSuccess: () => {
      toast({ variant: "success", description: t("contact.submitSuccess") });
      setSubject("");
      setBody("");
      setCategory("general");
      queryClient.invalidateQueries({ queryKey: ["support", "tickets", "mine"] });
    },
    onError: (error) => {
      toast({ variant: "destructive", description: getApiErrorMessage(error, t("contact.submitError")) });
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
          data={SUPPORT_TICKET_CATEGORIES.map((c) => ({ value: c, label: t(`contact.categories.${c}`) }))}
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
  const { t, i18n } = useTranslation("support");
  const { data, isLoading } = useQuery({
    queryKey: ["support", "tickets", "mine"],
    queryFn: listMySupportTickets,
  });

  return (
    <Card withBorder padding="lg">
      <Stack gap="sm">
        <Title order={4}>{t("myTickets.title")}</Title>
        {isLoading ? (
          <Loader size="sm" />
        ) : data && data.length > 0 ? (
          <Stack gap="xs">
            {data.map((ticket) => (
              <Card key={ticket.id} withBorder padding="sm">
                <Group justify="space-between" wrap="nowrap" align="flex-start">
                  <Stack gap={2}>
                    <Text size="sm" fw={500}>
                      {ticket.subject}
                    </Text>
                    <Text size="xs" c="dimmed">
                      {formatDateTime(ticket.createdAt, i18n.language)}
                    </Text>
                    {ticket.resolution ? (
                      <Text size="xs" mt={4}>
                        <Text component="span" fw={500}>
                          {t("myTickets.resolution")}:
                        </Text>{" "}
                        {ticket.resolution}
                      </Text>
                    ) : null}
                  </Stack>
                  <Badge {...STATUS_BADGE[ticket.status]}>{t(`myTickets.status.${ticket.status}`)}</Badge>
                </Group>
              </Card>
            ))}
          </Stack>
        ) : (
          <Text size="sm" c="dimmed">
            {t("myTickets.empty")}
          </Text>
        )}
      </Stack>
    </Card>
  );
}
