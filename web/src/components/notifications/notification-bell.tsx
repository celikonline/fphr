import { useState } from "react";
import { Link, useNavigate } from "react-router";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bell, Settings2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { usePermission } from "@/components/permission-guard";
import { cn } from "@/lib/utils";
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
const BELL_PAGE_SIZE = 10;

/** Header bell: polled unread badge, latest notifications dropdown, mark read. Renders nothing without the view permission. */
export function NotificationBell() {
  const canView = usePermission("notifications.my.view");
  if (!canView) return null;
  return <NotificationBellMenu />;
}

function NotificationBellMenu() {
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
    queryFn: () => listMyNotifications({ page: 1, pageSize: BELL_PAGE_SIZE }),
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

  return (
    <DropdownMenu open={open} onOpenChange={setOpen}>
      <DropdownMenuTrigger asChild>
        <Button
          variant="ghost"
          size="icon"
          className="relative"
          aria-label={t("notifications:bell.label")}
          title={t("notifications:bell.label")}
        >
          <Bell className="h-4 w-4" />
          {unread > 0 && (
            <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-destructive px-1 text-[10px] font-semibold leading-none text-destructive-foreground">
              {unread > 99 ? "99+" : unread}
            </span>
          )}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-80 p-0">
        <div className="flex items-center justify-between px-3 py-2">
          <DropdownMenuLabel className="p-0">{t("notifications:bell.title")}</DropdownMenuLabel>
          {canManage && (
            <Button
              variant="link"
              size="sm"
              className="h-auto p-0 text-xs"
              disabled={unread === 0 || markAll.isPending}
              onClick={() => markAll.mutate()}
            >
              {t("notifications:bell.markAllRead")}
            </Button>
          )}
        </div>
        <DropdownMenuSeparator className="m-0" />
        <div className="max-h-96 overflow-y-auto">
          {listQuery.isError ? (
            <p className="p-4 text-sm text-destructive">{t("notifications:bell.loadError")}</p>
          ) : listQuery.isLoading ? (
            <p className="p-4 text-sm text-muted-foreground">{t("notifications:bell.loading")}</p>
          ) : items.length === 0 ? (
            <p className="p-4 text-sm text-muted-foreground">{t("notifications:bell.empty")}</p>
          ) : (
            items.map((item) => (
              <DropdownMenuItem
                key={item.id}
                onSelect={() => openItem(item)}
                className={cn(
                  "flex cursor-pointer flex-col items-start gap-0.5 rounded-none px-3 py-2",
                  !item.readAt && "bg-accent/40"
                )}
              >
                <span className={cn("text-sm", !item.readAt && "font-semibold")}>{item.title}</span>
                <span className="text-xs text-muted-foreground">{item.body}</span>
                <span className="text-[11px] text-muted-foreground">{dateFormat.format(new Date(item.createdAt))}</span>
              </DropdownMenuItem>
            ))
          )}
        </div>
        <DropdownMenuSeparator className="m-0" />
        <DropdownMenuItem asChild className="rounded-none px-3 py-2">
          <Link to={NOTIFICATION_SETTINGS_PATH}>
            <Settings2 className="mr-2 h-4 w-4" /> {t("notifications:bell.preferences")}
          </Link>
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
