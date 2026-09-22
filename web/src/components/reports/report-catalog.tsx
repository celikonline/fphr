import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { Badge, Card, Group, SegmentedControl, SimpleGrid, Skeleton, Stack, Table, Text, TextInput, ThemeIcon, Title } from "@mantine/core";
import { CalendarDays, Check, ClipboardList, FileSpreadsheet, LayoutGrid, List, Search, Users, Wallet } from "lucide-react";
import { groupReportCatalog, type ReportCatalogItem } from "@/services/reports.service";

type CatalogView = "cards" | "grid";

const VIEW_STORAGE_KEY = "senseik.reports.catalogView";

const CATEGORY_ICON: Record<string, typeof ClipboardList> = {
  administrative: ClipboardList,
  employee: Users,
  leave: CalendarDays,
  finance: Wallet,
};

/** Catalog keys are dotted ("leave.balance.summary"); i18n resolves them as a nested path under `catalog`. */
export function reportLabelKey(reportKey: string, field: "label" | "description"): string {
  return `reports:catalog.${reportKey}.${field}`;
}

function readStoredView(): CatalogView {
  try {
    return window.localStorage.getItem(VIEW_STORAGE_KEY) === "grid" ? "grid" : "cards";
  } catch {
    return "cards";
  }
}

function storeView(view: CatalogView) {
  try {
    window.localStorage.setItem(VIEW_STORAGE_KEY, view);
  } catch {
    /* private mode / blocked storage: the choice just is not remembered */
  }
}

/** Lower-cases (Turkish-aware) and strips diacritics so "iş" also finds "IŞ", "ış" and "İş". */
function fold(value: string, language: string): string {
  return value
    .toLocaleLowerCase(language)
    .replace(/ı/g, "i")
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "");
}

interface ReportCatalogProps {
  items: ReportCatalogItem[];
  isLoading: boolean;
  selectedKey: string | null;
  onSelect: (key: string) => void;
}

