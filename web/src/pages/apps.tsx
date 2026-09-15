import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { Badge, Card, Group, Stack, Text, Title } from "@mantine/core";
import { AppWindow } from "lucide-react";
import { APP_LAUNCHER_ITEMS } from "@/config/navigation";
import { useVisibleItems } from "@/hooks/use-nav-visibility";

const ACTIVE_APPS = APP_LAUNCHER_ITEMS.filter((item) => item.phase === undefined);
const PLANNED_APPS = APP_LAUNCHER_ITEMS.filter((item) => item.phase !== undefined);

/** "Uygulamalar" launcher: available apps link out; later-phase apps are listed disabled with their phase. */
export default function AppsPage() {
  const { t } = useTranslation(["navigation"]);
  const activeApps = useVisibleItems(ACTIVE_APPS);

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} fz={24}>
          <Group gap="xs" component="span">
            <AppWindow size={24} /> {t("navigation:nav.apps")}
          </Group>
        </Title>
        <Text c="dimmed">{t("navigation:apps.subtitle")}</Text>
      </div>

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
        {activeApps.map((item) => {
          const Icon = item.icon;
          return (
            <Card
              key={item.key}
              component={Link}
              to={item.path}
              withBorder
              padding="lg"
              h="100%"
              className="transition-colors hover:border-primary"
            >
              <Stack align="center" gap="sm" ta="center">
                <Icon size={32} color="var(--mantine-primary-color-filled)" />
                <Text size="sm" fw={500}>
                  {t(item.labelKey)}
                </Text>
              </Stack>
            </Card>
          );
        })}
        {PLANNED_APPS.map((item) => {
          const Icon = item.icon;
          return (
            <Card key={item.key} aria-disabled withBorder padding="lg" h="100%" opacity={0.6}>
              <Stack align="center" gap="sm" ta="center">
                <Icon size={32} color="var(--mantine-color-dimmed)" />
                <Text size="sm" fw={500}>
                  {t(item.labelKey)}
                </Text>
                <Badge variant="outline" color="gray" tt="none">
                  {t("navigation:phase", { phase: item.phase })}
                </Badge>
              </Stack>
            </Card>
          );
        })}
      </div>
    </div>
  );
}
