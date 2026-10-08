import { Link, useLocation, useNavigate } from "react-router";
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
  PanelLeftClose,
  PanelLeftOpen,
  Plus,
  Settings,
  ShieldAlert,
  UserCircle,
} from "lucide-react";
import { useAuthStore } from "@/store/auth.store";
import { useImpersonatorId } from "@/hooks/use-access-setting";
import { useVisibleItems } from "@/hooks/use-nav-visibility";
import { APP_LAUNCHER_ITEMS, NAV_ITEMS, navIconColor } from "@/config/navigation";
import { NotificationsMenu } from "@/components/notifications/notification-bell";
import RouteBoundary from "@/components/route-boundary";
import SetupGuard from "@/components/setup-guard";
import { CommercialLicenseBanner } from "@/components/commercial-license-banner";
import SettingsSubNav from "@/components/settings-subnav";
import ScreenSearch from "@/components/screen-search";
import ModuleRouteGuard from "@/components/module-disabled";
import { NavList } from "./sidebar-nav";
import classes from "./app-layout.module.css";

const ACTIVE_APPS = APP_LAUNCHER_ITEMS.filter((item) => item.phase === undefined);
const PLANNED_APPS = APP_LAUNCHER_ITEMS.filter((item) => item.phase !== undefined);
const SETTINGS_ITEMS = NAV_ITEMS.filter((item) => item.key === "settings");
// The hub is always reachable; show its shortcut only when an enabled request flow is available.
const CREATE_ITEMS = [
  { path: "/app/leave", permissions: ["leave.my.requests"] },
  { path: "/app/leave/overtimes", permissions: ["leave.my.overtimes"] },
  { path: "/app/payments", permissions: ["expense.my.claims"] },
  { path: "/app/payments", permissions: ["expense.my.advances"] },
];

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
        <ActionIcon
          variant="subtle" size={32} radius="sm" aria-label={label}
          className={classes.headerAction}
        >
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
            <Bell size={18} className="block" />
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
          <ActionIcon
            variant="subtle" size={32} radius="xl" aria-label={label}
            className={classes.headerAction}
          >
            <Avatar color="blue" variant="filled" radius="xl" size={28} className={classes.headerAvatar}>
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
          <div className="mt-2">
            <TenantBadge />
          </div>
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

/** Desktop navigation preference, controlled once from the top bar. */
function SidebarToggle({ collapsed, onToggle }: { collapsed: boolean; onToggle: () => void }) {
  const { t } = useTranslation(["navigation"]);
  const label = t(collapsed ? "navigation:nav.expandSidebar" : "navigation:nav.collapseSidebar");
  const Icon = collapsed ? PanelLeftOpen : PanelLeftClose;

  return (
    <Tooltip label={label} position="bottom" openDelay={300}>
      <ActionIcon
        variant="subtle"
        size={32}
        radius="sm"
        aria-label={label}
        onClick={onToggle}
        aria-expanded={!collapsed}
        aria-controls="desktop-navigation"
        className={`${classes.headerAction} ${classes.sidebarToggle}`}
      >
        <Icon size={18} />
      </ActionIcon>
    </Tooltip>
  );
}

