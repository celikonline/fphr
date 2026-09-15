/**
 * App-wide `toast()` backed by Mantine notifications. Callable from anywhere (services, mutation
 * callbacks) without a hook; `<Notifications />` is mounted in `MantineRoot`.
 */
import type { ReactNode } from "react";
import { notifications } from "@mantine/notifications";

export interface ToastOptions {
  title?: ReactNode;
  description?: ReactNode;
  variant?: "default" | "success" | "destructive";
  /** Auto close delay in ms (default 5000); `false` keeps it open. */
  duration?: number | false;
}

const COLOR: Record<NonNullable<ToastOptions["variant"]>, string | undefined> = {
  default: undefined,
  success: "green",
  destructive: "red",
};

function toast({ title, description, variant = "default", duration = 5000 }: ToastOptions) {
  const id = notifications.show({
    title,
    message: description ?? "",
    color: COLOR[variant],
    autoClose: duration,
  });
  return {
    id,
    dismiss: () => notifications.hide(id),
    update: (next: ToastOptions) =>
      notifications.update({
        id,
        title: next.title ?? title,
        message: next.description ?? description ?? "",
        color: COLOR[next.variant ?? variant],
      }),
  };
}

export { toast };
