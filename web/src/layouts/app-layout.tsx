import { Link, NavLink as RouterNavLink, Outlet, useLocation } from "react-router";
import { useTranslation } from "react-i18next";
import {
  ActionIcon,
  Alert,
  Anchor,
  Avatar,
  Badge,
  Box,
  Breadcrumbs,
  Button,
  Group,
  Menu,
  NavLink,
  Stack,
  Text,
  Tooltip,
  UnstyledButton,
} from "@mantine/core";
import {
  ChevronRight,
  Globe,
  LayoutGrid,
  LogOut,
  Menu as MenuIcon,
  Monitor,
  Moon,
  MoreHorizontal,
  ShieldAlert,
  Sun,
  User as UserIcon,
} from "lucide-react";
import { useUIStore } from "@/store/ui.store";
import { useAuthStore } from "@/store/auth.store";
import { useTenantStore } from "@/store/tenant.store";
import { useImpersonatorId } from "@/hooks/use-access-setting";
import { useVisibleItems } from "@/hooks/use-nav-visibility";
import {
  APP_LAUNCHER_ITEMS,
  NAV_FOOTER_ITEMS,
  NAV_ITEMS,
  resolveBreadcrumbs,
  type NavItem,
} from "@/config/navigation";
import { NotificationBell } from "@/components/notifications/notification-bell";
import { avatarUrl } from "@/components/employees/employee-badges";
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
      variant="filled"
      aria-label={collapsed ? label : undefined}
      mod={{ collapsed }}
      classNames={NAV_LINK_CLASS_NAMES}
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

type Crumb = { path: string; label: string; link: boolean };

function crumbNode(item: Crumb, isLast: boolean) {
  if (isLast) {
    return (
      <Text key={item.path} size="sm" fw={600} lh={1.5} aria-current="page" truncate>
        {item.label}
      </Text>
    );
  }
  if (item.link) {
    return (
      <Anchor key={item.path} component={Link} to={item.path} size="sm" c="dimmed" underline="hover">
        {item.label}
      </Anchor>
    );
  }
  return (
    <Text key={item.path} size="sm" c="dimmed">
      {item.label}
    </Text>
  );
}

function HeaderBreadcrumbs() {
  const { t } = useTranslation(["navigation"]);
  const location = useLocation();
  const crumbs = resolveBreadcrumbs(location.pathname);
  const items: Crumb[] = [
    { path: "/app", label: t("nav.home"), link: true },
    ...crumbs.map((crumb) => ({ path: crumb.path, label: t(crumb.labelKey), link: crumb.link })),
  ];
  const separator = <ChevronRight size={14} color="var(--mantine-color-dimmed)" aria-hidden="true" />;
  const full = (
    <Breadcrumbs separator={separator} separatorMargin={6} className="flex-nowrap">
      {items.map((item, index) => crumbNode(item, index === items.length - 1))}
    </Breadcrumbs>
  );

  // Below md, keep the first and last two crumbs and fold the middle ones into an ellipsis.
  if (items.length <= 3) return full;

  const middleLabel = items
    .slice(1, -2)
    .map((item) => item.label)
    .join(" / ");
  const tail = items.slice(-2);

  return (
    <>
      <div className="hidden md:block">{full}</div>
      <div className="md:hidden">
        <Breadcrumbs separator={separator} separatorMargin={6} className="flex-nowrap">
          {crumbNode(items[0], false)}
          <Text key="ellipsis" span c="dimmed" title={middleLabel} aria-label={middleLabel} className="flex items-center">
            <MoreHorizontal size={16} aria-hidden="true" />
          </Text>
          {tail.map((item, index) => crumbNode(item, index === tail.length - 1))}
        </Breadcrumbs>
      </div>
    </>
  );
}

function HeaderIconMenuTarget({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <Tooltip label={label}>
      <Menu.Target>
        <ActionIcon variant="subtle" color="gray" size="lg" aria-label={label}>
          {children}
        </ActionIcon>
      </Menu.Target>
    </Tooltip>
  );
}

