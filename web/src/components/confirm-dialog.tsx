import { useTranslation } from "react-i18next";
import { Button, Group, Modal, Text } from "@mantine/core";

interface ConfirmDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title?: string;
  description: string;
  onConfirm: () => void;
  isLoading?: boolean;
  /** Confirm button text (default: common:delete). */
  confirmLabel?: string;
  /** Confirm button style (default: destructive). */
  confirmVariant?: "default" | "destructive";
}

/**
 * Shared delete/destructive-action confirmation dialog, reused by the Item 20 CRUD screens
 * (Organization, Locations, Positions) instead of duplicating the same markup three times.
 */
export default function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  onConfirm,
  isLoading,
  confirmLabel,
  confirmVariant = "destructive",
}: ConfirmDialogProps) {
  const { t } = useTranslation(["common"]);

  return (
    <Modal
      opened={open}
      onClose={() => onOpenChange(false)}
      title={
        <Text fw={600} size="lg">
          {title ?? t("common:confirmDeleteTitle")}
        </Text>
      }
      centered
    >
      <Text size="sm" c="dimmed">
        {description}
      </Text>
      <Group justify="flex-end" gap="sm" mt="lg">
        <Button type="button" variant="default" onClick={() => onOpenChange(false)} disabled={isLoading}>
          {t("common:cancel")}
        </Button>
        <Button
          type="button"
          variant="filled"
          color={confirmVariant === "destructive" ? "red" : undefined}
          onClick={onConfirm}
          loading={isLoading}
        >
          {confirmLabel ?? t("common:delete")}
        </Button>
      </Group>
    </Modal>
  );
}
