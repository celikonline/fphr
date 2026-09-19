import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { Button, Card, Stack, Text } from "@mantine/core";
import { PowerOff } from "lucide-react";
import type { ReactNode } from "react";
import { useDisabledModuleForCurrentRoute } from "@/hooks/use-module-enabled";

/** Shown in place of a page whose product module the tenant turned off (Ayarlar → Modüller, senseik-057). */
export function ModuleDisabledPage({ moduleKey }: { moduleKey: string }) {
  const { t } = useTranslation(["settings", "common"]);

  return (
    <div className="mx-auto max-w-3xl p-6 md:p-10">
      <Card withBorder padding="xl" data-testid="module-disabled">
        <Stack align="center" gap="xs" ta="center" py="md">
          <Text c="dimmed" lh={0}>
            <PowerOff size={32} />
          </Text>
          <Text fw={500}>{t("settings:modules.disabledPage.title")}</Text>
          <Text size="sm" c="dimmed">
            {t("settings:modules.disabledPage.description", { module: t(`settings:modules.names.${moduleKey}`) })}
          </Text>
          <Button component={Link} to="/app" variant="light" mt="sm">
            {t("settings:modules.disabledPage.home")}
          </Button>
        </Stack>
      </Card>
    </div>
  );
}

/**
 * Wraps the routed page: a URL under a disabled module (bookmark, stale link) renders the friendly
 * "Bu modül kapalı" page instead of a screen whose every API call would fail with 403 `module.disabled`.
 */
export default function ModuleRouteGuard({ children }: { children: ReactNode }) {
  const disabledModule = useDisabledModuleForCurrentRoute();
  return disabledModule ? <ModuleDisabledPage moduleKey={disabledModule} /> : <>{children}</>;
}
