import { useState, type ReactElement } from "react";
import { Link, useNavigate } from "react-router";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button, Group, Menu, ScrollArea, Stack, Text, Tooltip, type MenuProps } from "@mantine/core";
import { FileClock, Settings2, type LucideIcon } from "lucide-react";
import { usePermission } from "@/components/permission-guard";
import {
  NOTIFICATIONS_QUERY_KEY,
  NOTIFICATIONS_UNREAD_KEY,
  NOTIFICATION_SETTINGS_PATH,
  getUnreadCount,
  listMyNotifications,
  markAllNotificationsRead,
  markNotificationRead,
  type UserNotificationDto,
} from "@/services/notifications.service";

const UNREAD_POLL_MS = 45_000;
const PAGE_SIZE = 10;

/** Icon shown next to the title for template keys that need one (i18n label: `notifications:types.*`). */
const TEMPLATE_ICONS: Partial<Record<string, { icon: LucideIcon; labelKey: string }>> = {
  "documents.expiry_reminder": { icon: FileClock, labelKey: "notifications:types.documentExpiryReminder" },
};

interface NotificationsMenuProps {
  position: MenuProps["position"];
  /** The trigger element; receives the polled unread count for its badge. */
  renderTarget: (unread: number) => ReactElement;
  /** Shown around an icon-only trigger. */
  tooltip?: string;
}

/** Polled unread badge, latest notifications dropdown, mark read. Renders nothing without the view permission. */
export function NotificationsMenu(props: NotificationsMenuProps) {
  const canView = usePermission("notifications.my.view");
  if (!canView) return null;
  return <NotificationsDropdown {...props} />;
}

function NotificationsDropdown({ position, renderTarget, tooltip }: NotificationsMenuProps) {
  const { t, i18n } = useTranslation(["notifications"]);
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const canManage = usePermission("notifications.my.manage");
  const [open, setOpen] = useState(false);

  const unreadQuery = useQuery({
    queryKey: NOTIFICATIONS_UNREAD_KEY,
    queryFn: getUnreadCount,
    refetchInterval: UNREAD_POLL_MS,
  });
  const listQuery = useQuery({
    queryKey: [...NOTIFICATIONS_QUERY_KEY, "bell"],
    queryFn: () => listMyNotifications({ page: 1, pageSize: PAGE_SIZE }),
    enabled: open,
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: NOTIFICATIONS_QUERY_KEY });
  const markRead = useMutation({ mutationFn: markNotificationRead, onSuccess: invalidate });
  const markAll = useMutation({ mutationFn: markAllNotificationsRead, onSuccess: invalidate });

  const unread = unreadQuery.data ?? 0;
  const items = listQuery.data?.items ?? [];
  const dateFormat = new Intl.DateTimeFormat(i18n.language, { dateStyle: "short", timeStyle: "short" });

  function openItem(item: UserNotificationDto) {
    if (!item.readAt && canManage) markRead.mutate(item.id);
    if (item.actionUrl) navigate(item.actionUrl);
  }

  const target = <Menu.Target>{renderTarget(unread)}</Menu.Target>;

  return (
    <Menu opened={open} onChange={setOpen} position={position} width={320}>
      {tooltip ? (
        <Tooltip label={tooltip} position="right" openDelay={300}>
          {target}
        </Tooltip>
      ) : (
        target
      )}
      <Menu.Dropdown p={0}>
        <Group justify="space-between" wrap="nowrap" px="sm" py="xs">
          <Text size="sm" fw={600}>
            {t("notifications:bell.title")}
          </Text>
          {canManage && (
            <Button
              variant="subtle"
              size="compact-xs"
              disabled={unread === 0 || markAll.isPending}
              onClick={() => markAll.mutate()}
            >
              {t("notifications:bell.markAllRead")}
            </Button>
          )}
        </Group>
        <Menu.Divider m={0} />
        <ScrollArea.Autosize mah={384} type="auto">
          {listQuery.isError ? (
            <Text size="sm" c="red" p="md">
              {t("notifications:bell.loadError")}
            </Text>
          ) : listQuery.isLoading ? (
            <Text size="sm" c="dimmed" p="md">
              {t("notifications:bell.loading")}
            </Text>
          ) : items.length === 0 ? (
            <Text size="sm" c="dimmed" p="md">
              {t("notifications:bell.empty")}
            </Text>
          ) : (
            items.map((item) => {
              const typeIcon = TEMPLATE_ICONS[item.templateKey];
              return (
                <Menu.Item
                  key={item.id}
                  onClick={() => openItem(item)}
                  bdrs={0}
                  px="sm"
                  py="xs"
                  bg={item.readAt ? undefined : "var(--mantine-primary-color-light)"}
                >
                  <Stack gap={2} align="flex-start">
                    <Group gap={6} wrap="nowrap">
                      {typeIcon && (
                        <Tooltip label={t(typeIcon.labelKey)}>
                          <typeIcon.icon size={14} aria-label={t(typeIcon.labelKey)} />
                        </Tooltip>
                      )}
                      <Text size="sm" fw={item.readAt ? 400 : 600}>
                        {item.title}
                      </Text>
                    </Group>
                    <Text size="xs" c="dimmed">
                      {item.body}
                    </Text>
                    <Text fz={11} c="dimmed">
                      {dateFormat.format(new Date(item.createdAt))}
                    </Text>
                  </Stack>
                </Menu.Item>
              );
            })
          )}
        </ScrollArea.Autosize>
        <Menu.Divider m={0} />
        <Menu.Item
          component={Link}
          to={NOTIFICATION_SETTINGS_PATH}
          leftSection={<Settings2 size={16} />}
          bdrs={0}
          px="sm"
          py="xs"
        >
          {t("notifications:bell.preferences")}
        </Menu.Item>
      </Menu.Dropdown>
    </Menu>
  );
}
