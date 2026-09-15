import type { TFunction } from "i18next";
import type { NotificationItemDto } from "@/services/notifications.service";

/** Renders a notification in the UI language from its template key + args; falls back to the server-rendered text. */
export function renderNotification(t: TFunction, notification: NotificationItemDto): { title: string; body: string } {
  const args = notification.args ?? {};
  const requestType = args.RequestType
    ? t(`workflow:requestType.${args.RequestType}`, { defaultValue: args.RequestType })
    : undefined;
  const values = { ...args, ...(requestType ? { RequestType: requestType } : {}) };
  const base = `notifications:templates.${notification.templateKey}`;
  return {
    title: t(`${base}.title`, { ...values, defaultValue: notification.title }),
    body: t(`${base}.body`, { ...values, defaultValue: notification.body }),
  };
}
