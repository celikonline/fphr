import { useTranslation } from "react-i18next";
import { Badge, Stack, Text } from "@mantine/core";

/** "Incalculable" label with the backend's reason as a hint; unknown reason codes are shown as raw text. */
export function IncalculableHint({ reason }: { reason?: string | null }) {
  const { t } = useTranslation(["attendance"]);
  if (!reason) return null;
  return (
    <Stack gap={2} align="flex-start">
      <Badge variant="light" color="yellow">
        {t("attendance:incalculableReason.label")}
      </Badge>
      <Text size="xs" c="dimmed">
        {t(`attendance:incalculableReason.${reason}`, { defaultValue: reason })}
      </Text>
    </Stack>
  );
}
