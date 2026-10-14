import { useState } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { Badge, Card, Group, Stack, Text, TextInput, Title } from "@mantine/core";
import { AppWindow, Search } from "lucide-react";
import { APP_LAUNCHER_ITEMS, APP_LAUNCHER_SECTIONS, navIconColor } from "@/config/navigation";
import { useVisibleItems } from "@/hooks/use-nav-visibility";
import classes from "./apps.module.css";

const ACTIVE_APPS = APP_LAUNCHER_ITEMS.filter((item) => item.phase === undefined);
const PLANNED_APPS = APP_LAUNCHER_ITEMS.filter((item) => item.phase !== undefined);

/** "Uygulamalar" launcher: available apps link out; later-phase apps are listed disabled with their phase. */
export default function AppsPage() {
  const { t } = useTranslation(["navigation"]);
  const visibleApps = useVisibleItems(ACTIVE_APPS);
  const [query, setQuery] = useState("");

  const needle = query.trim().toLocaleLowerCase("tr");
  const matches = (labelKey: string) =>
    needle === "" || t(labelKey).toLocaleLowerCase("tr").includes(needle);
  const activeApps = visibleApps.filter((item) => matches(item.labelKey));
  const plannedApps = PLANNED_APPS.filter((item) => matches(item.labelKey));
  const groupedApps = APP_LAUNCHER_SECTIONS.map((section) => ({
    ...section,
    apps: activeApps.filter((item) => section.itemKeys.some((key) => key === item.key)),
  })).filter((section) => section.apps.length > 0);
  const ungroupedApps = activeApps.filter(
    (item) =>
      !APP_LAUNCHER_SECTIONS.some((section) => section.itemKeys.some((key) => key === item.key))
  );

  return (
    <Stack gap="lg" className="max-w-5xl p-6 md:p-10">
      <div>
        <Title order={1} fz={24}>
          <Group gap="xs" component="span">
            <AppWindow size={24} /> {t("navigation:nav.apps")}
          </Group>
        </Title>
        <Text c="dimmed">{t("navigation:apps.subtitle")}</Text>
      </div>

      <TextInput
        aria-label={t("navigation:apps.searchPlaceholder")}
        placeholder={t("navigation:apps.searchPlaceholder")}
        leftSection={<Search size={16} />}
        value={query}
        onChange={(event) => setQuery(event.currentTarget.value)}
        maw={400}
      />

      {activeApps.length === 0 && plannedApps.length === 0 && (
        <Text c="dimmed">{t("navigation:apps.noResults")}</Text>
      )}

      {groupedApps.map((section) => (
        <Stack key={section.key} gap="sm">
          <Title order={2} fz="lg">
            {t(`navigation:${section.labelKey}`)}
          </Title>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
            {section.apps.map((item) => {
              const Icon = item.icon;
              return (
                <Card
                  key={item.key}
                  component={Link}
                  to={item.path}
                  withBorder
                  padding="md"
                  h="100%"
                  className={classes.appCard}
                >
                  <Group align="center" gap="sm" wrap="nowrap">
                    <Icon size={22} color={navIconColor(item)} />
                    <Text size="sm" fw={500} lh={1.3}>
                      {t(item.labelKey)}
                    </Text>
                  </Group>
                </Card>
              );
            })}
          </div>
        </Stack>
      ))}

      {ungroupedApps.length > 0 && (
        <Stack gap="sm">
          <Title order={2} fz="lg">
            {t("navigation:navGroups.otherModules")}
          </Title>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
            {ungroupedApps.map((item) => {
              const Icon = item.icon;
              return (
                <Card
                  key={item.key}
                  component={Link}
                  to={item.path}
                  withBorder
                  padding="md"
                  h="100%"
                  className={classes.appCard}
                >
                  <Group align="center" gap="sm" wrap="nowrap">
                    <Icon size={22} color={navIconColor(item)} />
                    <Text size="sm" fw={500} lh={1.3}>
                      {t(item.labelKey)}
                    </Text>
                  </Group>
                </Card>
              );
            })}
          </div>
        </Stack>
      )}

      {plannedApps.length > 0 && (
        <Stack gap="sm">
          <Title order={2} fz="lg">
            {t("navigation:apps.comingSoon")}
          </Title>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
            {plannedApps.map((item) => {
              const Icon = item.icon;
              return (
                <Card key={item.key} aria-disabled withBorder padding="md" h="100%" opacity={0.6}>
                  <Group align="center" gap="sm" wrap="nowrap">
                    <Icon size={22} color="var(--mantine-color-dimmed)" />
                    <Stack gap={4}>
                      <Text size="sm" fw={500}>
                        {t(item.labelKey)}
                      </Text>
                      <Badge variant="outline" color="gray" tt="none">
                        {t("navigation:phase", { phase: item.phase })}
                      </Badge>
                    </Stack>
                  </Group>
                </Card>
              );
            })}
          </div>
        </Stack>
      )}
    </Stack>
  );
}
