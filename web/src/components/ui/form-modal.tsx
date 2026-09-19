import type { ComponentProps } from "react";
import { Modal } from "@mantine/core";

/**
 * Left-aligned Mantine `Modal` for create/edit forms (senseik-021). Mantine's `Modal` centers
 * horizontally by default (the `centered` prop only controls vertical placement) — product wants
 * form dialogs anchored to the left edge of the viewport instead. `Modal` has no `position` prop
 * (unlike `Drawer`), so this overrides the `inner` wrapper's flex alignment; that is the least
 * invasive option because it keeps the existing `Modal` API (title, size, fullScreen, onClose, ...)
 * untouched for every current usage apart from the import — no need to switch to `Drawer`, whose
 * slide-in animation and edge-docked layout are a different pattern already used for side panels
 * elsewhere in the app (see leave-request-detail.tsx and friends).
 *
 * Confirmation/simple dialogs (`components/confirm-dialog.tsx`, `components/ui/confirm-modal.tsx`)
 * intentionally keep using the plain Mantine `Modal` with `centered` — this wrapper is only for
 * multi-field forms, not yes/no prompts.
 */
export function FormModal(props: ComponentProps<typeof Modal>) {
  return <Modal {...props} styles={{ inner: { justifyContent: "flex-start" } }} />;
}
