import { useState } from "react";
import { NavLink as RouterNavLink, useLocation } from "react-router";
import { useTranslation } from "react-i18next";
import { LayoutGrid, Search } from "lucide-react";
import { NavLink, ScrollArea, Stack, Text, TextInput } from "@mantine/core";
import { SETTINGS_SECTIONS } from "@/config/navigation";
import { useVisibleItems } from "@/hooks/use-nav-visibility";
import classes from "@/layouts/app-layout.module.css";

const SETTINGS_HOME_PATH = "/app/settings";
const ALL_LINKS = SETTINGS_SECTIONS.flatMap((section) => section.links);

const NAV_LINK_CLASS_NAMES = { root: classes.navLink };

/** Hub (`/app/settings`) kendi kartlarını gösterir; ikinci menü yalnızca onun altındaki sayfalarda çıkar. */
export function isSettingsSubPage(pathname: string): boolean {
  const path = pathname.replace(/\/+$/, "");
  return path.startsWith(`${SETTINGS_HOME_PATH}/`);
}

/** Ayar alt sayfalarında ana kenar çubuğunun yanında gruplanmış ikinci ayarlar menüsü (yetki/modül filtreli, yalnızca masaüstü). */
export default function SettingsSubNav() {
  const { t, i18n } = useTranslation(["navigation", "settings"]);
  const { pathname } = useLocation();
  const [query, setQuery] = useState("");
  const visibleLinks = useVisibleItems(ALL_LINKS);

  if (!isSettingsSubPage(pathname)) return null;

  const visibleKeys = new Set(visibleLinks.map((link) => link.key));
  const needle = query.trim().toLocaleLowerCase(i18n.language);
  const matches = (labelKey: string, descriptionKey: string) =>
    needle === "" ||
    `${t(labelKey)} ${t(descriptionKey)}`.toLocaleLowerCase(i18n.language).includes(needle);
  const groups = SETTINGS_SECTIONS.map((section) => ({
    section,
    links: section.links.filter((link) => visibleKeys.has(link.key) && matches(link.labelKey, link.descriptionKey)),
  })).filter((group) => group.links.length > 0);

  return (
    <aside
      data-testid="settings-subnav"
      aria-label={t("navigation:settings.subnavLabel")}
      className={`hidden w-60 shrink-0 flex-col md:flex ${classes.sidebar}`}
    >
      <ScrollArea className="min-h-0 flex-1" type="auto" offsetScrollbars>
        <Stack component="nav" gap="md" className="px-2 py-4">
          <TextInput
            type="search"
            size="xs"
            value={query}
            onChange={(event) => setQuery(event.currentTarget.value)}
            onKeyDown={(event) => {
              if (event.key === "Escape") setQuery("");
            }}
            placeholder={t("settings:hub.searchPlaceholder")}
            aria-label={t("settings:hub.searchLabel")}
            leftSection={<Search size={14} />}
          />
          <NavLink
            component={RouterNavLink}
            to={SETTINGS_HOME_PATH}
            end
            variant="light"
            classNames={NAV_LINK_CLASS_NAMES}
            leftSection={<LayoutGrid size={18} />}
            label={t("navigation:settings.allSettings")}
            noWrap
          />
          {groups.map(({ section, links }) => (
            <Stack key={section.key} gap={2}>
              <Text size="xs" fw={600} c="dimmed" tt="uppercase" className="px-3 pb-1">
                {t(section.labelKey)}
              </Text>
              {links.map((link) => {
                const Icon = link.icon;
                return (
                  <NavLink
                    key={link.key}
                    component={RouterNavLink}
                    to={link.path}
                    variant="light"
                    classNames={NAV_LINK_CLASS_NAMES}
                    leftSection={<Icon size={18} />}
                    label={t(link.labelKey)}
                    noWrap
                  />
                );
              })}
            </Stack>
          ))}
          {needle !== "" && groups.length === 0 && (
            <Text size="sm" c="dimmed" ta="center" role="status">
              {t("navigation:search.nothing")}
            </Text>
          )}
        </Stack>
      </ScrollArea>
    </aside>
  );
}
