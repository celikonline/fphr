import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { Badge, Button, Card, Group, Paper, Stack, Text, ThemeIcon, Title } from "@mantine/core";
import { Hourglass } from "lucide-react";

interface ComingSoonPageProps {
  /** i18n key in the "navigation" namespace for the page title. */
  titleKey: string;
  /** Planned roadmap phase ("Faz N"). */
  phase: number;
  /** Optional sub-features (navigation i18n keys) listed as planned. */
  itemKeys?: string[];
}

/** Placeholder for menu entries whose module ships in a later roadmap phase. */
export default function ComingSoonPage({ titleKey, phase, itemKeys }: ComingSoonPageProps) {
  const { t } = useTranslation(["navigation", "common"]);

  return (
    <div className="max-w-3xl p-6 md:p-10">
      <Card withBorder padding="xl">
        <Stack align="center" gap="md">
          <ThemeIcon size={48} radius="xl" variant="light" color="gray">
            <Hourglass size={24} />
          </ThemeIcon>
          <Group gap="xs" justify="center">
            <Title order={3}>{t(titleKey)}</Title>
            <Badge variant="light">{t("navigation:phase", { phase })}</Badge>
          </Group>
          <Text size="sm" c="dimmed" ta="center">
            {t("navigation:comingSoon.description", { phase })}
          </Text>
          {itemKeys && itemKeys.length > 0 && (
            <Stack gap="xs" w="100%" maw={384} component="ul">
              {itemKeys.map((key) => (
                <Paper key={key} component="li" withBorder px="sm" py="xs">
                  <Group justify="space-between" wrap="nowrap">
                    <Text size="sm">{t(key)}</Text>
                    <Badge variant="outline">{t("navigation:phase", { phase })}</Badge>
                  </Group>
                </Paper>
              ))}
            </Stack>
          )}
          <Button component={Link} to="/app" variant="default">
            {t("common:backHome")}
          </Button>
        </Stack>
      </Card>
    </div>
  );
}
