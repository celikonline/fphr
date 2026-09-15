import { Link, NavLink as RouterNavLink, Outlet } from "react-router";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { ActionIcon, Alert, Badge, Box, Button, Group, Menu, NavLink, Stack, Text, Tooltip } from "@mantine/core";
import { Grip, ShieldAlert } from "lucide-react";
import { useAuthStore } from "@/store/auth.store";
import { useImpersonatorId } from "@/hooks/use-access-setting";
import { useVisibleItems } from "@/hooks/use-nav-visibility";
import { APP_LAUNCHER_ITEMS, NAV_FOOTER_ITEMS, NAV_ITEMS, navIconColor, type NavItem } from "@/config/navigation";
import { NotificationBell } from "@/components/notifications/notification-bell";
import classes from "./app-layout.module.css";

const ACTIVE_APPS = APP_LAUNCHER_ITEMS.filter((item) => item.phase === undefined);
const PLANNED_APPS = APP_LAUNCHER_ITEMS.filter((item) => item.phase !== undefined);

function NavList({ collapsed }: { collapsed: boolean }) {
  const { t } = useTranslation(["navigation"]);
  const items = useVisibleItems(NAV_ITEMS);
  const footerItems = useVisibleItems(NAV_FOOTER_ITEMS);

  return (
    <>
      <Stack component="nav" gap={4} className="flex-1 overflow-y-auto px-2 py-4">
        {items.map((item) => (
          <NavItemLink key={item.key} item={item} collapsed={collapsed} label={t(item.labelKey)} />
        ))}
      </Stack>
      <Stack component="nav" gap={4} className={`px-2 py-3 ${classes.footerNav}`}>
        {footerItems.map((item) => (
          <NavItemLink key={item.key} item={item} collapsed={collapsed} label={t(item.labelKey)} />
        ))}
      </Stack>
    </>
  );
}

const NAV_LINK_CLASS_NAMES = { root: classes.navLink, section: classes.navSection, body: classes.navBody };

function NavItemLink({ item, collapsed, label }: { item: NavItem; collapsed: boolean; label: string }) {
  const { t } = useTranslation(["navigation"]);
  const Icon = item.icon;

  // React Router sets aria-current="page" on the matching link, which Mantine's NavLink styles as active.
  const link = (
    <NavLink
      component={RouterNavLink}
      to={item.path}
      end={item.end}
      variant="light"
      aria-label={collapsed ? label : undefined}
      mod={{ collapsed }}
      classNames={NAV_LINK_CLASS_NAMES}
      styles={{ section: { color: navIconColor(item) } }}
      leftSection={<Icon size={20} />}
      label={collapsed ? undefined : label}
      noWrap
      rightSection={
        !collapsed && item.phase !== undefined ? (
          <Badge size="xs" variant="light" color="gray" tt="none">
            {t("navigation:phase", { phase: item.phase })}
          </Badge>
        ) : undefined
      }
    />
  );

  if (!collapsed) return link;

  return (
    <Tooltip label={label} position="right" openDelay={300}>
      {link}
    </Tooltip>
  );
}

/** The filled square launcher at the end of the top bar (Kolay İK reference layout). */
function AppsLauncher() {
  const { t } = useTranslation(["navigation"]);
  const allowed = useVisibleItems(ACTIVE_APPS);

  return (
    <Menu position="bottom-end" width={320}>
      <Tooltip label={t("navigation:nav.apps")}>
        <Menu.Target>
          <ActionIcon variant="filled" size={40} radius="md" aria-label={t("navigation:nav.apps")}>
            <Grip size={20} />
          </ActionIcon>
        </Menu.Target>
      </Tooltip>
      <Menu.Dropdown p="xs">
        <Menu.Label>{t("navigation:nav.apps")}</Menu.Label>
        <Menu.Divider />
        <div className="grid grid-cols-3 gap-1">
          {allowed.map((item) => {
            const Icon = item.icon;
            return (
              <Menu.Item key={item.key} component={Link} to={item.path} p="xs">
                <Stack gap={4} align="center" ta="center">
                  <Icon size={20} color={navIconColor(item)} />
                  <Text size="xs" lh={1.2}>
                    {t(item.labelKey)}
                  </Text>
                </Stack>
              </Menu.Item>
            );
          })}
          {PLANNED_APPS.map((item) => {
            const Icon = item.icon;
            return (
              <Menu.Item key={item.key} disabled p="xs">
                <Stack gap={4} align="center" ta="center">
                  <Icon size={20} color="var(--mantine-color-dimmed)" />
                  <Text size="xs" lh={1.2}>
                    {t(item.labelKey)}
                  </Text>
                  <Badge size="xs" variant="outline" color="gray" tt="none">
                    {t("navigation:phase", { phase: item.phase })}
                  </Badge>
                </Stack>
              </Menu.Item>
            );
          })}
        </div>
        <Menu.Divider />
        <Menu.Item component={Link} to="/app/apps">
          <Text size="sm" ta="center">
            {t("navigation:apps.viewAll")}
          </Text>
        </Menu.Item>
      </Menu.Dropdown>
    </Menu>
  );
}