function AppsLauncher() {
  const { t } = useTranslation(["navigation"]);
  const allowed = useVisibleItems(ACTIVE_APPS);

  return (
    <Menu position="bottom-end" width={320}>
      <HeaderIconMenuTarget label={t("navigation:nav.apps")}>
        <LayoutGrid size={16} />
      </HeaderIconMenuTarget>
      <Menu.Dropdown p="xs">
        <Menu.Label>{t("navigation:nav.apps")}</Menu.Label>
        <Menu.Divider />
        <div className="grid grid-cols-3 gap-1">
          {allowed.map((item) => {
            const Icon = item.icon;
            return (
              <Menu.Item key={item.key} component={Link} to={item.path} p="xs">
                <Stack gap={4} align="center" ta="center">
                  <Icon size={20} color="var(--mantine-primary-color-filled)" />
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

function ThemeToggle() {
  const { t } = useTranslation(["common"]);
  const theme = useUIStore((state) => state.theme);
  const setTheme = useUIStore((state) => state.setTheme);

  const icon = theme === "dark" ? <Moon size={16} /> : theme === "light" ? <Sun size={16} /> : <Monitor size={16} />;

  return (
    <Menu position="bottom-end">
      <HeaderIconMenuTarget label={t("common:theme")}>{icon}</HeaderIconMenuTarget>
      <Menu.Dropdown>
        <Menu.Item leftSection={<Sun size={16} />} onClick={() => setTheme("light")}>
          {t("common:themeLight")}
        </Menu.Item>
        <Menu.Item leftSection={<Moon size={16} />} onClick={() => setTheme("dark")}>
          {t("common:themeDark")}
        </Menu.Item>
        <Menu.Item leftSection={<Monitor size={16} />} onClick={() => setTheme("system")}>
          {t("common:themeSystem")}
        </Menu.Item>
      </Menu.Dropdown>
    </Menu>
  );
}

function LanguageSwitcher() {
  const { t, i18n } = useTranslation(["common"]);

  return (
    <Menu position="bottom-end">
      <HeaderIconMenuTarget label={t("common:language")}>
        <Globe size={16} />
      </HeaderIconMenuTarget>
      <Menu.Dropdown>
        <Menu.Item onClick={() => i18n.changeLanguage("tr")}>Türkçe</Menu.Item>
        <Menu.Item onClick={() => i18n.changeLanguage("en")}>English</Menu.Item>
        <Menu.Item onClick={() => i18n.changeLanguage("de")}>Deutsch</Menu.Item>
      </Menu.Dropdown>
    </Menu>
  );
}

function UserMenu() {
  const { t } = useTranslation(["common", "navigation"]);
  const user = useAuthStore((state) => state.user);
  const logout = useAuthStore((state) => state.logout);
  const initials = (user?.name || user?.email || "?")
    .split(" ")
    .map((part) => part[0])
    .join("")
    .slice(0, 2)
    .toUpperCase();

  return (
    <Menu position="bottom-end" width={220}>
      <Menu.Target>
        <UnstyledButton aria-label={t("navigation:nav.account")} p={4} pr={8} bdrs="md" className={classes.userButton}>
          <Group gap="xs" wrap="nowrap">
            <Avatar size={32} radius="xl" src={user?.id ? avatarUrl(user.id) : undefined}>
              {initials}
            </Avatar>
            <Text size="sm" fw={500} truncate className="hidden max-w-[10rem] sm:block">
              {user?.name || user?.email}
            </Text>
          </Group>
        </UnstyledButton>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Label>
          <Text size="xs" c="dimmed" fw={500} truncate>
            {user?.email}
          </Text>
        </Menu.Label>
        <Menu.Divider />
        <Menu.Item component={Link} to="/app/account" leftSection={<UserIcon size={16} />}>
          {t("navigation:nav.account")}
        </Menu.Item>
        <Menu.Item color="red" leftSection={<LogOut size={16} />} onClick={logout}>
          {t("common:logout")}
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

/**
 * Main application shell: single left sidebar (Kolay İK order) + header (breadcrumbs, apps launcher) + content.
 */
export default function AppLayout() {
  const { t } = useTranslation(["navigation"]);
  const sidebarOpen = useUIStore((state) => state.sidebarOpen);
  const toggleSidebar = useUIStore((state) => state.toggleSidebar);
  const tenant = useTenantStore((state) => state.currentTenant);
  const userTenantName = useAuthStore((state) => state.user?.tenant?.name);

  return (
    <Box className="flex h-screen flex-col" bg="var(--mantine-color-body)" c="var(--mantine-color-text)">
      <ImpersonationBanner />
      <div className="flex min-h-0 flex-1">
        {/* Sidebar - collapses to an icon rail below md, or when toggled */}
        <aside className={`hidden shrink-0 flex-col md:flex ${sidebarOpen ? "w-64" : "w-16"} ${classes.sidebar}`}>
          <div className={`flex h-16 items-center ${sidebarOpen ? "px-4" : "justify-center px-2"} ${classes.brandRow}`}>
            <Anchor component={Link} to="/app" size="lg" fw={700} underline="never">
              {sidebarOpen ? "SenseİK" : "S"}
            </Anchor>
          </div>
          <NavList collapsed={!sidebarOpen} />
        </aside>

        {/* Mobile icon rail - always icon-only, no store dependency */}
        <aside className={`flex w-14 shrink-0 flex-col md:hidden ${classes.sidebar}`}>
          <div className={`flex h-16 items-center justify-center ${classes.brandRow}`}>
            <Text span size="lg" fw={700} c="var(--mantine-primary-color-filled)">
              S
            </Text>
          </div>
          <NavList collapsed />
        </aside>

        <div className="flex flex-1 flex-col overflow-hidden">
          <header className={`flex h-16 shrink-0 items-center justify-between px-4 md:px-6 ${classes.header}`}>
            <div className="flex min-w-0 items-center gap-3">
              <Tooltip label={t("navigation:nav.toggleSidebar")}>
                <ActionIcon
                  variant="subtle"
                  color="gray"
                  size="lg"
                  onClick={toggleSidebar}
                  aria-label={t("navigation:nav.toggleSidebar")}
                  className="hidden md:inline-flex"
                >
                  <MenuIcon size={20} />
                </ActionIcon>
              </Tooltip>
              <div className="hidden min-w-0 flex-col sm:flex">
                <HeaderBreadcrumbs />
                <Text size="xs" c="dimmed" truncate>
                  {tenant?.name ?? userTenantName ?? "SenseİK"}
                </Text>
              </div>
            </div>

            <Group gap={4} wrap="nowrap">
              <AppsLauncher />
              <NotificationBell />
              <LanguageSwitcher />
              <ThemeToggle />
              <UserMenu />
            </Group>
          </header>

          <main className="flex-1 overflow-auto">
            <Outlet />
          </main>
        </div>
      </div>
    </Box>
  );
}
