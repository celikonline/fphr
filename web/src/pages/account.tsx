import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import {
  Anchor,
  Avatar,
  Badge,
  Button,
  Card,
  DataList,
  Group,
  Input,
  SegmentedControl,
  Select,
  Stack,
  Text,
  Title,
} from "@mantine/core";
import { LifeBuoy, UserCircle } from "lucide-react";
import { useAuthStore } from "@/store/auth.store";
import { useUIStore, type Theme } from "@/store/ui.store";

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

/** Kendi hesabım: profile basics from /identity/me plus client-side language and theme preferences. */
export default function AccountPage() {
  const { t, i18n } = useTranslation(["account", "common", "navigation"]);
  const user = useAuthStore((state) => state.user);
  const theme = useUIStore((state) => state.theme);
  const setTheme = useUIStore((state) => state.setTheme);

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
          <Avatar size={56} radius="xl" fz="lg">
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
    </div>
  );
}
