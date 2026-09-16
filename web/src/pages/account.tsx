import { useState } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Anchor,
  Avatar,
  Badge,
  Button,
  Card,
  Code,
  CopyButton,
  DataList,
  Group,
  Input,
  Modal,
  PinInput,
  SegmentedControl,
  Select,
  Stack,
  Text,
  Title,
} from "@mantine/core";
import { Check, Copy, LifeBuoy, LogOut, ShieldCheck, ShieldOff, UserCircle } from "lucide-react";
import { avatarColor } from "@/components/employees/employee-badges";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";
import { disableMfa, enrollMfa, getMfaStatus, verifyMfaEnrollment, type MfaEnrollment } from "@/services/mfa.service";
import { useAuthStore } from "@/store/auth.store";
import { useUIStore, type Theme } from "@/store/ui.store";

type TFn = (key: string, opts?: Record<string, unknown>) => string;

const LANGUAGES = [
  { value: "tr", label: "Türkçe" },
  { value: "en", label: "English" },
  { value: "de", label: "Deutsch" },
];

const THEMES: Theme[] = ["light", "dark", "system"];

function ProfileRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <DataList.Item>
      <DataList.ItemLabel>{label}</DataList.ItemLabel>
      <DataList.ItemValue>{children}</DataList.ItemValue>
    </DataList.Item>
  );
}

/** Base32 secrets are easier to type/compare in 4-character groups (e.g. authenticator app setup screens). */
function groupSecret(secret: string): string {
  return secret.replace(/(.{4})/g, "$1 ").trim();
}

/** `identity.mfa.*` (S-5, Faz 3 tur 3); falls back to the server's localized `detail` for anything else. */
function mfaErrorMessage(error: unknown, t: TFn): string {
  const problem = getApiProblem(error);
  if (problem?.code === "identity.mfa.invalid_code") return t("account:mfaSection.errors.invalidCode");
  if (problem?.code === "identity.mfa.already_enabled") return t("account:mfaSection.errors.alreadyEnabled");
  return getApiErrorMessage(error, t("account:mfaSection.errors.generic"));
}

