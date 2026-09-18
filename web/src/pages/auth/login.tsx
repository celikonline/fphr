import { useState, type CSSProperties } from "react";
import { Link, useNavigate } from "react-router";
import { useTranslation } from "react-i18next";
import {
  ActionIcon,
  Alert,
  Button,
  Checkbox,
  Group,
  Menu,
  Modal,
  PasswordInput,
  PinInput,
  Stack,
  Text,
  TextInput,
  ThemeIcon,
  Title,
  Tooltip,
  UnstyledButton,
} from "@mantine/core";
import {
  ArrowLeft,
  ArrowRight,
  Building2,
  Palmtree,
  Eye,
  EyeOff,
  Moon,
  ShieldCheck,
  Sun,
  Monitor,
  Wallet,
  Target,
} from "lucide-react";
import { getApiProblem } from "@/lib/api-error";
import { useAuthStore, type TenantChoice } from "@/store/auth.store";
import { useUIStore } from "@/store/ui.store";
import classes from "./login.module.css";

/** Brand mark: Kurumsal Mavi square over a Kurumsal Lacivert one (white on the navy brand panel). */
function BrandMark({ onDark = false }: { onDark?: boolean }) {
  return (
    <svg width="40" height="40" viewBox="0 0 40 40" fill="none" aria-hidden="true">
      <rect
        x="12"
        y="4"
        width="24"
        height="24"
        rx="6"
        fill={onDark ? "var(--mantine-color-white)" : "var(--mantine-color-navy-7)"}
      />
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
    <Menu position="bottom-end">
      <Tooltip label={t("common:theme")} position="left">
        <Menu.Target>
          <ActionIcon
            variant="default"
            size="lg"
            radius="xl"
            className="fixed right-6 top-6 z-20"
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
  { icon: Palmtree, key: "featureLeaveTitle", color: "brand.6" },
  { icon: Wallet, key: "featurePayrollTitle", color: "orange.6" },
  { icon: Target, key: "featurePerformanceTitle", color: "teal.6" },
] as const;

/** Staggered entrance delay for `classes.reveal` elements. */
function revealDelay(ms: number): CSSProperties {
  return { animationDelay: `${ms}ms` };
}

/** Survives a reload while the MFA code screen is up (it's session state, not React state) — otherwise a stray
 * F5 silently drops the user back to the empty email/password form with no indication a challenge was pending. */
const MFA_TOKEN_STORAGE_KEY = "senseik.mfaToken";

function readStoredMfaToken(): string | null {
  try {
    return sessionStorage.getItem(MFA_TOKEN_STORAGE_KEY);
  } catch {
    return null;
  }
}

function writeStoredMfaToken(token: string | null) {
  try {
    if (token) {
      sessionStorage.setItem(MFA_TOKEN_STORAGE_KEY, token);
    } else {
      sessionStorage.removeItem(MFA_TOKEN_STORAGE_KEY);
    }
  } catch {
    // Storage unavailable (private mode, blocked cookies, ...) — MFA still works, just won't survive a reload.
  }
}

export default function LoginPage() {
  const { t } = useTranslation(["auth"]);
  const navigate = useNavigate();
  const login = useAuthStore((state) => state.login);
  const completeMfaLogin = useAuthStore((state) => state.completeMfaLogin);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [rememberMe, setRememberMe] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState("");
  const [tenantChoices, setTenantChoices] = useState<TenantChoice[] | null>(null);
  const [mfaToken, setMfaTokenState] = useState<string | null>(() => readStoredMfaToken());
  const [mfaCode, setMfaCode] = useState("");
  const [mfaError, setMfaError] = useState("");

  const setMfaToken = (token: string | null) => {
    setMfaTokenState(token);
    writeStoredMfaToken(token);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsLoading(true);
    setError("");

    try {
      const result = await login(email, password);
      if (result.status === "select_tenant") {
        setTenantChoices(result.tenants);
        return;
      }
      if (result.status === "mfa_required") {
        setMfaToken(result.mfaToken);
        return;
      }
      navigate("/app", { replace: true });
    } catch (err) {
      setError(err instanceof Error ? err.message : t("auth:loginFailed"));
    } finally {
      setIsLoading(false);
    }
  };

  const handleSelectTenant = async (tenant: TenantChoice) => {
    setIsLoading(true);
    setError("");

    try {
      const result = await login(email, password, tenant.slug);
      setTenantChoices(null);
      if (result.status === "mfa_required") {
        setMfaToken(result.mfaToken);
        return;
      }
      navigate("/app", { replace: true });
    } catch (err) {
      setTenantChoices(null);
      setError(err instanceof Error ? err.message : t("auth:loginFailed"));
    } finally {
      setIsLoading(false);
    }
  };

  /** `identity.mfa.token_expired` (S-5): the challenge itself expired — send the user back to email/password. */
  const handleMfaSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!mfaToken) return;
    setIsLoading(true);
    setMfaError("");

    try {
      await completeMfaLogin(mfaToken, mfaCode);
      setMfaToken(null);
      navigate("/app", { replace: true });
    } catch (err) {
      const problem = getApiProblem(err instanceof Error ? err.cause : undefined);
      if (problem?.code === "identity.mfa.token_expired") {
        setMfaToken(null);
        setMfaCode("");
        setError(t("auth:mfa.tokenExpired"));
      } else {
        setMfaCode("");
        setMfaError(err instanceof Error ? err.message : t("auth:mfa.invalidCode"));
      }
    } finally {
      setIsLoading(false);
    }
  };

  const handleBackToLogin = () => {
    setMfaToken(null);
    setMfaCode("");
    setMfaError("");
  };

  return (
    <div className={`flex min-h-screen ${classes.page}`}>
      <LoginThemeToggle />

      {/* Brand panel */}
      <aside
        className={`hidden min-w-[440px] flex-none basis-[46%] flex-col justify-between p-14 lg:flex ${classes.brandPanel}`}
      >
        <div className={`${classes.shape} ${classes.shapeBack}`} aria-hidden="true" />
        <div className={`${classes.shape} ${classes.shapeFront}`} aria-hidden="true" />

        <Group gap="sm" wrap="nowrap" className={classes.reveal}>
          <BrandMark onDark />
          <Stack gap={2}>
            <Text fz={20} fw={800} lh={1.1} lts={-0.4}>
              SenseİK
            </Text>
            <Text fz={10.5} lts={0.5} lh={1.2} className={classes.muted}>
              {t("auth:brandPoweredBy")}
            </Text>
          </Stack>
        </Group>

        <div className="max-w-md">
          <span className={`${classes.eyebrow} ${classes.reveal}`} style={revealDelay(80)}>
            <span className={classes.pulse} />
            {t("auth:brandEyebrow")}
          </span>
          <Title
            order={1}
            mt="lg"
            fz={42}
            fw={800}
            lh={1.15}
            lts={-0.8}
            className={`${classes.tagline} ${classes.reveal}`}
            style={revealDelay(160)}
          >
            {t("auth:brandTagline")}
          </Title>
          <Text
            mt="md"
            maw={400}
            size="md"
            lh={1.65}
            className={`${classes.muted} ${classes.reveal}`}
            style={revealDelay(240)}
          >
            {t("auth:brandSubtitle")}
          </Text>

          <Stack mt={40} gap="sm">
            {FEATURES.map(({ icon: Icon, key, color }, index) => (
              <div key={key} className={`${classes.feature} ${classes.reveal}`} style={revealDelay(320 + index * 80)}>
                <ThemeIcon color={color} size={40} radius={10}>
                  <Icon size={20} />
                </ThemeIcon>
                <Text size="sm" fw={600}>
                  {t(`auth:${key}`)}
                </Text>
              </div>
            ))}
          </Stack>
        </div>

        <Text size="xs" className={classes.muted}>
          {t("auth:footerCopyright", { year: new Date().getFullYear() })}
        </Text>
      </aside>

      {/* Form panel */}
      <main className={`flex flex-1 items-center justify-center px-6 py-16 ${classes.formPanel}`}>
        {mfaToken !== null ? (
          <form className={`w-full max-w-sm ${classes.reveal}`} style={revealDelay(120)} onSubmit={handleMfaSubmit} noValidate>
            <Group gap="sm" wrap="nowrap" mb={40} className="lg:hidden">
              <BrandMark />
              <Text fz={20} fw={800} lts={-0.4}>
                SenseİK
              </Text>
            </Group>

            <div className={classes.accentBar} />
            <ThemeIcon variant="light" color="brand" size={44} radius="md" mt="md">
              <ShieldCheck size={22} />
            </ThemeIcon>
            <Title order={2} mt="md" fz={30} fw={800} lh={1.2} lts={-0.6}>
              {t("auth:mfa.title")}
            </Title>
            <Text mt={8} size="sm" c="dimmed">
              {t("auth:mfa.subtitle")}
            </Text>

            {mfaError && (
              <Alert mt="lg" color="red" variant="light">
                <Text size="sm" fw={500} c="red">
                  {mfaError}
                </Text>
              </Alert>
            )}

            <Stack mt="xl" gap={4}>
              <Text component="label" size="sm" fw={500}>
                {t("auth:mfa.codeLabel")}
              </Text>
              <PinInput
                length={6}
                type="number"
                size="md"
                value={mfaCode}
                onChange={setMfaCode}
                disabled={isLoading}
                autoFocus
                oneTimeCode
                inputMode="numeric"
                ariaLabel={t("auth:mfa.codeLabel")}
                classNames={{ root: classes.mfaCode }}
              />
            </Stack>

            <Button
              type="submit"
              mt="xl"
              h={48}
              fullWidth
              variant="gradient"
              gradient={{ from: "blue.6", to: "brand.6", deg: 120 }}
              disabled={isLoading || mfaCode.length < 6}
              className={classes.submit}
            >
              {isLoading ? t("auth:mfa.submitting") : t("auth:mfa.submit")}
            </Button>

            <Button mt="md" fullWidth variant="subtle" leftSection={<ArrowLeft size={16} />} onClick={handleBackToLogin} disabled={isLoading}>
              {t("auth:mfa.back")}
            </Button>
          </form>
        ) : (
        <form className={`w-full max-w-sm ${classes.reveal}`} style={revealDelay(120)} onSubmit={handleSubmit} noValidate>
          <Group gap="sm" wrap="nowrap" mb={40} className="lg:hidden">
            <BrandMark />
            <Text fz={20} fw={800} lts={-0.4}>
              SenseİK
            </Text>
          </Group>

          <div className={classes.accentBar} />
          <Title order={2} mt="md" fz={30} fw={800} lh={1.2} lts={-0.6}>
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
            mt="xl"
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
            classNames={{ input: classes.input }}
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
            classNames={{ input: classes.input }}
          />

          <Group mt="md" justify="space-between" wrap="nowrap">
            <Checkbox
              checked={rememberMe}
              onChange={(e) => setRememberMe(e.currentTarget.checked)}
              label={t("auth:rememberMe")}
              styles={{ label: { color: "var(--mantine-color-dimmed)" } }}
            />
            <Text component={Link} to="/forgot-password" size="sm" fw={500} c="dimmed" style={{ textDecoration: "none" }}>
              {t("auth:forgotPassword")}
            </Text>
          </Group>

          <Button
            type="submit"
            mt="xl"
            h={48}
            fullWidth
            variant="gradient"
            gradient={{ from: "blue.6", to: "brand.6", deg: 120 }}
            rightSection={isLoading ? undefined : <ArrowRight size={18} />}
            disabled={isLoading}
            className={classes.submit}
          >
            {isLoading ? t("auth:loggingIn") : t("auth:login")}
          </Button>

          <Text mt="lg" ta="center" size="sm" c="dimmed">
            {t("auth:noAccount")}
          </Text>
          <Text mt="xl" ta="center" size="xs" c="dimmed" className={classes.demo}>
            {t("auth:demoCredentials")}
          </Text>
        </form>
        )}
      </main>

      <Modal
        opened={tenantChoices !== null}
        onClose={() => setTenantChoices(null)}
        title={t("auth:selectCompany.title")}
        centered
      >
        <Text size="sm" c="dimmed" mb="md">
          {t("auth:selectCompany.subtitle")}
        </Text>
        <Stack gap="xs">
          {tenantChoices?.map((tenant) => (
            <UnstyledButton
              key={tenant.tenantId}
              onClick={() => handleSelectTenant(tenant)}
              disabled={isLoading}
              className={classes.tenantOption}
            >
              <Group justify="space-between" wrap="nowrap">
                <Group gap="sm" wrap="nowrap">
                  <ThemeIcon variant="light" color="brand" size={36} radius="md">
                    <Building2 size={18} />
                  </ThemeIcon>
                  <div>
                    <Text fw={600} size="sm">
                      {tenant.name}
                    </Text>
                    <Text size="xs" c="dimmed">
                      {tenant.slug}
                    </Text>
                  </div>
                </Group>
                <ArrowRight size={16} />
              </Group>
            </UnstyledButton>
          ))}
        </Stack>
      </Modal>
    </div>
  );
}
