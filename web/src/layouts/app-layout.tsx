import { Link, useLocation, useNavigate } from "react-router";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useLocalStorage } from "@mantine/hooks";
import {
  ActionIcon,
  Alert,
  Avatar,
  Badge,
  Box,
  Button,
  Group,
  Indicator,
  Menu,
  Stack,
  Text,
  Tooltip,
} from "@mantine/core";
import {
  ArrowLeft,
  Bell,
  Building2,
  Grip,
  LogOut,
  ChevronLeft,
  ChevronRight,
  ShieldAlert,
  UserCircle,
} from "lucide-react";
import { useAuthStore } from "@/store/auth.store";
import { useImpersonatorId } from "@/hooks/use-access-setting";
import { useVisibleItems } from "@/hooks/use-nav-visibility";
import { APP_LAUNCHER_ITEMS, navIconColor } from "@/config/navigation";
import { NotificationsMenu } from "@/components/notifications/notification-bell";
import RouteBoundary from "@/components/route-boundary";
import SetupGuard from "@/components/setup-guard";
import SettingsSubNav from "@/components/settings-subnav";
import ScreenSearch from "@/components/screen-search";
import ModuleRouteGuard from "@/components/module-disabled";
import { NavList } from "./sidebar-nav";
import classes from "./app-layout.module.css";

const ACTIVE_APPS = APP_LAUNCHER_ITEMS.filter((item) => item.phase === undefined);
const PLANNED_APPS = APP_LAUNCHER_ITEMS.filter((item) => item.phase !== undefined);

/** Per-viewer convenience, not shared state - remembered locally, never sent to the server. */
const SIDEBAR_COLLAPSED_KEY = "senseik.sidebar.collapsed";

/** Bell at the top right: unread count badge, dropdown with the latest notifications opens below it. */
function NotificationsButton() {
  const { t } = useTranslation(["notifications"]);
  const label = t("notifications:bell.label");

  return (
    <NotificationsMenu
      position="bottom-end"
      tooltip={label}
      tooltipPosition="bottom"
      renderTarget={(unread) => (
        <ActionIcon variant="default" size={40} radius="md" aria-label={label}>
          <Indicator
            color="red"
            size={16}
            offset={2}
            label={unread}
            maxValue={99}
            disabled={unread === 0}
            styles={{ indicator: { fontSize: 10, fontWeight: 600, paddingInline: 4 } }}
            inline
          >
            <Bell size={20} className="block" />
          </Indicator>
        </ActionIcon>
      )}
    />
  );
}

function initialsOf(user: { firstName?: string; lastName?: string; name: string }): string {
  const parts = [user.firstName, user.lastName].filter(Boolean) as string[];
  const source = parts.length > 0 ? parts : user.name.split(/\s+/).filter(Boolean);
  return source
    .slice(0, 2)
    .map((part) => part.charAt(0).toLocaleUpperCase())
    .join("");
}

/** Avatar menu at the top right: who is signed in, link to the profile page and sign out. */
function UserMenu() {
  const { t } = useTranslation(["navigation", "common"]);
  const user = useAuthStore((state) => state.user);
  const logout = useAuthStore((state) => state.logout);

  if (!user) return null;

  const label = t("navigation:nav.account");

  return (
    <Menu position="bottom-end" width={240}>
      <Tooltip label={label} openDelay={300}>
        <Menu.Target>
          <ActionIcon variant="transparent" size={40} radius="xl" aria-label={label}>
            <Avatar color="navy" radius="xl" size={40}>
              {initialsOf(user)}
            </Avatar>
          </ActionIcon>
        </Menu.Target>
      </Tooltip>
      <Menu.Dropdown>
        <div className="px-3 py-2">
          <Text size="sm" fw={600} truncate>
            {user.name}
          </Text>
          <Text size="xs" c="dimmed" truncate>
            {user.email}
          </Text>
        </div>
        <Menu.Divider />
        <Menu.Item component={Link} to="/app/account" leftSection={<UserCircle size={16} />}>
          {label}
        </Menu.Item>
        <Menu.Item color="red" leftSection={<LogOut size={16} />} onClick={logout}>
          {t("common:logout")}
        </Menu.Item>
      </Menu.Dropdown>
    </Menu>
  );
}

/**
 * Collapse/expand control on the seam between the sidebar and the content. It stays invisible until the pointer
 * nears the top of that seam (or the button gets keyboard focus), so it does not clutter the menu.
 */
function SidebarEdgeToggle({ collapsed, onToggle }: { collapsed: boolean; onToggle: () => void }) {
  const { t } = useTranslation(["navigation"]);
  const label = t(collapsed ? "navigation:nav.expandSidebar" : "navigation:nav.collapseSidebar");
  const Icon = collapsed ? ChevronRight : ChevronLeft;

  return (
    <div className="group/edge absolute top-0 -right-4 z-20 h-16 w-9">
      <Tooltip label={label} position="right" openDelay={300}>
        <ActionIcon
          variant="default"
          size={26}
          radius="xl"
          aria-label={label}
          onClick={onToggle}
          className="absolute top-3 left-1/2 -translate-x-1/2 opacity-70 shadow-sm transition-opacity hover:opacity-100 group-hover/edge:opacity-100 focus-visible:opacity-100"
        >
          <Icon size={16} />
        </ActionIcon>
      </Tooltip>
    </div>
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
    <Alert
      color="yellow"
      variant="light"
      radius={0}
      py={8}
      icon={<ShieldAlert size={16} />}
      className="shrink-0"
    >
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
    <Link
      to="/app"
      className={`flex w-14 shrink-0 items-center justify-center md:w-64 md:justify-start md:px-5 ${classes.brand}`}
    >
      <Text fz={22} fw={700} lh={1} className="hidden md:block">
        SenseHR
      </Text>
      <Text fz={22} fw={700} lh={1} className="md:hidden">
        S
      </Text>
    </Link>
  );
}

