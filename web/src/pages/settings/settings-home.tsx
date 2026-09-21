import { useState } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { ChevronRight, Search, Settings } from "lucide-react";
import { Badge, Card, Group, NavLink, SegmentedControl, SimpleGrid, Stack, Text, TextInput, ThemeIcon, Title } from "@mantine/core";
import { useLocalStorage } from "@mantine/hooks";
import { SETTINGS_SECTIONS, type SettingsLink, type SettingsSection } from "@/config/navigation";
import { useVisibleItems } from "@/hooks/use-nav-visibility";

const ALL_LINKS = SETTINGS_SECTIONS.flatMap((section) => section.links);

/** Per-viewer convenience (never sent to the server): how many columns the group cards are laid out in. */
const COLUMNS_KEY = "senseik.settings.columns";
const COLUMN_CHOICES = ["1", "2", "3"] as const;
type ColumnChoice = (typeof COLUMN_CHOICES)[number];
const DEFAULT_COLUMNS: ColumnChoice = "3";

function gridCols(choice: ColumnChoice) {
  const count = Number(choice);
  return { base: 1, sm: Math.min(count, 2), lg: count };
}

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

function SettingsSectionCard({ section, links }: { section: SettingsSection; links: SettingsLink[] }) {
  const { t } = useTranslation(["navigation"]);

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

/**
 * Ayarlar hub: işlevsel gruplara ayrılmış, her bağlantı yetki/modül durumuna göre filtrelenir.
 * Arama başlık, açıklama ve grup adı üzerinde çalışır (yerel dile duyarlı, büyük/küçük harf ve baştaki/sondaki boşluk yok sayılır);
 * grup adı eşleşirse grubun tüm bağlantıları gösterilir.
 */
export default function SettingsHomePage() {
  const { t, i18n } = useTranslation(["settings", "navigation"]);
  const [query, setQuery] = useState("");
  const [columns, setColumns] = useLocalStorage<ColumnChoice>({ key: COLUMNS_KEY, defaultValue: DEFAULT_COLUMNS });
  const visibleLinks = useVisibleItems(ALL_LINKS);

  const visibleKeys = new Set(visibleLinks.map((link) => link.key));
  const locale = i18n.language;
  const needle = query.trim().toLocaleLowerCase(locale);

  const groups = SETTINGS_SECTIONS.map((section) => {
    const sectionMatches = needle !== "" && t(section.labelKey, { ns: "navigation" }).toLocaleLowerCase(locale).includes(needle);
    const links = section.links.filter((link) => {
      if (!visibleKeys.has(link.key)) return false;
      if (needle === "" || sectionMatches) return true;
      const haystack = `${t(link.labelKey, { ns: "navigation" })} ${t(link.descriptionKey, { ns: "navigation" })}`;
      return haystack.toLocaleLowerCase(locale).includes(needle);
    });
    return { section, links };
  }).filter((group) => group.links.length > 0);

  const resultCount = groups.reduce((sum, group) => sum + group.links.length, 0);

  return (
    <div className="max-w-7xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <Settings className="h-6 w-6" /> {t("settings:hub.title")}
        </Title>
        <Text c="dimmed">{t("settings:hub.subtitle")}</Text>
      </div>

      <Group gap="md" align="flex-start" wrap="nowrap">
      <TextInput
        className="min-w-0 flex-1"
        type="search"
        size="md"
        value={query}
        onChange={(event) => setQuery(event.currentTarget.value)}
        onKeyDown={(event) => {
          if (event.key === "Escape") setQuery("");
        }}
        placeholder={t("settings:hub.searchPlaceholder")}
        aria-label={t("settings:hub.searchLabel")}
        leftSection={<Search className="h-4 w-4" />}
      />
      <SegmentedControl
        size="md"
        visibleFrom="sm"
        value={columns}
        onChange={(value) => setColumns(value as ColumnChoice)}
        data={COLUMN_CHOICES.map((choice) => ({ value: choice, label: choice }))}
        aria-label={t("settings:hub.columns")}
      />
      </Group>

      {needle !== "" && (
        <Text size="sm" c="dimmed" role="status" aria-live="polite">
          {resultCount > 0
            ? t("settings:hub.resultCount", { count: resultCount })
            : t("settings:hub.noResults", { query: query.trim() })}
        </Text>
      )}

      <SimpleGrid cols={gridCols(columns)} spacing="md">
        {groups.map(({ section, links }) => (
          <SettingsSectionCard key={section.key} section={section} links={links} />
        ))}
      </SimpleGrid>
    </div>
  );
}
