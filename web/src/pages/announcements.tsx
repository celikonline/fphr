import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Megaphone } from "lucide-react";
import {
  Badge,
  Card,
  Group,
  Skeleton,
  Stack,
  Tabs,
  Text,
  Title,
  UnstyledButton,
} from "@mantine/core";
import NoAccess from "@/components/no-access";
import { useAnyPermission, usePermission } from "@/components/permission-guard";
import AnnouncementManagePanel from "@/components/announcements/announcement-manage-panel";
import { AnnouncementRichContent } from "@/components/announcements/announcement-rich-content";
import { formatDateTime } from "@/lib/dates";
import {
  enumKey,
  listMyAnnouncementAttachments,
  getMyAnnouncementFeed,
  markAnnouncementRead,
  type AnnouncementFeedItem,
  type AnnouncementPriority,
} from "@/services/announcements.service";

const FEED_QUERY_KEY = ["announcements", "feed"] as const;

const PRIORITY_COLORS: Record<AnnouncementPriority, string> = {
  low: "gray",
  normal: "blue",
  high: "orange",
  urgent: "red",
};

/** Duyurular: çalışan akışı (announcements.my.feed) + İK yönetimi (announcements.announcements.manage). */
export default function AnnouncementsPage() {
  const canAccess = useAnyPermission([
    "announcements.my.feed",
    "announcements.announcements.manage",
  ]);
  if (!canAccess) return <NoAccess />;
  return <AnnouncementsScreen />;
}

function AnnouncementsScreen() {
  const { t } = useTranslation(["announcements"]);
  const canFeed = usePermission("announcements.my.feed");
  const canManage = usePermission("announcements.announcements.manage");

  return (
    <Stack gap="md" className="p-6 md:p-10">
      <Title order={2}>{t("announcements:title")}</Title>
      {canFeed && canManage ? (
        <Tabs defaultValue="feed" keepMounted={false}>
          <Tabs.List>
            <Tabs.Tab value="feed">{t("announcements:tabs.feed")}</Tabs.Tab>
            <Tabs.Tab value="manage">{t("announcements:tabs.manage")}</Tabs.Tab>
          </Tabs.List>
          <Tabs.Panel value="feed" pt="md">
            <AnnouncementFeed />
          </Tabs.Panel>
          <Tabs.Panel value="manage" pt="md">
            <AnnouncementManagePanel />
          </Tabs.Panel>
        </Tabs>
      ) : canManage ? (
        <AnnouncementManagePanel />
      ) : (
        <AnnouncementFeed />
      )}
    </Stack>
  );
}

function AnnouncementFeed() {
  const { t } = useTranslation(["announcements"]);
  const feedQuery = useQuery({ queryKey: FEED_QUERY_KEY, queryFn: getMyAnnouncementFeed });
  const items = feedQuery.data ?? [];

  return (
    <Stack gap="sm">
      {feedQuery.isLoading && <Skeleton height={96} />}
      {feedQuery.isError && <Text c="red">{t("announcements:feed.loadError")}</Text>}
      {feedQuery.isSuccess && items.length === 0 && (
        <Card withBorder>
          <Group gap="sm">
            <Megaphone size={18} />
            <Text c="dimmed">{t("announcements:feed.empty")}</Text>
          </Group>
        </Card>
      )}
      {items.map((item) => (
        <FeedCard key={item.id} item={item} />
      ))}
    </Stack>
  );
}

function FeedCard({ item }: { item: AnnouncementFeedItem }) {
  const { t, i18n } = useTranslation(["announcements"]);
  const queryClient = useQueryClient();
  const [expanded, setExpanded] = useState(false);
  const priority = enumKey<AnnouncementPriority>(item.priority);

  const attachmentsQuery = useQuery({
    queryKey: ["announcements", item.id, "feed-attachments"],
    queryFn: () => listMyAnnouncementAttachments(item.id),
    enabled: expanded,
  });

  const readMutation = useMutation({
    mutationFn: () => markAnnouncementRead(item.id),
    onSuccess: () => {
      queryClient.setQueryData<AnnouncementFeedItem[]>(FEED_QUERY_KEY, (current) =>
        current?.map((entry) => (entry.id === item.id ? { ...entry, isRead: true } : entry))
      );
    },
  });

  function toggle() {
    const next = !expanded;
    setExpanded(next);
    if (next && !item.isRead && !readMutation.isPending) readMutation.mutate();
  }

  return (
    <Card withBorder padding="md">
      <UnstyledButton onClick={toggle} aria-expanded={expanded} style={{ width: "100%" }}>
        <Group justify="space-between" wrap="nowrap" align="flex-start">
          <Stack gap={4}>
            <Group gap="xs">
              {!item.isRead && (
                <Badge size="xs" variant="filled">
                  {t("announcements:feed.new")}
                </Badge>
              )}
              <Text fw={item.isRead ? 500 : 700}>{item.title}</Text>
            </Group>
            <Text size="xs" c="dimmed">
              {t(`announcements:types.${enumKey(item.type)}`)}
              {item.publishedAt ? ` · ${formatDateTime(item.publishedAt, i18n.language)}` : ""}
            </Text>
          </Stack>
          <Badge color={PRIORITY_COLORS[priority] ?? "gray"} variant="light">
            {t(`announcements:priorities.${priority}`)}
          </Badge>
        </Group>
      </UnstyledButton>
      {expanded && (
        <div className="mt-3 text-sm">
          <AnnouncementRichContent html={item.content} attachments={attachmentsQuery.data} />
        </div>
      )}
    </Card>
  );
}
