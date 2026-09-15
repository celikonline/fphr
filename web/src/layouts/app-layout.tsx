import { Fragment } from "react";
import { Link, NavLink, Outlet, useLocation } from "react-router";
import { useTranslation } from "react-i18next";
import {
  ChevronRight,
  Globe,
  LayoutGrid,
  LogOut,
  Menu,
  Moon,
  ShieldAlert,
  Sun,
  User as UserIcon,
  Monitor,
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
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Button } from "@/components/ui/button";

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

  return (
    <NavLink
      to={item.path}
      end={item.end}
      title={collapsed ? label : undefined}
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
            <span className="rounded bg-muted px-1.5 py-0.5 text-[10px] font-semibold text-muted-foreground">
              {t("navigation:phase", { phase: item.phase })}
            </span>
          )}
        </>
      )}
    </NavLink>
  );
}

function Breadcrumbs() {
  const { t } = useTranslation(["navigation"]);
  const location = useLocation();
  const crumbs = resolveBreadcrumbs(location.pathname);

  return (
    <nav aria-label="breadcrumb" className="flex items-center gap-1.5 text-sm text-muted-foreground">
      {crumbs.length === 0 ? (
        <span className="font-medium text-foreground">{t("nav.home")}</span>
      ) : (
        <Link to="/app" className="hover:text-foreground">
          {t("nav.home")}
        </Link>
      )}
      {crumbs.map((crumb, index) => {
        const isLast = index === crumbs.length - 1;
        return (
          <Fragment key={crumb.path}>
            <ChevronRight className="h-3.5 w-3.5" />
            {isLast || !crumb.link ? (
              <span className={cn(isLast && "font-medium text-foreground")}>{t(crumb.labelKey)}</span>
            ) : (
              <Link to={crumb.path} className="hover:text-foreground">
                {t(crumb.labelKey)}
              </Link>
            )}
          </Fragment>
        );
      })}
    </nav>
  );
}

