import { Link, NavLink, Outlet, useLocation } from "react-router";
import { useTranslation } from "react-i18next";
import {
  ActionIcon,
  Alert,
  Anchor,
  Avatar,
  Badge,
  Breadcrumbs,
  Button,
  Group,
  Menu,
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
import { cn } from "@/lib/utils";
import { NotificationBell } from "@/components/notifications/notification-bell";

const ACTIVE_APPS = APP_LAUNCHER_ITEMS.filter((item) => item.phase === undefined);
const PLANNED_APPS = APP_LAUNCHER_ITEMS.filter((item) => item.phase !== undefined);

function NavList({ collapsed }: { collapsed: boolean }) {
  const { t } = useTranslation(["navigation"]);
  const items = useVisibleItems(NAV_ITEMS);
  const footerItems = useVisibleItems(NAV_FOOTER_ITEMS);

  return (
    <>
      <nav className="flex-1 space-y-1 overflow-y-auto px-2 py-4">
        {items.map((item) => (
          <NavItemLink key={item.key} item={item} collapsed={collapsed} label={t(item.labelKey)} />
        ))}
      </nav>
      <nav className="space-y-1 border-t border-border px-2 py-3">
        {footerItems.map((item) => (
          <NavItemLink key={item.key} item={item} collapsed={collapsed} label={t(item.labelKey)} />
        ))}
      </nav>
    </>
  );
}

function NavItemLink({ item, collapsed, label }: { item: NavItem; collapsed: boolean; label: string }) {
  const { t } = useTranslation(["navigation"]);
  const Icon = item.icon;

  const link = (
    <NavLink
      to={item.path}
      end={item.end}
      aria-label={collapsed ? label : undefined}
      className={({ isActive }) =>
        cn(
          "flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors",
          "hover:bg-accent hover:text-accent-foreground",
          isActive ? "bg-primary text-primary-foreground hover:bg-primary/90 hover:text-primary-foreground" : "text-foreground/80",
          collapsed && "justify-center px-2"
        )
      }
    >
      <Icon className="h-5 w-5 shrink-0" />
      {!collapsed && (
        <>
          <span className="flex-1 truncate">{label}</span>
          {item.phase !== undefined && (
            <Badge size="xs" variant="light" color="gray" tt="none">
              {t("navigation:phase", { phase: item.phase })}
            </Badge>
          )}
        </>
      )}
    </NavLink>
  );

  if (!collapsed) return link;

  // Tooltip merges `className` as a string, which would drop NavLink's active-state class function; wrap instead.
  return (
    <Tooltip label={label} position="right" openDelay={300}>
      <div>{link}</div>
    </Tooltip>
  );
}

type Crumb = { path: string; label: string; link: boolean };

function crumbNode(item: Crumb, isLast: boolean) {
  if (isLast) {
    return (
      <Text key={item.path} size="sm" fw={500} aria-current="page" truncate>
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
        <UnstyledButton aria-label={t("navigation:nav.account")} p={4} pr={8} bdrs="md" className="hover:bg-accent">
          <Group gap="xs" wrap="nowrap">
            <Avatar size={32} radius="xl">
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
    <div className="flex h-screen flex-col bg-background text-foreground">
      <ImpersonationBanner />
      <div className="flex min-h-0 flex-1">
        {/* Sidebar - collapses to an icon rail below md, or when toggled */}
        <aside
          className={cn(
            "hidden shrink-0 flex-col border-r border-border bg-card transition-all duration-300 md:flex",
            sidebarOpen ? "w-64" : "w-16"
          )}
        >
          <div className={cn("flex h-16 items-center border-b border-border px-4", !sidebarOpen && "justify-center px-2")}>
            <Anchor component={Link} to="/app" size="lg" fw={700} underline="never">
              {sidebarOpen ? "SenseİK" : "S"}
            </Anchor>
          </div>
          <NavList collapsed={!sidebarOpen} />
        </aside>

        {/* Mobile icon rail - always icon-only, no store dependency */}
        <aside className="flex w-14 shrink-0 flex-col border-r border-border bg-card md:hidden">
          <div className="flex h-16 items-center justify-center border-b border-border">
            <Text span size="lg" fw={700} c="var(--mantine-primary-color-filled)">
              S
            </Text>
          </div>
          <NavList collapsed />
        </aside>

        <div className="flex flex-1 flex-col overflow-hidden">
          <header className="flex h-16 shrink-0 items-center justify-between border-b border-border bg-card px-4 md:px-6">
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
              <div className="hidden min-w-0 flex-col leading-tight sm:flex">
                <Text size="sm" fw={600} truncate>
                  {tenant?.name ?? userTenantName ?? "SenseİK"}
                </Text>
                <HeaderBreadcrumbs />
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
    </div>
  );
}
