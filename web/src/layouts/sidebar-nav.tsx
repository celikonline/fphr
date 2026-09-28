import { useMemo, useState } from "react";
import { NavLink as RouterNavLink, useLocation } from "react-router";
import { useTranslation } from "react-i18next";
import type { TFunction } from "i18next";
import {
  Badge,
  Divider,
  Indicator,
  Menu,
  NavLink,
  Stack,
  Text,
  TextInput,
  Tooltip,
} from "@mantine/core";
import { Search } from "lucide-react";
import {
  NAV_FOOTER_ITEMS,
  NAV_SECTIONS,
  isNavGroup,
  navIconColor,
  type NavGroup,
  type NavItem,
} from "@/config/navigation";
import { useVisibleItems } from "@/hooks/use-nav-visibility";
import { useWorkflowInboxCount } from "@/hooks/use-workflow-inbox-count";
import classes from "./app-layout.module.css";

const NAV_LINK_CLASS_NAMES = {
  root: classes.navLink,
  section: classes.navSection,
  body: classes.navBody,
  label: classes.navLabel,
};

/** Whether the current path is inside the item's route - drives which group opens by default. */
function itemMatchesPath(item: NavItem, pathname: string): boolean {
  if (item.end) return pathname === item.path;
  return pathname === item.path || pathname.startsWith(`${item.path}/`);
}

type RenderEntry =
  | { kind: "item"; item: NavItem }
  | { kind: "group"; group: NavGroup; items: NavItem[]; forceOpen: boolean };

/** "Onay kutusu" sidebar entry: pending task count on the icon (senseik-047 D-01/D-03), mirrors NotificationsNavItem's Indicator. */
export function WorkflowInboxNavItem({
  item,
  collapsed,
  label,
}: {
  item: NavItem;
  collapsed: boolean;
  label: string;
}) {
  const Icon = item.icon;
  const pendingCount = useWorkflowInboxCount();

  const link = (
    <NavLink
      component={RouterNavLink}
      to={item.path}
      end={item.end}
      variant="light"
      aria-label={collapsed ? label : undefined}
      mod={{ collapsed }}
      classNames={NAV_LINK_CLASS_NAMES}
      leftSection={
        <Indicator
          color="red"
          size={16}
          offset={2}
          label={pendingCount}
          maxValue={99}
          disabled={pendingCount === 0}
          styles={{ indicator: { fontSize: 10, fontWeight: 600, paddingInline: 4 } }}
          inline
        >
          <Icon size={20} />
        </Indicator>
      }
      label={collapsed ? undefined : label}
    />
  );

  if (!collapsed) return link;

  return (
    <Tooltip label={label} position="right" openDelay={300}>
      {link}
    </Tooltip>
  );
}

/** A single nav leaf. Items with a `phase` are not built yet: rendered inert (no `to`) with a "Yakında" badge. */
export function NavItemLink({
  item,
  collapsed,
  label,
}: {
  item: NavItem;
  collapsed: boolean;
  label: string;
}) {
  const { t } = useTranslation(["navigation"]);
  const Icon = item.icon;
  const isPlanned = item.phase !== undefined;

  // React Router sets aria-current="page" on the matching link, which Mantine's NavLink styles as active.
  // Two separate JSX calls (instead of a ternary on `component`) so TS resolves each polymorphic overload on its own.
  const link = isPlanned ? (
    <NavLink
      component="div"
      variant="light"
      disabled
      aria-label={collapsed ? label : undefined}
      mod={{ collapsed }}
      classNames={NAV_LINK_CLASS_NAMES}
      leftSection={<Icon size={20} />}
      label={collapsed ? undefined : label}
      rightSection={
        !collapsed ? (
          <Badge size="xs" variant="light" color="gray" tt="none">
            {t("navigation:nav.comingSoon")}
          </Badge>
        ) : undefined
      }
    />
  ) : (
    <NavLink
      component={RouterNavLink}
      to={item.path}
      end={item.end}
      variant="light"
      aria-label={collapsed ? label : undefined}
      mod={{ collapsed }}
      classNames={NAV_LINK_CLASS_NAMES}
      leftSection={<Icon size={20} />}
      label={collapsed ? undefined : label}
    />
  );

  if (!collapsed) return link;

  return (
    <Tooltip label={label} position="right" openDelay={300}>
      {link}
    </Tooltip>
  );
}