function MfaEnrollDialog({
  enrollment,
  onClose,
  onEnabled,
  t,
}: {
  enrollment: MfaEnrollment;
  onClose: () => void;
  onEnabled: () => void;
  t: TFn;
}) {
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);

  const verifyMutation = useMutation({
    mutationFn: () => verifyMfaEnrollment(code),
    onSuccess: onEnabled,
    onError: (err: unknown) => {
      setCode("");
      setError(mfaErrorMessage(err, t));
    },
  });

  return (
    <Modal opened onClose={onClose} title={t("account:mfaSection.enrollTitle")} centered>
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          {t("account:mfaSection.enrollHint")}
        </Text>

        <div>
          <Text size="xs" fw={600} c="dimmed" tt="uppercase" mb={4}>
            {t("account:mfaSection.secretLabel")}
          </Text>
          <Group gap="xs" wrap="nowrap" align="center">
            <Code fz="sm" style={{ wordBreak: "break-all", letterSpacing: 1 }}>
              {groupSecret(enrollment.secret)}
            </Code>
            <CopyButton value={enrollment.secret}>
              {({ copied, copy }) => (
                <Button
                  size="xs"
                  variant="default"
                  color={copied ? "teal" : undefined}
                  leftSection={copied ? <Check size={14} /> : <Copy size={14} />}
                  onClick={copy}
                >
                  {copied ? t("account:mfaSection.copied") : t("account:mfaSection.copy")}
                </Button>
              )}
            </CopyButton>
          </Group>
        </div>

        <Anchor href={enrollment.otpAuthUri} size="sm" target="_blank" rel="noreferrer">
          {t("account:mfaSection.otpauthLink")}
        </Anchor>

        {error && (
          <Alert color="red" variant="light">
            {error}
          </Alert>
        )}

        <Stack gap={4}>
          <Text size="sm" fw={500}>
            {t("account:mfaSection.codeLabel")}
          </Text>
          <PinInput
            length={6}
            type="number"
            value={code}
            onChange={setCode}
            disabled={verifyMutation.isPending}
            oneTimeCode
            ariaLabel={t("account:mfaSection.codeLabel")}
          />
        </Stack>

        <Group justify="flex-end">
          <Button variant="default" onClick={onClose} disabled={verifyMutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button
            leftSection={<ShieldCheck size={16} />}
            onClick={() => verifyMutation.mutate()}
            disabled={code.length < 6 || verifyMutation.isPending}
          >
            {verifyMutation.isPending ? t("account:mfaSection.verifying") : t("account:mfaSection.verifyCta")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function MfaDisableDialog({ onClose, onDisabled, t }: { onClose: () => void; onDisabled: () => void; t: TFn }) {
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);

  const disableMutation = useMutation({
    mutationFn: () => disableMfa(code),
    onSuccess: onDisabled,
    onError: (err: unknown) => {
      setCode("");
      setError(mfaErrorMessage(err, t));
    },
  });

  return (
    <Modal opened onClose={onClose} title={t("account:mfaSection.disableTitle")} centered>
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          {t("account:mfaSection.disableHint")}
        </Text>

        {error && (
          <Alert color="red" variant="light">
            {error}
          </Alert>
        )}

        <Stack gap={4}>
          <Text size="sm" fw={500}>
            {t("account:mfaSection.codeLabel")}
          </Text>
          <PinInput
            length={6}
            type="number"
            value={code}
            onChange={setCode}
            disabled={disableMutation.isPending}
            oneTimeCode
            ariaLabel={t("account:mfaSection.codeLabel")}
          />
        </Stack>

        <Group justify="flex-end">
          <Button variant="default" onClick={onClose} disabled={disableMutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button
            color="red"
            leftSection={<ShieldOff size={16} />}
            onClick={() => disableMutation.mutate()}
            disabled={code.length < 6 || disableMutation.isPending}
          >
            {disableMutation.isPending ? t("account:mfaSection.disabling") : t("account:mfaSection.disableCta")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

/** Kendi hesabım: profile basics from /identity/me plus client-side language and theme preferences. */
export default function AccountPage() {
  const { t, i18n } = useTranslation(["account", "auth", "common", "navigation"]);
  const user = useAuthStore((state) => state.user);
  const logout = useAuthStore((state) => state.logout);
  const refreshMe = useAuthStore((state) => state.refreshMe);
  const theme = useUIStore((state) => state.theme);
  const setTheme = useUIStore((state) => state.setTheme);
  const queryClient = useQueryClient();

  // S-12 (gün açılışı tur 1): `GET me/mfa` is the security card's source of truth — in particular
  // `pendingEnrollment`, which `/identity/me` (and thus `user?.mfaEnabled`) doesn't expose. Falls back to the
  // auth store's snapshot while this query is loading so the card doesn't flash the wrong CTA.
  const mfaStatusQuery = useQuery({ queryKey: ["mfa-status"], queryFn: getMfaStatus });
  const mfaEnabled = mfaStatusQuery.data?.enabled ?? user?.mfaEnabled ?? false;
  const mfaPending = !mfaEnabled && (mfaStatusQuery.data?.pendingEnrollment ?? false);

  const [enrollment, setEnrollment] = useState<MfaEnrollment | null>(null);
  const [disableOpen, setDisableOpen] = useState(false);
  const enrollMutation = useMutation({
    mutationFn: enrollMfa,
    onSuccess: setEnrollment,
    onError: (err: unknown) =>
      toast({ variant: "destructive", title: t("common:error"), description: mfaErrorMessage(err, t) }),
  });

  function refreshMfaState() {
    void refreshMe();
    void queryClient.invalidateQueries({ queryKey: ["mfa-status"] });
  }

  const initials = (user?.name || user?.email || "?")
    .split(" ")
    .map((part) => part[0])
    .join("")
    .slice(0, 2)
    .toUpperCase();
  const currentLanguage = LANGUAGES.find((lang) => i18n.resolvedLanguage?.startsWith(lang.value))?.value ?? "tr";

  return (
    <div className="mx-auto max-w-4xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} fz={24}>
          <Group gap="xs" component="span">
            <UserCircle size={24} /> {t("account:title")}
          </Group>
        </Title>
        <Text c="dimmed">{t("account:subtitle")}</Text>
      </div>

      <Card withBorder padding="lg">
        <Group gap="md" wrap="nowrap" mb="lg">
          <Avatar size={56} radius="xl" fz="lg" color={user?.id ? avatarColor(user.id) : undefined}>
            {initials}
          </Avatar>
          <div className="min-w-0">
            <Text fz="xl" fw={600} truncate>
              {user?.name || user?.email}
            </Text>
            <Text size="sm" c="dimmed" truncate>
              {user?.email}
            </Text>
          </div>
        </Group>
        <DataList withDivider labelWidth="33%">
          <ProfileRow label={t("account:firstName")}>{user?.firstName || "—"}</ProfileRow>
          <ProfileRow label={t("account:lastName")}>{user?.lastName || "—"}</ProfileRow>
          <ProfileRow label={t("account:email")}>{user?.email ?? "—"}</ProfileRow>
          <ProfileRow label={t("account:company")}>{user?.tenant?.name ?? "—"}</ProfileRow>
          <ProfileRow label={t("account:roles")}>
            {user && user.roles.length > 0 ? (
              <Group gap={4}>
                {user.roles.map((role) => (
                  <Badge key={role} variant="light" color="gray" tt="none">
                    {t(`account:roleNames.${role}`, role)}
                  </Badge>
                ))}
              </Group>
            ) : (
              "—"
            )}
          </ProfileRow>
          <ProfileRow label={t("account:mfa")}>
            {user?.mfaEnabled ? t("account:mfaEnabled") : t("account:mfaDisabled")}
          </ProfileRow>
          {user?.employeeId && (
            <ProfileRow label={t("account:employeeCard")}>
              <Anchor component={Link} to={`/app/employees/${user.employeeId}`} size="sm">
                {t("account:viewEmployeeCard")}
              </Anchor>
            </ProfileRow>
          )}
        </DataList>
      </Card>

      <Card withBorder padding="lg">
        <Stack gap={4} mb="lg">
          <Text fw={600}>{t("account:preferences")}</Text>
          <Text size="sm" c="dimmed">
            {t("account:preferencesHint")}
          </Text>
        </Stack>
        <Stack gap="lg">
          <Select
            id="account-language"
            label={t("common:language")}
            data={LANGUAGES}
            value={currentLanguage}
            onChange={(code) => {
              if (code) void i18n.changeLanguage(code);
            }}
            allowDeselect={false}
            maw={320}
          />

          <Input.Wrapper label={t("common:theme")} labelElement="div">
            <div>
              <SegmentedControl<Theme>
                value={theme}
                onChange={setTheme}
                data={THEMES.map((value) => ({
                  value,
                  label: t(`common:theme${value.charAt(0).toUpperCase()}${value.slice(1)}`),
                }))}
                mt={4}
              />
            </div>
          </Input.Wrapper>
        </Stack>
      </Card>

      <Card withBorder padding="lg">
        <Group justify="space-between" align="flex-start" gap="sm" wrap="wrap">
          <Stack gap={4} maw={420}>
            <Group gap="xs">
              <Text fw={600}>{t("account:mfaSection.title")}</Text>
              <Badge color={mfaEnabled ? "teal" : mfaPending ? "orange" : "gray"} variant="light" tt="none">
                {mfaEnabled
                  ? t("account:mfaEnabled")
                  : mfaPending
                    ? t("auth:mfa.status.pendingBadge")
                    : t("account:mfaDisabled")}
              </Badge>
            </Group>
            <Text size="sm" c="dimmed">
              {mfaPending ? t("auth:mfa.status.pendingHint") : t("account:mfaSection.hint")}
            </Text>
          </Stack>
          {mfaEnabled ? (
            <Button color="red" variant="default" leftSection={<ShieldOff size={16} />} onClick={() => setDisableOpen(true)}>
              {t("account:mfaSection.disableCta")}
            </Button>
          ) : (
            <Button
              leftSection={<ShieldCheck size={16} />}
              onClick={() => enrollMutation.mutate()}
              loading={enrollMutation.isPending}
            >
              {mfaPending ? t("auth:mfa.status.resumeCta") : t("account:mfaSection.enableCta")}
            </Button>
          )}
        </Group>
      </Card>

      <Card withBorder padding="lg">
        <Group justify="space-between" gap="sm">
          <div>
            <Text fw={500}>{t("navigation:nav.support")}</Text>
            <Text size="sm" c="dimmed">
              {t("account:supportHint")}
            </Text>
          </div>
          <Button component={Link} to="/app/support" variant="default" leftSection={<LifeBuoy size={16} />}>
            {t("navigation:nav.support")}
          </Button>
        </Group>
      </Card>

      <Card withBorder padding="lg" color="red">
        <Group justify="space-between" gap="sm">
          <div>
            <Text fw={500}>{t("common:logout")}</Text>
            <Text size="sm" c="dimmed">
              {t("account:logoutHint")}
            </Text>
          </div>
          <Button color="red" variant="default" leftSection={<LogOut size={16} />} onClick={logout}>
            {t("common:logout")}
          </Button>
        </Group>
      </Card>

      {enrollment && (
        <MfaEnrollDialog
          enrollment={enrollment}
          onClose={() => setEnrollment(null)}
          onEnabled={() => {
            setEnrollment(null);
            refreshMfaState();
            toast({ variant: "success", title: t("common:success"), description: t("account:mfaSection.enableSuccess") });
          }}
          t={t}
        />
      )}

      {disableOpen && (
        <MfaDisableDialog
          onClose={() => setDisableOpen(false)}
          onDisabled={() => {
            setDisableOpen(false);
            refreshMfaState();
            toast({ variant: "success", title: t("common:success"), description: t("account:mfaSection.disableSuccess") });
          }}
          t={t}
        />
      )}
    </div>
  );
}