/** Persistent warning while the session runs under a support-access (impersonation) grant. */
function ImpersonationBanner() {
  const { t } = useTranslation(["common"]);
  const impersonatorId = useImpersonatorId();
  const logout = useAuthStore((state) => state.logout);

  if (!impersonatorId) return null;

  return (
    <Alert color="yellow" variant="light" radius={0} py={8} icon={<ShieldAlert size={16} />} className="shrink-0">
      <Group justify="space-between" gap="sm" wrap="nowrap">
        <Text size="sm">{t("common:impersonation.banner")}</Text>
        <Button size="xs" variant="default" onClick={logout}>
          {t("common:impersonation.end")}
        </Button>
      </Group>
    </Alert>
  );
}

/** Wordmark heading the sidebar column, sharing its width and background (Kolay İK reference layout). */
function Brand() {
  return (
    <Link to="/app" className={`flex w-14 shrink-0 items-center justify-center md:w-52 md:justify-start md:px-5 ${classes.brand}`}>
      <Text fz={22} fw={700} lh={1} className="hidden md:block">
        SenseİK
      </Text>
      <Text fz={22} fw={700} lh={1} className="md:hidden">
        S
      </Text>
    </Link>
  );
}

/** Greeting at the start of the top bar: name, then today's date (Kolay İK reference layout). */
function Greeting() {
  const { t, i18n } = useTranslation(["home"]);
  const user = useAuthStore((state) => state.user);

  // Computed once per mount: reading the clock during render is impure (react-hooks/purity).
  const [today] = useState(() => new Date());
  const dateLabel = new Intl.DateTimeFormat(i18n.language, { day: "numeric", month: "long", year: "numeric" }).format(today);
  const weekdayLabel = new Intl.DateTimeFormat(i18n.language, { weekday: "long" }).format(today);

  return (
    <div className="hidden min-w-0 flex-1 sm:block">
      <Text size="sm" c="dimmed" truncate>
        {t("home:greeting", { name: user?.firstName || user?.name || "" })}
      </Text>
      <Text fz={20} fw={500} lh={1.3} truncate>
        <Text span inherit fw={400} c="dimmed">
          {t("home:today")}
        </Text>{" "}
        {dateLabel}, {weekdayLabel}
      </Text>
    </div>
  );
}

/**
 * Main application shell, framed as a rounded panel on the page: top bar (wordmark over the sidebar
 * column, greeting, apps launcher) + single left sidebar (Kolay İK order) + content.
 */
export default function AppLayout() {
  return (
    <Box className="flex h-screen flex-col" bg="var(--mantine-color-body)" c="var(--mantine-color-text)">
      <ImpersonationBanner />
      <div className="flex min-h-0 flex-1 p-2 md:p-4">
        <div className={`flex min-h-0 flex-1 flex-col ${classes.portal}`}>
          <header className={`flex h-18 shrink-0 items-stretch ${classes.header}`}>
            <Brand />
            <div className="flex min-w-0 flex-1 items-center justify-end gap-3 px-4 md:px-6">
              <Greeting />
              <NotificationBell />
              <AppsLauncher />
            </div>
          </header>

          <div className="flex min-h-0 flex-1">
            {/* Sidebar - full labels from md up */}
            <aside className={`hidden w-52 shrink-0 flex-col md:flex ${classes.sidebar}`}>
              <NavList collapsed={false} />
            </aside>

            {/* Mobile icon rail - always icon-only */}
            <aside className={`flex w-14 shrink-0 flex-col md:hidden ${classes.sidebar}`}>
              <NavList collapsed />
            </aside>

            <main className="min-w-0 flex-1 overflow-auto">
              <Outlet />
            </main>
          </div>
        </div>
      </div>
    </Box>
  );
}
