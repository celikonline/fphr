import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { ChevronRight, Settings } from "lucide-react";
import { Badge, Card, Group, NavLink, SimpleGrid, Stack, Text, ThemeIcon, Title } from "@mantine/core";
import { SETTINGS_SECTIONS, type SettingsLink, type SettingsSection } from "@/config/navigation";
import { useVisibleItems } from "@/hooks/use-nav-visibility";

function SettingsLinkRow({ link }: { link: SettingsLink }) {
  const { t } = useTranslation(["navigation"]);
  const Icon = link.icon;

  return (
    <NavLink
      component={Link}
      to={link.path}
      py="sm"
      noWrap
      leftSection={
        <ThemeIcon variant="transparent" size="sm">
          <Icon className="h-5 w-5" />
        </ThemeIcon>
      }
      rightSection={
        <Text component="span" c="dimmed" className="inline-flex">
          <ChevronRight className="h-4 w-4" />
        </Text>
      }
      label={
        <Group component="span" gap="xs" wrap="nowrap">
          <Text component="span" size="sm" fw={500}>
            {t(link.labelKey)}
          </Text>
          {link.phase !== undefined && (
            <Badge variant="outline" color="gray" size="xs">
              {t("navigation:phase", { phase: link.phase })}
            </Badge>
          )}
        </Group>
      }
      description={t(link.descriptionKey)}
    />
  );
}

function SettingsSectionCard({ section }: { section: SettingsSection }) {
  const { t } = useTranslation(["navigation"]);
  const links = useVisibleItems(section.links);
  if (links.length === 0) return null;

  return (
    <Card withBorder padding="md">
      <Title order={2} size="h5" mb="xs">
        {t(section.labelKey)}
      </Title>
      <Stack gap={4}>
        {links.map((link) => (
          <SettingsLinkRow key={link.key} link={link} />
        ))}
      </Stack>
    </Card>
  );
}

/** Ayarlar hub: Şirket bilgileri / Kurallar / Sistem ayarları / Veri aktar, each link permission-gated. */
export default function SettingsHomePage() {
  const { t } = useTranslation(["settings"]);

  return (
    <div className="max-w-5xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <Settings className="h-6 w-6" /> {t("settings:hub.title")}
        </Title>
        <Text c="dimmed">{t("settings:hub.subtitle")}</Text>
      </div>

      <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md">
        {SETTINGS_SECTIONS.map((section) => (
          <SettingsSectionCard key={section.key} section={section} />
        ))}
      </SimpleGrid>
    </div>
  );
}