function AppsLauncher() {
  const { t } = useTranslation(["navigation"]);
  const allowed = useVisibleItems(ACTIVE_APPS);

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" aria-label={t("navigation:nav.apps")} title={t("navigation:nav.apps")}>
          <LayoutGrid className="h-4 w-4" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-80 p-2">
        <DropdownMenuLabel>{t("navigation:nav.apps")}</DropdownMenuLabel>
        <DropdownMenuSeparator />
        <div className="grid grid-cols-3 gap-1">
          {allowed.map((item) => {
            const Icon = item.icon;
            return (
              <DropdownMenuItem key={item.key} asChild className="flex h-auto flex-col items-center gap-1 p-2 text-center">
                <Link to={item.path}>
                  <Icon className="h-5 w-5 text-primary" />
                  <span className="text-xs leading-tight">{t(item.labelKey)}</span>
                </Link>
              </DropdownMenuItem>
            );
          })}
          {PLANNED_APPS.map((item) => {
            const Icon = item.icon;
            return (
              <DropdownMenuItem
                key={item.key}
                disabled
                className="flex h-auto flex-col items-center gap-1 p-2 text-center"
              >
                <Icon className="h-5 w-5 text-muted-foreground" />
                <span className="text-xs leading-tight">{t(item.labelKey)}</span>
                <Badge variant="outline" className="px-1.5 py-0 text-[10px]">
                  {t("navigation:phase", { phase: item.phase })}
                </Badge>
              </DropdownMenuItem>
            );
          })}
        </div>
        <DropdownMenuSeparator />
        <DropdownMenuItem asChild>
          <Link to="/app/apps" className="justify-center text-sm">
            {t("navigation:apps.viewAll")}
          </Link>
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

function ThemeToggle() {
  const { t } = useTranslation(["common"]);
  const theme = useUIStore((state) => state.theme);
  const setTheme = useUIStore((state) => state.setTheme);

  const icon =
    theme === "dark" ? <Moon className="h-4 w-4" /> : theme === "light" ? <Sun className="h-4 w-4" /> : <Monitor className="h-4 w-4" />;

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" aria-label={t("common:theme")}>
          {icon}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuItem onClick={() => setTheme("light")}>
          <Sun className="mr-2 h-4 w-4" /> {t("common:themeLight")}
        </DropdownMenuItem>
        <DropdownMenuItem onClick={() => setTheme("dark")}>
          <Moon className="mr-2 h-4 w-4" /> {t("common:themeDark")}
        </DropdownMenuItem>
        <DropdownMenuItem onClick={() => setTheme("system")}>
          <Monitor className="mr-2 h-4 w-4" /> {t("common:themeSystem")}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

function LanguageSwitcher() {
  const { t, i18n } = useTranslation(["common"]);

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" aria-label={t("common:language")}>
          <Globe className="h-4 w-4" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuItem onClick={() => i18n.changeLanguage("tr")}>Türkçe</DropdownMenuItem>
        <DropdownMenuItem onClick={() => i18n.changeLanguage("en")}>English</DropdownMenuItem>
        <DropdownMenuItem onClick={() => i18n.changeLanguage("de")}>Deutsch</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
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
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button className="flex items-center gap-2 rounded-md p-1 pr-2 hover:bg-accent" aria-label={t("navigation:nav.account")}>
          <Avatar className="h-8 w-8">
            <AvatarFallback>{initials}</AvatarFallback>
          </Avatar>
          <span className="hidden max-w-[10rem] truncate text-sm font-medium sm:inline">
            {user?.name || user?.email}
          </span>
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuLabel className="truncate">{user?.email}</DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem asChild>
          <Link to="/app/account">
            <UserIcon className="mr-2 h-4 w-4" /> {t("navigation:nav.account")}
          </Link>
        </DropdownMenuItem>
        <DropdownMenuItem onClick={logout} className="text-destructive focus:text-destructive">
          <LogOut className="mr-2 h-4 w-4" /> {t("common:logout")}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

/** Persistent warning while the session runs under a support-access (impersonation) grant. */
function ImpersonationBanner() {
  const { t } = useTranslation(["common"]);
  const impersonatorId = useImpersonatorId();
  const logout = useAuthStore((state) => state.logout);

  if (!impersonatorId) return null;

  return (
    <div
      role="alert"
      className="flex shrink-0 items-center justify-between gap-3 border-b border-amber-500/50 bg-amber-100 px-4 py-2 text-sm text-amber-900 dark:bg-amber-950 dark:text-amber-200"
    >
      <span className="flex items-center gap-2">
        <ShieldAlert className="h-4 w-4 shrink-0" />
        {t("common:impersonation.banner")}
      </span>
      <Button size="sm" variant="outline" onClick={logout}>
        {t("common:impersonation.end")}
      </Button>
    </div>
  );
}

/**
 * Main application shell: single left sidebar (Kolay İK order) + header (breadcrumbs, apps launcher) + content.
 */
export default function AppLayout() {
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
            <Link to="/app" className="text-lg font-bold text-primary">
              {sidebarOpen ? "SenseİK" : "S"}
            </Link>
          </div>
          <NavList collapsed={!sidebarOpen} />
        </aside>

        {/* Mobile icon rail - always icon-only, no store dependency */}
        <aside className="flex w-14 shrink-0 flex-col border-r border-border bg-card md:hidden">
          <div className="flex h-16 items-center justify-center border-b border-border">
            <span className="text-lg font-bold text-primary">S</span>
          </div>
          <NavList collapsed />
        </aside>

        <div className="flex flex-1 flex-col overflow-hidden">
          <header className="flex h-16 shrink-0 items-center justify-between border-b border-border bg-card px-4 md:px-6">
            <div className="flex items-center gap-3">
              <Button
                variant="ghost"
                size="icon"
                onClick={toggleSidebar}
                aria-label="Toggle sidebar"
                className="hidden md:inline-flex"
              >
                <Menu className="h-5 w-5" />
              </Button>
              <div className="hidden flex-col leading-tight sm:flex">
                <span className="text-sm font-semibold">{tenant?.name ?? userTenantName ?? "SenseİK"}</span>
                <Breadcrumbs />
              </div>
            </div>

            <div className="flex items-center gap-1">
              <AppsLauncher />
              <LanguageSwitcher />
              <ThemeToggle />
              <UserMenu />
            </div>
          </header>

          <main className="flex-1 overflow-auto">
            <Outlet />
          </main>
        </div>
      </div>
    </div>
  );
}
