import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Group, Modal, Text } from "@mantine/core";

export interface ConfirmModalProps {
  opened: boolean;
  onClose: () => void;
  title: string;
  message: string;
  /** Confirm button label (default: common:confirm). */
  confirmLabel?: string;
  /** Destructive styling for the confirm button (delete/deactivate flows). */
  danger?: boolean;
  /** Awaited before the modal closes itself; rejecting keeps the modal open (caller shows its own error toast). */
  onConfirm: () => Promise<void>;
}

/**
 * Faz 1 ortak bileşen: generic async confirmation dialog (delete/deactivate style actions) built on
 * top of the same Mantine `Modal` pattern as `components/confirm-dialog.tsx`, but owning its own
 * loading state so callers only pass an async `onConfirm`. First consumer: `pages/users-roles.tsx`.
 */
export function ConfirmModal({ opened, onClose, title, message, confirmLabel, danger, onConfirm }: ConfirmModalProps) {
  const { t } = useTranslation(["common"]);
  const [loading, setLoading] = useState(false);

  async function handleConfirm() {
    setLoading(true);
    try {
      await onConfirm();
      onClose();
    } finally {
      setLoading(false);
    }
  }

  return (
    <Modal
      opened={opened}
      onClose={() => (loading ? undefined : onClose())}
      title={
        <Text fw={600} size="lg">
          {title}
        </Text>
      }
      centered
    >
      <Text size="sm" c="dimmed">
        {message}
      </Text>
      <Group justify="flex-end" gap="sm" mt="lg">
        <Button type="button" variant="default" onClick={onClose} disabled={loading}>
          {t("common:cancel")}
        </Button>
        <Button
          type="button"
          variant="filled"
          color={danger ? "red" : undefined}
          onClick={handleConfirm}
          loading={loading}
        >
          {confirmLabel ?? t("common:confirm")}
        </Button>
      </Group>
    </Modal>
  );
}