/** Compact app launcher beside the brand. */
function AppsLauncher() {
  const { t } = useTranslation(["navigation"]);
  const allowed = useVisibleItems(ACTIVE_APPS);

  return (
    <Menu position="bottom-start" width={320}>
      <Tooltip label={t("navigation:nav.apps")}>
        <Menu.Target>
          <ActionIcon
            variant="subtle" size={32} radius="sm" aria-label={t("navigation:nav.apps")}
            className={classes.headerAction}
          >
            <Grip size={18} />
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

/** Use the existing request hub so choosing a flow mounts its page and opens its dialog. */
function CreateButton() {
  const { t } = useTranslation(["navigation"]);
  const allowed = useVisibleItems(CREATE_ITEMS);
  if (allowed.length === 0) return null;

  return (
    <Button
      component={Link} to="/app/requests"
      size="compact-sm" radius="sm" leftSection={<Plus size={16} />}
      aria-label={t("navigation:create.label")} className={classes.createButton}
    >
      <span className={classes.createLabel}>{t("navigation:create.label")}</span>
    </Button>
  );
}

function SettingsButton() {
  const { t } = useTranslation(["navigation"]);
  const allowed = useVisibleItems(SETTINGS_ITEMS);
  if (allowed.length === 0) return null;
  const item = allowed[0];
  const label = t(item.labelKey);
  return (
    <Tooltip label={label} openDelay={300}>
      <ActionIcon
        component={Link} to={item.path} variant="subtle" size={32} radius="sm"
        aria-label={label} className={classes.headerAction}
      >
        <Settings size={18} />
      </ActionIcon>
    </Tooltip>
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

/** Product identity stays visible independently of the navigation width. */
function Brand() {
  return (
    <Link to="/app" className={classes.brand} aria-label="SenseHR">
      <svg
        width="22" height="22" viewBox="0 0 24 24" fill="currentColor"
        aria-hidden="true" className={classes.brandMark}
      >
        <rect x="2" y="2" width="13" height="13" rx="4" />
        <rect x="10" y="10" width="12" height="12" rx="4" opacity="0.65" />
      </svg>
      <Text
        component="span"
        fz={16}
        fw={750}
        lh={1}
        ff="var(--mantine-font-family-headings)"
        style={{ letterSpacing: "-0.02em" }}
        className={classes.brandName}
      >
        SenseHR
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
      <ActionIcon
        variant="subtle" size={32} radius="sm" aria-label={label}
        onClick={handleBack} className={classes.headerAction}
      >
        <ArrowLeft size={18} />
      </ActionIcon>
    </Tooltip>
  );
}

/** Company the current session is logged in to, shown inside the profile menu. */
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

/**
 * Main application shell: compact top bar, bordered left navigation, and continuous content.
 */
export default function AppLayout() {
  // Desktop-only preference - the mobile rail below is always icon-only regardless of this value.
  const [collapsed, setCollapsed] = useLocalStorage<boolean>({
    key: SIDEBAR_COLLAPSED_KEY,
    defaultValue: false,
  });

  return (
    <Box className={`flex h-screen flex-col ${classes.page}`} c="var(--mantine-color-text)">
      <ImpersonationBanner />
      <div className="flex min-h-0 flex-1">
        <div className={`flex min-h-0 flex-1 flex-col ${classes.portal}`}>
          <header className={classes.topbar}>
            <div className={classes.headerStart}>
              <SidebarToggle
                collapsed={collapsed} onToggle={() => setCollapsed((current) => !current)}
              />
              <AppsLauncher />
              <Brand />
              <BackButton />
            </div>
            <div className={classes.headerSearch}>
              <ScreenSearch classNames={{ input: classes.searchInput, section: classes.searchSection }} />
            </div>
            <div className={classes.headerEnd}>
              <CreateButton />
              <NotificationsButton />
              <SettingsButton />
              <UserMenu />
            </div>
          </header>

          <div className="flex min-h-0 flex-1">
            {/* Sidebar - full labels from md up, user can collapse to icon-only via SidebarToggle */}
            <div className="relative hidden shrink-0 md:flex">
              <aside
                id="desktop-navigation"
                data-testid="sidebar-desktop"
                className={`${collapsed ? "w-14" : "w-56"} flex shrink-0 flex-col ${classes.rail}`}
              >
                <NavList collapsed={collapsed} />
              </aside>
            </div>

            {/* Mobile icon rail - always icon-only, unaffected by the desktop collapse preference */}
            <aside
              data-testid="sidebar-mobile"
              className={`flex w-14 shrink-0 flex-col md:hidden ${classes.rail}`}
            >
              <NavList collapsed />
            </aside>

            {/* Ayar alt sayfalarında: gruplanmış ikinci ayarlar menüsü */}
            <SettingsSubNav />

            <main className="min-w-0 flex-1 overflow-auto">
              {/* Keeps the sidebar and header on screen while the next page's chunk loads. */}
              <RouteBoundary>
                <ModuleRouteGuard>
                  <CommercialLicenseBanner />
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
