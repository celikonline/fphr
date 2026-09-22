import type { ComponentType } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { Badge, Card, Group, Stack, Text, ThemeIcon, Title, UnstyledButton } from "@mantine/core";
import { ChevronRight, FolderOpen, GraduationCap, Laptop } from "lucide-react";
import { useAuthStore } from "@/store/auth.store";
import type { ToggleableModuleKey } from "@/config/modules";
import type { EmployeeDetail } from "@/services/employees.service";
import { ASSETS_CARD_TAB_PERMISSIONS } from "./assets-section";
import { DOCUMENTS_CARD_TAB_PERMISSIONS } from "./documents-section";

interface Shortcut {
  /** Same key as the target card tab (/app/employees/:id/:tab). */
  tabKey: "assets" | "documents";
  icon: ComponentType<{ size?: number }>;
  permission: string | string[];
  module?: ToggleableModuleKey;
}

// Mirrors the gates of the target tabs in card-tabs.tsx so a shortcut never leads to a hidden tab.
const SHORTCUTS: Shortcut[] = [
  { tabKey: "assets", icon: Laptop, permission: ASSETS_CARD_TAB_PERMISSIONS, module: "assets" },
  { tabKey: "documents", icon: FolderOpen, permission: DOCUMENTS_CARD_TAB_PERMISSIONS },
];

/** "Diğer" tab: training records (module not built yet) plus shortcuts to the Zimmet and Dosyalar tabs. */
export function OtherSection({ employee }: { employee: EmployeeDetail }) {
  const { t } = useTranslation(["employees"]);
  const hasPermission = useAuthStore((state) => state.hasPermission);
  const isModuleEnabled = useAuthStore((state) => state.isModuleEnabled);

  const shortcuts = SHORTCUTS.filter(
    ({ permission, module }) =>
      (Array.isArray(permission)
        ? permission.some((key) => hasPermission(key))
        : hasPermission(permission)) &&
      (!module || isModuleEnabled(module))
  );

  return (
    <Stack gap="lg">
      <Card withBorder padding="lg">
        <Group gap="xs">
          <GraduationCap size={18} />
          <Title order={4}>{t("employees:card.other.training.title")}</Title>
          <Badge variant="light">{t("employees:card.other.training.badge")}</Badge>
        </Group>
        <Stack align="center" gap={4} py="lg">
          <ThemeIcon variant="light" color="gray" size={48} radius="xl">
            <GraduationCap size={24} />
          </ThemeIcon>
          <Text fw={500} mt="xs">
            {t("employees:card.other.training.emptyTitle")}
          </Text>
          <Text size="sm" c="dimmed" ta="center" maw={420}>
            {t("employees:card.tabNotes.other")}
          </Text>
        </Stack>
      </Card>

      {shortcuts.length > 0 && (
        <Card withBorder padding="lg">
          <Title order={4} mb="md">
            {t("employees:card.other.shortcuts.title")}
          </Title>
          <div className="grid gap-3 sm:grid-cols-2">
            {shortcuts.map(({ tabKey, icon: Icon }) => (
              <UnstyledButton
                key={tabKey}
                component={Link}
                to={`/app/employees/${employee.id}/${tabKey}`}
                className="rounded-md border border-(--mantine-color-default-border) p-3 hover:bg-(--mantine-color-default-hover)"
              >
                <Group wrap="nowrap" gap="sm">
                  <ThemeIcon variant="light" size="lg">
                    <Icon size={18} />
                  </ThemeIcon>
                  <div className="min-w-0 flex-1">
                    <Text size="sm" fw={500}>
                      {t(`employees:card.tabs.${tabKey}`)}
                    </Text>
                    <Text size="xs" c="dimmed">
                      {t(`employees:card.other.shortcuts.${tabKey}`)}
                    </Text>
                  </div>
                  <ChevronRight size={16} color="var(--mantine-color-dimmed)" />
                </Group>
              </UnstyledButton>
            ))}
          </div>
        </Card>
      )}
    </Stack>
  );
}
