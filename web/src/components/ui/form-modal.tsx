import type { ComponentProps } from "react";
import { useTranslation } from "react-i18next";
import { Button, Group, Modal } from "@mantine/core";

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
export type FormModalProps = ComponentProps<typeof Modal> & {
  /**
   * Optional "Oluştur ve Yeni" action (E9). When given, a secondary button is rendered under the
   * content; the caller owns validation/submit and resets its own form while the modal stays open.
   * Omitted → the modal renders exactly as before.
   */
  onSaveAndNew?: () => void;
};

export function FormModal({ onSaveAndNew, children, ...props }: FormModalProps) {
  const { t } = useTranslation("common");
  return (
    <Modal closeButtonProps={{ "aria-label": t("close") }} {...props} styles={{ inner: { justifyContent: "flex-start" } }}>
      {children}
      {onSaveAndNew && (
        <Group justify="flex-end" mt="sm">
          <Button type="button" variant="light" data-testid="form-modal-save-and-new" onClick={onSaveAndNew}>
            {t("saveAndNew")}
          </Button>
        </Group>
      )}
    </Modal>
  );
}
