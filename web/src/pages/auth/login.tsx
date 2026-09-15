import { useState } from "react";
import { useNavigate } from "react-router";
import { useTranslation } from "react-i18next";
import {
  ActionIcon,
  Alert,
  Button,
  Checkbox,
  Group,
  Menu,
  PasswordInput,
  Stack,
  Text,
  TextInput,
  ThemeIcon,
  Title,
  Tooltip,
} from "@mantine/core";
import { Palmtree, Eye, EyeOff, Moon, Sun, Monitor, Wallet, Target } from "lucide-react";
import { useAuthStore } from "@/store/auth.store";
import { useUIStore } from "@/store/ui.store";
import classes from "./login.module.css";

function BrandMark() {
  return (
    <svg width="40" height="40" viewBox="0 0 40 40" fill="none" aria-hidden="true">
      <rect x="12" y="4" width="24" height="24" rx="6" fill="var(--mantine-color-navy-7)" />
      <rect x="4" y="12" width="24" height="24" rx="6" fill="var(--mantine-color-brand-6)" />
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

  const icon = theme === "dark" ? <Moon size={16} /> : theme === "light" ? <Sun size={16} /> : <Monitor size={16} />;

  return (
    <Menu position="bottom-start">
      <Tooltip label={t("common:theme")} position="right">
        <Menu.Target>
          <ActionIcon
            variant="default"
            size="lg"
            radius="xl"
            className="fixed left-6 top-6 z-20"
            aria-label={t("common:theme")}
          >
            {icon}
          </ActionIcon>
        </Menu.Target>
      </Tooltip>
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

function PasswordToggleIcon({ reveal }: { reveal: boolean }) {
  return reveal ? <EyeOff size={16} /> : <Eye size={16} />;
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
    <div className={`flex min-h-screen ${classes.page}`}>
      <LoginThemeToggle />

      {/* Brand panel */}
      <aside
        className={`hidden min-w-[420px] flex-none basis-[44%] flex-col justify-between p-14 lg:flex ${classes.brandPanel}`}
      >
        <Group gap="sm" wrap="nowrap">
          <BrandMark />
          <Stack gap={2}>
            <Text fz={20} fw={800} lh={1.1} lts={-0.4}>
              SenseİK
            </Text>
            <Text fz={10.5} c="dimmed" lts={0.5} lh={1.2}>
              {t("auth:brandPoweredBy")}
            </Text>
          </Stack>
        </Group>

        <div className="max-w-md">
          <Title order={1} fz={36} fw={800} lh={1.2} lts={-0.5} className={classes.tagline}>
            {t("auth:brandTagline")}
          </Title>
          <Text mt="md" maw={384} size="md" lh={1.6} c="dimmed">
            {t("auth:brandSubtitle")}
          </Text>

          <Stack mt={40} gap="md">
            {FEATURES.map(({ icon: Icon, key }) => (
              <Group key={key} gap={14} wrap="nowrap">
                <ThemeIcon variant="light" size={40} radius={10}>
                  <Icon size={20} />
                </ThemeIcon>
                <Text size="sm" fw={500}>
                  {t(`auth:${key}`)}
                </Text>
              </Group>
            ))}
          </Stack>
        </div>

        <Text size="xs" c="dimmed">
          {t("auth:footerCopyright", { year: new Date().getFullYear() })}
        </Text>
      </aside>

      {/* Form panel */}
      <main className="flex flex-1 items-center justify-center px-6 py-12">
        <form className="w-full max-w-sm" onSubmit={handleSubmit} noValidate>
          <Title order={2} fz={24} fw={700} lts={-0.3}>
            {t("auth:welcomeBack")}
          </Title>
          <Text mt={8} size="sm" c="dimmed">
            {t("auth:welcomeSubtitle")}
          </Text>

          {error && (
            <Alert mt="lg" color="red" variant="light">
              <Text size="sm" fw={500} c="red">
                {error}
              </Text>
            </Alert>
          )}

          <TextInput
            mt="lg"
            size="md"
            id="email"
            type="email"
            label={t("auth:email")}
            autoComplete="username"
            value={email}
            onChange={(e) => setEmail(e.currentTarget.value)}
            placeholder={t("auth:emailPlaceholder")}
            disabled={isLoading}
            required
            withAsterisk={false}
          />

          <PasswordInput
            mt="md"
            size="md"
            id="password"
            label={t("auth:password")}
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.currentTarget.value)}
            placeholder={t("auth:passwordPlaceholder")}
            disabled={isLoading}
            required
            withAsterisk={false}
            visible={showPassword}
            onVisibilityChange={setShowPassword}
            visibilityToggleIcon={PasswordToggleIcon}
            visibilityToggleFocusable
            visibilityToggleButtonProps={{
              "aria-label": showPassword ? t("auth:hidePassword") : t("auth:showPassword"),
            }}
          />

          <Group mt="md" justify="space-between" wrap="nowrap">
            <Checkbox
              checked={rememberMe}
              onChange={(e) => setRememberMe(e.currentTarget.checked)}
              label={t("auth:rememberMe")}
              styles={{ label: { color: "var(--mantine-color-dimmed)" } }}
            />
            <Text
              size="sm"
              fw={500}
              c="dimmed"
              opacity={0.6}
              style={{ cursor: "not-allowed" }}
              title={t("auth:forgotPassword")}
            >
              {t("auth:forgotPassword")}
            </Text>
          </Group>

          <Button type="submit" mt="lg" h={44} fullWidth disabled={isLoading}>
            {isLoading ? t("auth:loggingIn") : t("auth:login")}
          </Button>

          <Text mt="lg" ta="center" size="sm" c="dimmed">
            {t("auth:noAccount")}
          </Text>
          <Text mt="xl" ta="center" size="xs" c="dimmed">
            {t("auth:demoCredentials")}
          </Text>
        </form>
      </main>
    </div>
  );
}