/** Report catalog with a search box and a card / grid (table) switch; the view choice is remembered per browser. */
export function ReportCatalog({ items, isLoading, selectedKey, onSelect }: ReportCatalogProps) {
  const { t, i18n } = useTranslation(["reports"]);
  const [search, setSearch] = useState("");
  const [view, setView] = useState<CatalogView>(readStoredView);

  const label = (item: ReportCatalogItem) => t(reportLabelKey(item.key, "label"), { defaultValue: item.key });
  const description = (item: ReportCatalogItem) => t(reportLabelKey(item.key, "description"), { defaultValue: "" });
  const categoryName = (categoryKey: string) => t(`reports:categories.${categoryKey}`, { defaultValue: t("reports:categories.other") });

  const groups = useMemo(() => {
    const query = fold(search.trim(), i18n.language);
    const visible = query
      ? items.filter((item) => {
          const haystack = [
            t(reportLabelKey(item.key, "label"), { defaultValue: item.key }),
            t(reportLabelKey(item.key, "description"), { defaultValue: "" }),
            t(`reports:categories.${item.categoryKey}`, { defaultValue: "" }),
          ].join(" ");
          return fold(haystack, i18n.language).includes(query);
        })
      : items;
    return groupReportCatalog(visible);
  }, [items, search, i18n.language, t]);

  const changeView = (value: string) => {
    const next: CatalogView = value === "grid" ? "grid" : "cards";
    setView(next);
    storeView(next);
  };

  return (
    <Stack gap="md">
      <Group justify="space-between" align="flex-end" wrap="wrap" gap="sm">
        <Title order={2} size="h4">
          {t("reports:catalog.title")}
        </Title>
        <Group gap="xs" wrap="nowrap">
          <TextInput
            aria-label={t("reports:catalogView.search")}
            placeholder={t("reports:catalogView.search")}
            leftSection={<Search size={14} />}
            value={search}
            onChange={(event) => setSearch(event.currentTarget.value)}
            w={{ base: 160, sm: 240 }}
          />
          <SegmentedControl
            aria-label={t("reports:catalogView.label")}
            value={view}
            onChange={changeView}
            data={[
              { value: "cards", label: <LayoutGrid size={16} aria-label={t("reports:catalogView.cards")} /> },
              { value: "grid", label: <List size={16} aria-label={t("reports:catalogView.grid")} /> },
            ]}
          />
        </Group>
      </Group>

      {isLoading ? (
        <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="sm">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} h={96} radius="md" />
          ))}
        </SimpleGrid>
      ) : items.length === 0 ? (
        <EmptyCard message={t("reports:catalog.empty")} />
      ) : groups.length === 0 ? (
        <EmptyCard message={t("reports:catalogView.noResults")} />
      ) : view === "grid" ? (
        <Card withBorder padding={0} radius="md">
          <Table.ScrollContainer minWidth={420}>
            <Table highlightOnHover verticalSpacing="xs">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t("reports:catalogView.columns.report")}</Table.Th>
                  <Table.Th visibleFrom="sm">{t("reports:catalogView.columns.category")}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {groups.flatMap((group) =>
                  group.items.map((item) => (
                    <Table.Tr
                      key={item.key}
                      onClick={() => onSelect(item.key)}
                      aria-selected={item.key === selectedKey}
                      bg={item.key === selectedKey ? "var(--mantine-color-indigo-light)" : undefined}
                      style={{ cursor: "pointer" }}
                    >
                      <Table.Td>
                        <Text size="sm" fw={600}>
                          {label(item)}
                        </Text>
                        <Text size="xs" c="dimmed" lh={1.4}>
                          {description(item)}
                        </Text>
                      </Table.Td>
                      <Table.Td visibleFrom="sm">
                        <Badge variant="light" color="gray">
                          {categoryName(group.categoryKey)}
                        </Badge>
                      </Table.Td>
                    </Table.Tr>
                  )),
                )}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        </Card>
      ) : (
        groups.map((group) => {
          const Icon = CATEGORY_ICON[group.categoryKey] ?? FileSpreadsheet;
          return (
            <Stack key={group.categoryKey} gap="xs">
              <Group gap="xs">
                <ThemeIcon variant="light" color="indigo" size="sm" radius="sm">
                  <Icon size={14} />
                </ThemeIcon>
                <Text fw={600} size="sm">
                  {categoryName(group.categoryKey)}
                </Text>
                <Badge variant="default" size="xs">
                  {group.items.length}
                </Badge>
              </Group>
              <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="sm">
                {group.items.map((item) => (
                  <ReportCard key={item.key} item={item} selected={item.key === selectedKey} onSelect={() => onSelect(item.key)} />
                ))}
              </SimpleGrid>
            </Stack>
          );
        })
      )}
    </Stack>
  );
}

function EmptyCard({ message }: { message: string }) {
  return (
    <Card withBorder padding="lg" radius="md">
      <Text c="dimmed" size="sm">
        {message}
      </Text>
    </Card>
  );
}

function ReportCard({ item, selected, onSelect }: { item: ReportCatalogItem; selected: boolean; onSelect: () => void }) {
  const { t } = useTranslation(["reports"]);
  return (
    <Card
      component="button"
      type="button"
      withBorder
      padding="md"
      radius="md"
      ta="left"
      onClick={onSelect}
      aria-pressed={selected}
      style={{
        cursor: "pointer",
        borderColor: selected ? "var(--mantine-color-indigo-5)" : undefined,
        boxShadow: selected ? "0 0 0 1px var(--mantine-color-indigo-5)" : undefined,
      }}
    >
      <Group justify="space-between" align="flex-start" wrap="nowrap">
        <div>
          <Text fw={600} size="sm">
            {t(reportLabelKey(item.key, "label"), { defaultValue: item.key })}
          </Text>
          <Text size="xs" c="dimmed" mt={4} lh={1.5}>
            {t(reportLabelKey(item.key, "description"), { defaultValue: "" })}
          </Text>
        </div>
        {selected && (
          <ThemeIcon variant="light" color="indigo" size="sm" radius="xl">
            <Check size={12} />
          </ThemeIcon>
        )}
      </Group>
    </Card>
  );
}
