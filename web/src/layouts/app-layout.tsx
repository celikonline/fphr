import { Fragment } from "react";
import { NavLink, Outlet, useLocation } from "react-router-dom";
import { useTranslation } from "react-i18next";
import {
  ChevronRight,
  Globe,
  LogOut,
  Menu,
  Moon,
  Sun,
  User as UserIcon,
  Monitor,
} from "lucide-react";
import { useUIStore } from "@/store/ui.store";
import { useAuthStore } from "@/store/auth.store";
import { useTenantStore } from "@/store/tenant.store";
import { useAnyPermission } from "@/components/permission-guard";
import { NAV_ITEMS } from "@/config/navigation";
import { cn } from "@/lib/utils";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Button } from "@/components/ui/button";

function NavList() {
  const { t } = useTranslation(["navigation"]);
  const sidebarOpen = useUIStore((state) => state.sidebarOpen);

  return (
    <nav className="flex-1 space-y-1 overflow-y-auto px-2 py-4">
      {NAV_ITEMS.map((item) => (
        <NavItemLink key={item.path} item={item} collapsed={!sidebarOpen} label={t(item.labelKey)} />
      ))}
    </nav>
  );
}

function NavItemLink({
  item,
  collapsed,
  label,
}: {
  item: (typeof NAV_ITEMS)[number];
  collapsed: boolean;
  label: string;
}) {
  const canView = useAnyPermission(item.permissions ?? []);
  if (item.permissions && item.permissions.length > 0 && !canView) {
    return null;
  }

  const Icon = item.icon;

  return (
    <NavLink
      to={item.path}
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
      {!collapsed && <span className="truncate">{label}</span>}
    </NavLink>
  );
}

function Breadcrumbs() {
  const { t } = useTranslation(["navigation"]);
  const location = useLocation();
  const current = NAV_ITEMS.find((item) => location.pathname.startsWith(item.path));

  return (
    <nav aria-label="breadcrumb" className="flex items-center gap-1.5 text-sm text-muted-foreground">
      <span>{t("nav.home")}</span>
      {current && (
        <Fragment>
          <ChevronRight className="h-3.5 w-3.5" />
          <span className="font-medium text-foreground">{t(current.labelKey)}</span>
        </Fragment>
      )}
    </nav>
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
  const { i18n } = useTranslation();

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" aria-label="Language">
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
  const { t } = useTranslation(["common"]);
  const user = useAuthStore((state) => state.user);
  const logout = useAuthStore((state) => state.logout);
  const initials = (user?.name ?? user?.email ?? "?")
    .split(" ")
    .map((part) => part[0])
    .join("")
    .slice(0, 2)
    .toUpperCase();

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button className="flex items-center gap-2 rounded-md p-1 pr-2 hover:bg-accent" aria-label={t("common:settings")}>
          <Avatar className="h-8 w-8">
            <AvatarFallback>{initials}</AvatarFallback>
          </Avatar>
          <span className="hidden max-w-[10rem] truncate text-sm font-medium sm:inline">
            {user?.name ?? user?.email}
          </span>
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuLabel className="truncate">{user?.email}</DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem>
          <UserIcon className="mr-2 h-4 w-4" /> {t("common:profile", "Profile")}
        </DropdownMenuItem>
        <DropdownMenuItem onClick={logout} className="text-destructive focus:text-destructive">
          <LogOut className="mr-2 h-4 w-4" /> {t("common:logout")}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

/**
 * Main application shell: collapsible sidebar + header + breadcrumb content area.
 * Renders nested Item 20 screens via <Outlet/>.
 */
export default function AppLayout() {
  const sidebarOpen = useUIStore((state) => state.sidebarOpen);
  const toggleSidebar = useUIStore((state) => state.toggleSidebar);
  const tenant = useTenantStore((state) => state.currentTenant);

  return (
    <div className="flex h-screen bg-background text-foreground">
      {/* Sidebar - collapses to an icon rail below md, or when toggled */}
      <aside
        className={cn(
          "hidden shrink-0 flex-col border-r border-border bg-card transition-all duration-300 md:flex",
          sidebarOpen ? "w-64" : "w-16"
        )}
      >
        <div className={cn("flex h-16 items-center border-b border-border px-4", !sidebarOpen && "justify-center px-2")}>
          <span className="text-lg font-bold text-primary">{sidebarOpen ? "SenseİK" : "S"}</span>
        </div>
        <NavList />
      </aside>

      {/* Mobile icon rail - always icon-only, no store dependency */}
      <aside className="flex w-14 shrink-0 flex-col border-r border-border bg-card md:hidden">
        <div className="flex h-16 items-center justify-center border-b border-border">
          <span className="text-lg font-bold text-primary">S</span>
        </div>
        <NavList />
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
              <span className="text-sm font-semibold">{tenant?.name ?? "SenseİK"}</span>
              <Breadcrumbs />
            </div>
          </div>

          <div className="flex items-center gap-1">
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
  );
}