const HOME_PATH = "/app";

/**
 * Top-bar "back" control, hidden on the home page. It steps back in the browser history; when the page was
 * opened directly (router key "default", nothing to go back to inside the app) it goes home instead.
 */
function BackButton() {
  const { t } = useTranslation(["navigation"]);
  const navigate = useNavigate();
  const location = useLocation();

  if (location.pathname.replace(/\/+$/, "") === HOME_PATH) return null;

  const label = t("navigation:nav.back");
  const handleBack = () => {
    if (location.key === "default") navigate(HOME_PATH);
    else navigate(-1);
  };

  return (
    <Tooltip label={label}>
      <ActionIcon variant="default" size={40} radius="md" aria-label={label} onClick={handleBack}>
        <ArrowLeft size={20} />
      </ActionIcon>
    </Tooltip>
  );
}

/** Company the current session is logged in to (tenant name from /me), shown beside the apps launcher. */
function TenantBadge() {
  const tenantName = useAuthStore((state) => state.user?.tenant?.name);

  if (!tenantName) return null;

  return (
    <Tooltip label={tenantName} openDelay={300}>
      <Group gap={8} wrap="nowrap" data-testid="tenant-badge" className="min-w-0 max-w-56 shrink">
        <Building2 size={18} className="shrink-0" color="var(--mantine-color-dimmed)" aria-hidden />
        <Text size="sm" fw={600} truncate>
          {tenantName}
        </Text>
      </Group>
    </Tooltip>
  );
}

/** Greeting at the start of the top bar: name, then today's date (Kolay İK reference layout). */
function Greeting() {
  const { t, i18n } = useTranslation(["home"]);
  const user = useAuthStore((state) => state.user);

  // Computed once per mount: reading the clock during render is impure (react-hooks/purity).
  const [today] = useState(() => new Date());
  const dateLabel = new Intl.DateTimeFormat(i18n.language, {
    day: "numeric",
    month: "long",
    year: "numeric",
  }).format(today);
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
  // Desktop-only preference - the mobile rail below is always icon-only regardless of this value.
  const [collapsed, setCollapsed] = useLocalStorage<boolean>({
    key: SIDEBAR_COLLAPSED_KEY,
    defaultValue: false,
  });

  return (
    <Box
      className="flex h-screen flex-col"
      bg="var(--mantine-color-body)"
      c="var(--mantine-color-text)"
    >
      <ImpersonationBanner />
      <div className="flex min-h-0 flex-1 p-2 md:p-4">
        <div className={`flex min-h-0 flex-1 flex-col ${classes.portal}`}>
          <header className={`flex h-18 shrink-0 items-stretch ${classes.header}`}>
            <Brand />
            <div className="flex min-w-0 flex-1 items-center justify-end gap-3 px-4 md:px-6">
              <BackButton />
              <Greeting />
              <div className="hidden min-w-0 flex-1 sm:block md:max-w-sm">
                <ScreenSearch />
              </div>
              <TenantBadge />
              <AppsLauncher />
              <NotificationsButton />
              <UserMenu />
            </div>
          </header>

          <div className="flex min-h-0 flex-1">
            {/* Sidebar - full labels from md up, user can collapse to icon-only via SidebarToggle */}
            <div className="relative hidden shrink-0 md:flex">
              <aside
                data-testid="sidebar-desktop"
                className={`${collapsed ? "w-14" : "w-64"} flex shrink-0 flex-col ${classes.sidebar}`}
              >
                <NavList collapsed={collapsed} />
              </aside>
              <SidebarEdgeToggle
                collapsed={collapsed}
                onToggle={() => setCollapsed((current) => !current)}
              />
            </div>

            {/* Mobile icon rail - always icon-only, unaffected by the desktop collapse preference */}
            <aside
              data-testid="sidebar-mobile"
              className={`flex w-14 shrink-0 flex-col md:hidden ${classes.sidebar}`}
            >
              <NavList collapsed />
            </aside>

            {/* Ayar alt sayfalarında: gruplanmış ikinci ayarlar menüsü */}
            <SettingsSubNav />

            <main className="min-w-0 flex-1 overflow-auto">
              {/* Keeps the sidebar and header on screen while the next page's chunk loads. */}
              <RouteBoundary>
                <ModuleRouteGuard>
                  <SetupGuard />
                </ModuleRouteGuard>
              </RouteBoundary>
            </main>
          </div>
        </div>
      </div>
    </Box>
  );
}
