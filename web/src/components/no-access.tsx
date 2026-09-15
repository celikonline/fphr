import { useTranslation } from "react-i18next";
import { Card, Stack, Text } from "@mantine/core";
import { Lock } from "lucide-react";

/** Shown in place of a page the current user has no permission for. */
export default function NoAccess() {
  const { t } = useTranslation(["common"]);

  return (
    <div className="mx-auto max-w-3xl p-6 md:p-10">
      <Card withBorder padding="xl">
        <Stack align="center" gap="xs" ta="center" py="md">
          <Text c="dimmed" lh={0}>
            <Lock size={32} />
          </Text>
          <Text fw={500}>{t("common:noAccessTitle")}</Text>
          <Text size="sm" c="dimmed">
            {t("common:noAccessDescription")}
          </Text>
        </Stack>
      </Card>
    </div>
  );
}
