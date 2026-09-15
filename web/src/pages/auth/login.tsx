import { useState } from "react";
import { useNavigate } from "react-router";
import { useTranslation } from "react-i18next";
import { Palmtree, Eye, EyeOff, Moon, Sun, Monitor, Wallet, Target } from "lucide-react";
import { useAuthStore } from "@/store/auth.store";
import { useUIStore } from "@/store/ui.store";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

function BrandMark() {
  return (
    <svg width="40" height="40" viewBox="0 0 40 40" fill="none" aria-hidden="true">
      <rect x="12" y="4" width="24" height="24" rx="6" className="fill-petrol" />
      <rect x="4" y="12" width="24" height="24" rx="6" className="fill-brand" />
      <path
        d="M12.5 27.5L21 19M21 19H14.5M21 19V25.5"
        stroke="white"
        strokeWidth="2.4"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

function LoginThemeToggle() {
  const { t } = useTranslation(["common"]);
  const theme = useUIStore((state) => state.theme);
  const setTheme = useUIStore((state) => state.setTheme);

  const icon =
    theme === "dark" ? <Moon className="h-4 w-4" /> : theme === "light" ? <Sun className="h-4 w-4" /> : <Monitor className="h-4 w-4" />;

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="outline"
          size="icon"
          className="fixed left-6 top-6 z-20 rounded-full"
          aria-label={t("common:theme")}
        >
          {icon}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start">
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

const FEATURES = [
  { icon: Palmtree, key: "featureLeaveTitle" },
  { icon: Wallet, key: "featurePayrollTitle" },
  { icon: Target, key: "featurePerformanceTitle" },
] as const;

export default function LoginPage() {
  const { t } = useTranslation(["auth"]);
  const navigate = useNavigate();
  const login = useAuthStore((state) => state.login);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [rememberMe, setRememberMe] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState("");

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsLoading(true);
    setError("");

    try {
      await login(email, password);
      navigate("/app", { replace: true });
    } catch (err) {
      setError(err instanceof Error ? err.message : t("auth:loginFailed"));
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="flex min-h-screen bg-background">
      <LoginThemeToggle />

      {/* Brand panel */}
      <aside
        className="hidden min-w-[420px] flex-none basis-[44%] flex-col justify-between border-r border-border
          bg-gradient-to-br from-[#EAF8F1] via-[#F5FBF8] to-white
          p-14 dark:from-[#16463B] dark:via-[#12332C] dark:to-[#0A211C] lg:flex"
      >
        <div className="flex items-center gap-3">
          <BrandMark />
          <span className="flex flex-col leading-tight">
            <span className="text-xl font-extrabold tracking-tight text-foreground dark:text-white">SenseİK</span>
            <span className="mt-0.5 text-[10.5px] tracking-wider text-muted-foreground dark:text-white/55">
              {t("auth:brandPoweredBy")}
            </span>
          </span>
        </div>

        <div className="max-w-md">
          <h1 className="text-balance text-3xl font-extrabold leading-tight tracking-tight text-foreground dark:text-white lg:text-4xl">
            {t("auth:brandTagline")}
          </h1>
          <p className="mt-4 max-w-sm text-base leading-relaxed text-muted-foreground dark:text-white/70">
            {t("auth:brandSubtitle")}
          </p>

          <div className="mt-10 flex flex-col gap-4">
            {FEATURES.map(({ icon: Icon, key }) => (
              <div key={key} className="flex items-center gap-3.5">
                <span className="flex h-10 w-10 flex-none items-center justify-center rounded-[10px] bg-brand/10 dark:bg-white/10">
                  <Icon className="h-5 w-5 text-brand-strong dark:text-brand" />
                </span>
                <span className="text-sm font-medium text-foreground dark:text-white/90">{t(`auth:${key}`)}</span>
              </div>
            ))}
          </div>
        </div>

        <p className="text-xs text-muted-foreground dark:text-white/40">
          {t("auth:footerCopyright", { year: new Date().getFullYear() })}
        </p>
      </aside>

      {/* Form panel */}
      <main className="flex flex-1 items-center justify-center px-6 py-12">
        <form className="w-full max-w-sm" onSubmit={handleSubmit} noValidate>
          <h2 className="text-2xl font-bold tracking-tight text-foreground">{t("auth:welcomeBack")}</h2>
          <p className="mt-2 text-sm text-muted-foreground">{t("auth:welcomeSubtitle")}</p>

          {error && (
            <div className="mt-6 rounded-md bg-destructive/10 p-3">
              <p className="text-sm font-medium text-destructive">{error}</p>
            </div>
          )}

          <div className="mt-6 space-y-2">
            <Label htmlFor="email">{t("auth:email")}</Label>
            <Input
              id="email"
              type="email"
              autoComplete="username"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder={t("auth:emailPlaceholder")}
              disabled={isLoading}
              required
            />
          </div>

          <div className="mt-4 space-y-2">
            <Label htmlFor="password">{t("auth:password")}</Label>
            <div className="relative">
              <Input
                id="password"
                type={showPassword ? "text" : "password"}
                autoComplete="current-password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder={t("auth:passwordPlaceholder")}
                disabled={isLoading}
                required
                className="pr-10"
              />
              <button
                type="button"
                onClick={() => setShowPassword((v) => !v)}
                aria-label={showPassword ? t("auth:hidePassword") : t("auth:showPassword")}
                className="absolute right-2 top-1/2 flex h-7 w-7 -translate-y-1/2 items-center justify-center rounded-md text-muted-foreground hover:text-foreground"
              >
                {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
              </button>
            </div>
          </div>

          <div className="mt-4 flex items-center justify-between">
            <label className="flex select-none items-center gap-2 text-sm text-muted-foreground">
              <input
                type="checkbox"
                checked={rememberMe}
                onChange={(e) => setRememberMe(e.target.checked)}
                className="h-4 w-4 rounded border-input accent-brand"
              />
              {t("auth:rememberMe")}
            </label>
            <span
              className="cursor-not-allowed text-sm font-medium text-muted-foreground/60"
              title={t("auth:forgotPassword")}
            >
              {t("auth:forgotPassword")}
            </span>
          </div>

          <Button
            type="submit"
            disabled={isLoading}
            className="mt-6 h-11 w-full bg-brand text-brand-foreground hover:bg-brand-strong"
          >
            {isLoading ? t("auth:loggingIn") : t("auth:login")}
          </Button>

          <p className="mt-6 text-center text-sm text-muted-foreground">{t("auth:noAccount")}</p>
          <p className="mt-8 text-center text-xs text-muted-foreground">{t("auth:demoCredentials")}</p>
        </form>
      </main>
    </div>
  );
}