/** Expanded-mode: a chevron'd NavLink whose children are the group's sub-items (Mantine's own collapse/expand). */
function ExpandedNavGroup({
  group,
  items,
  opened,
  onToggle,
  t,
}: {
  group: NavGroup;
  items: NavItem[];
  opened: boolean;
  onToggle: () => void;
  t: TFunction<"navigation">;
}) {
  const Icon = group.icon;

  return (
    <NavLink
      component="button"
      type="button"
      variant="light"
      opened={opened}
      onChange={onToggle}
      aria-expanded={opened}
      childrenOffset={28}
      classNames={NAV_LINK_CLASS_NAMES}
      leftSection={<Icon size={20} />}
      label={t(group.labelKey)}
    >
      {items.map((item) =>
        item.key === "workflowInbox" ? (
          <WorkflowInboxNavItem
            key={item.key}
            item={item}
            collapsed={false}
            label={t(item.labelKey)}
          />
        ) : (
          <NavItemLink key={item.key} item={item} collapsed={false} label={t(item.labelKey)} />
        )
      )}
    </NavLink>
  );
}

/** Collapsed-mode / mobile rail: the group is a plain icon whose sub-items open in a Menu to the right. */
function CollapsedNavGroup({
  group,
  items,
  isCurrent,
  t,
}: {
  group: NavGroup;
  items: NavItem[];
  /** The current page is one of the group's items, so the group icon carries the özlük sekmesi. */
  isCurrent: boolean;
  t: TFunction<"navigation">;
}) {
  const Icon = group.icon;
  const label = t(group.labelKey);

  return (
    <Menu
      position="right-start"
      trigger="click-hover"
      openDelay={150}
      closeDelay={150}
      withinPortal
    >
      <Menu.Target>
        <NavLink
          component="button"
          type="button"
          variant="light"
          aria-label={label}
          mod={{ collapsed: true, current: isCurrent }}
          classNames={NAV_LINK_CLASS_NAMES}
          leftSection={<Icon size={20} />}
          noWrap
        />
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Label>{label}</Menu.Label>
        {items.map((item) => {
          const ItemIcon = item.icon;
          const isPlanned = item.phase !== undefined;
          // Two separate JSX calls (instead of a ternary on `component`) so TS resolves each polymorphic overload.
          if (isPlanned) {
            return (
              <Menu.Item
                key={item.key}
                component="div"
                disabled
                leftSection={<ItemIcon size={16} />}
                rightSection={
                  <Badge size="xs" variant="light" color="gray" tt="none">
                    {t("navigation:nav.comingSoon")}
                  </Badge>
                }
              >
                {t(item.labelKey)}
              </Menu.Item>
            );
          }
          return (
            <Menu.Item
              key={item.key}
              component={RouterNavLink}
              to={item.path}
              end={item.end}
              leftSection={<ItemIcon size={16} color={navIconColor(item)} />}
            >
              {t(item.labelKey)}
            </Menu.Item>
          );
        })}
      </Menu.Dropdown>
    </Menu>
  );
}

/**
 * Single left sidebar (11.10): grouped, collapsible sections with a search box in expanded mode. Collapsed
 * (desktop icon-rail preference or the mobile rail) shows groups as an icon that opens a side Menu.
 */
