import { useRef, useState } from "react";
import { useNavigate } from "react-router";
import { useTranslation } from "react-i18next";
import { Search } from "lucide-react";
import { Combobox, Kbd, Text, TextInput, useCombobox } from "@mantine/core";
import { useHotkeys } from "@mantine/hooks";
import { APP_LAUNCHER_ITEMS, NAV_ITEMS, SETTINGS_SECTIONS } from "@/config/navigation";
import { useVisibleItems } from "@/hooks/use-nav-visibility";

const MAX_RESULTS = 8;
const ALL_SETTINGS = SETTINGS_SECTIONS.flatMap((section) =>
  section.links.map((link) => ({ ...link, sectionLabelKey: section.labelKey }))
);
const RELEASED_NAV = NAV_ITEMS.filter((item) => item.phase === undefined);
const RELEASED_APPS = APP_LAUNCHER_ITEMS.filter((item) => item.phase === undefined);

interface SearchEntry {
  path: string;
  label: string;
  /** Second line: the settings group or the entry kind, so identical labels stay distinguishable. */
  context: string;
  haystack: string;
}

/**
 * Header search over every screen the user may open (menu, apps, settings pages), permission and module filtered
 * like the menus themselves. Ctrl/Cmd+K focuses it; Enter opens the first hit.
 */
export default function ScreenSearch() {
  const { t, i18n } = useTranslation(["navigation"]);
  const navigate = useNavigate();
  const inputRef = useRef<HTMLInputElement>(null);
  const [query, setQuery] = useState("");
  const combobox = useCombobox({ onDropdownClose: () => combobox.resetSelectedOption() });

  const menu = useVisibleItems(RELEASED_NAV);
  const apps = useVisibleItems(RELEASED_APPS);
  const settings = useVisibleItems(ALL_SETTINGS);

  useHotkeys([["mod+K", () => inputRef.current?.focus()]], []);

  const locale = i18n.language;
  const entries: SearchEntry[] = [
    ...menu.map((item) => ({
      path: item.path,
      label: t(item.labelKey),
      context: t("navigation:search.menu"),
      haystack: t(item.labelKey),
    })),
    ...apps.map((item) => ({
      path: item.path,
      label: t(item.labelKey),
      context: t("navigation:search.apps"),
      haystack: t(item.labelKey),
    })),
    ...settings.map((link) => ({
      path: link.path,
      label: t(link.labelKey),
      context: `${t("navigation:search.settings")} › ${t(link.sectionLabelKey)}`,
      haystack: `${t(link.labelKey)} ${t(link.descriptionKey)}`,
    })),
  ];

  const needle = query.trim().toLocaleLowerCase(locale);
  const seen = new Set<string>();
  const results =
    needle === ""
      ? []
      : entries
          .filter((entry) => entry.haystack.toLocaleLowerCase(locale).includes(needle))
          .filter((entry) => (seen.has(entry.path) ? false : (seen.add(entry.path), true)))
          .slice(0, MAX_RESULTS);

  const open = (path: string) => {
    setQuery("");
    combobox.closeDropdown();
    inputRef.current?.blur();
    void navigate(path);
  };

  return (
    <Combobox store={combobox} onOptionSubmit={open} width={360} position="bottom-start">
      <Combobox.Target>
        <TextInput
          ref={inputRef}
          value={query}
          onChange={(event) => {
            setQuery(event.currentTarget.value);
            combobox.openDropdown();
          }}
          onFocus={() => combobox.openDropdown()}
          onBlur={() => combobox.closeDropdown()}
          onKeyDown={(event) => {
            if (event.key === "Escape") {
              setQuery("");
              inputRef.current?.blur();
            } else if (
              event.key === "Enter" &&
              results[0] &&
              combobox.getSelectedOptionIndex() === -1
            ) {
              event.preventDefault();
              open(results[0].path);
            }
          }}
          placeholder={t("navigation:search.placeholder")}
          aria-label={t("navigation:search.label")}
          size="sm"
          radius="md"
          leftSection={<Search size={16} />}
          rightSectionWidth={64}
          rightSection={
            query === "" ? (
              <Text component="span" size="xs" c="dimmed" className="hidden lg:inline">
                <Kbd size="xs">Ctrl</Kbd> <Kbd size="xs">K</Kbd>
              </Text>
            ) : null
          }
          className="w-full min-w-0 max-w-sm"
          data-testid="screen-search"
        />
      </Combobox.Target>
      <Combobox.Dropdown hidden={needle === ""}>
        <Combobox.Options>
          {results.length === 0 ? (
            <Combobox.Empty>{t("navigation:search.nothing")}</Combobox.Empty>
          ) : (
            results.map((entry) => (
              <Combobox.Option key={entry.path} value={entry.path}>
                <Text size="sm">{entry.label}</Text>
                <Text size="xs" c="dimmed">
                  {entry.context}
                </Text>
              </Combobox.Option>
            ))
          )}
        </Combobox.Options>
      </Combobox.Dropdown>
    </Combobox>
  );
}