export function NavList({ collapsed }: { collapsed: boolean }) {
  const { t } = useTranslation(["navigation"]);
  const location = useLocation();
  const [query, setQuery] = useState("");
  const [openOverrides, setOpenOverrides] = useState<Record<string, boolean>>({});

  const allItems = useMemo(
    () => NAV_SECTIONS.flat().flatMap((entry) => (isNavGroup(entry) ? entry.items : [entry])),
    []
  );
  const visibleItems = useVisibleItems(allItems);
  const visibleSet = useMemo(() => new Set(visibleItems), [visibleItems]);
  // Notifications and the profile live in the top bar (NotificationsButton / UserMenu), not the sidebar.
  const footerItems = useVisibleItems(NAV_FOOTER_ITEMS).filter((item) => item.key !== "account");

  const activeGroupKey = useMemo(() => {
    for (const section of NAV_SECTIONS) {
      for (const entry of section) {
        if (
          isNavGroup(entry) &&
          entry.items.some((item) => itemMatchesPath(item, location.pathname))
        ) {
          return entry.key;
        }
      }
    }
    return undefined;
  }, [location.pathname]);

  const toggleGroup = (key: string) => {
    setOpenOverrides((current) => ({
      ...current,
      [key]: !(current[key] ?? key === activeGroupKey),
    }));
  };

  const needle = query.trim().toLocaleLowerCase("tr");
  const searching = !collapsed && needle !== "";
  const matchesLabel = (labelKey: string) => t(labelKey).toLocaleLowerCase("tr").includes(needle);

  const renderedSections = NAV_SECTIONS.map((section, sectionIndex) => {
    const entries = section
      .map((entry): RenderEntry | null => {
        if (isNavGroup(entry)) {
          const visible = entry.items.filter((item) => visibleSet.has(item));
          if (visible.length === 0) return null;
          if (!searching) return { kind: "group", group: entry, items: visible, forceOpen: false };
          const groupLabelMatches = matchesLabel(entry.labelKey);
          const matched = groupLabelMatches
            ? visible
            : visible.filter((item) => matchesLabel(item.labelKey));
          if (matched.length === 0) return null;
          return { kind: "group", group: entry, items: matched, forceOpen: true };
        }
        if (!visibleSet.has(entry)) return null;
        if (searching && !matchesLabel(entry.labelKey)) return null;
        return { kind: "item", item: entry };
      })
      .filter((entry): entry is RenderEntry => entry !== null);

    return { key: `section-${sectionIndex}`, entries };
  }).filter((section) => section.entries.length > 0);
  const hasAnyVisibleEntry = renderedSections.length > 0;

  return (
    <>
      <Stack
        component="nav"
        gap={4}
        className={`flex-1 overflow-y-auto px-2 py-4 ${classes.navScroll}`}
      >
        {!collapsed && (
          <TextInput
            type="search"
            size="xs"
            value={query}
            onChange={(event) => setQuery(event.currentTarget.value)}
            onKeyDown={(event) => {
              if (event.key === "Escape") setQuery("");
            }}
            placeholder={t("navigation:nav.sidebarSearchPlaceholder")}
            aria-label={t("navigation:nav.sidebarSearchPlaceholder")}
            leftSection={<Search size={14} />}
            // Readable on the navy rail (see .railSearch in app-layout.module.css): merges with the
            // theme's own tonal input classes, only overriding the CSS variables they set.
            classNames={{ wrapper: classes.railSearch }}
            mb={4}
          />
        )}
        {renderedSections.map((section, index) => (
          <div key={section.key}>
            {index > 0 && <Divider my={4} />}
            <Stack gap={4}>
              {section.entries.map((entry) =>
                entry.kind === "group" ? (
                  collapsed ? (
                    <CollapsedNavGroup
                      key={entry.group.key}
                      group={entry.group}
                      items={entry.items}
                      isCurrent={entry.group.key === activeGroupKey}
                      t={t}
                    />
                  ) : (
                    <ExpandedNavGroup
                      key={entry.group.key}
                      group={entry.group}
                      items={entry.items}
                      opened={
                        entry.forceOpen ||
                        (openOverrides[entry.group.key] ?? entry.group.key === activeGroupKey)
                      }
                      onToggle={() => toggleGroup(entry.group.key)}
                      t={t}
                    />
                  )
                ) : entry.item.key === "workflowInbox" ? (
                  <WorkflowInboxNavItem
                    key={entry.item.key}
                    item={entry.item}
                    collapsed={collapsed}
                    label={t(entry.item.labelKey)}
                  />
                ) : (
                  <NavItemLink
                    key={entry.item.key}
                    item={entry.item}
                    collapsed={collapsed}
                    label={t(entry.item.labelKey)}
                  />
                )
              )}
            </Stack>
          </div>
        ))}
        {searching && !hasAnyVisibleEntry && (
          <Text size="sm" c="dimmed" ta="center" role="status" className="px-2 py-2">
            {t("navigation:search.nothing")}
          </Text>
        )}
      </Stack>
      <Stack component="nav" gap={4} className={`px-2 py-3 ${classes.footerNav}`}>
        {footerItems.map((item) => (
          <NavItemLink key={item.key} item={item} collapsed={collapsed} label={t(item.labelKey)} />
        ))}
      </Stack>
    </>
  );
}
