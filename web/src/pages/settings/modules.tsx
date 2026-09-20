import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Blocks, Info, Lock } from "lucide-react";
import { Alert, Badge, Button, Card, Divider, Group, Skeleton, Stack, Switch, Text, Title } from "@mantine/core";
import { useAnyPermission, usePermission } from "@/components/permission-guard";
import NoAccess from "@/components/no-access";
import ConfirmDialog from "@/components/confirm-dialog";
import { toast } from "@/hooks/use-toast";
import { useAuthStore } from "@/store/auth.store";
import { getApiErrorMessage } from "@/lib/api-error";
import { getModuleSettings, updateModuleSettings, type ModuleSettingItem } from "@/services/module-settings.service";
import { SETTINGS_PERMISSIONS } from "@/config/navigation";

export const MODULE_SETTINGS_QUERY_KEY = ["identity", "module-settings"];

function sameSet(a: readonly string[], b: readonly string[]): boolean {
  if (a.length !== b.length) return false;
  const set = new Set(b);
  return a.every((key) => set.has(key));
}

/** Ayarlar > Sistem ayarları > Modüller (senseik-057): turn product modules on/off for the whole company. */
export default function ModuleSettingsPage() {
  const canView = useAnyPermission([...SETTINGS_PERMISSIONS.modules]);
  if (!canView) return <NoAccess />;
  return <ModuleSettingsForm />;
}

function ModuleSettingsForm() {
  const { t } = useTranslation(["settings", "common"]);
  const queryClient = useQueryClient();
  const canManage = usePermission("identity.modules.manage");
  const refreshMe = useAuthStore((state) => state.refreshMe);

  const { data, isLoading, isError } = useQuery({ queryKey: MODULE_SETTINGS_QUERY_KEY, queryFn: getModuleSettings });
  // Local edits only; null = untouched, so the list always reflects the latest server values until edited.
  const [edits, setEdits] = useState<string[] | null>(null);
  // Module the user is about to turn off - waits for the "veriler silinmez" confirmation.
  const [pendingDisable, setPendingDisable] = useState<ModuleSettingItem | null>(null);

  const saved = data?.disabledModules ?? [];
  const disabled = edits ?? saved;
  const dirty = edits !== null && !sameSet(edits, saved);
  const disabledSet = new Set(disabled);

  const saveMutation = useMutation({
    mutationFn: updateModuleSettings,
    onSuccess: async (_, savedList) => {
      queryClient.setQueryData(MODULE_SETTINGS_QUERY_KEY, {
        modules: (data?.modules ?? []).map((m) => ({ ...m, enabled: !savedList.includes(m.key) })),
        disabledModules: savedList,
      });
      setEdits(null);
      // Nav, launcher, home widgets and routes read `features.disabledModules` from /me.
      await refreshMe();
      toast({ variant: "success", title: t("common:success"), description: t("settings:modules.saveSuccess") });
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("settings:modules.saveError")),
      }),
  });

  function setEnabled(key: string, enabled: boolean) {
    const next = enabled ? disabled.filter((k) => k !== key) : [...disabled.filter((k) => k !== key), key];
    setEdits(next);
  }

  function onToggle(module: ModuleSettingItem, enabled: boolean) {
    if (enabled) {
      setEnabled(module.key, true);
    } else {
      setPendingDisable(module);
    }
  }

  const coreModules = (data?.modules ?? []).filter((m) => m.isCore);
  const toggleableModules = (data?.modules ?? []).filter((m) => !m.isCore);
  const name = (key: string) => t(`settings:modules.names.${key}`, key);
  const description = (key: string) => t(`settings:modules.descriptions.${key}`, "");

  return (
    <div className="mx-auto max-w-4xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Blocks className="h-6 w-6" /> {t("settings:modules.title")}
          </Title>
          <Text c="dimmed">{t("settings:modules.subtitle")}</Text>
        </div>
        {canManage && (
          <Group gap="xs">
            {dirty && (
              <Button type="button" variant="default" onClick={() => setEdits(null)} disabled={saveMutation.isPending}>
                {t("common:cancel")}
              </Button>
            )}
            <Button type="button" onClick={() => saveMutation.mutate(disabled)} disabled={!dirty || saveMutation.isPending}>
              {saveMutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        )}
      </Group>

      <Alert variant="light" color="gray" icon={<Info className="h-4 w-4" />}>
        {t("settings:modules.notice")}
      </Alert>

      {!canManage && (
        <Alert variant="light" color="yellow">
          {t("settings:modules.readOnly")}
        </Alert>
      )}

      {isLoading ? (
        <Skeleton h={320} />
      ) : isError || !data ? (
        <Alert variant="light" color="red">
          {t("settings:modules.loadError")}
        </Alert>
      ) : (
        <>
          <Card withBorder padding="lg">
            <Title order={2} size="h5" mb="md">
              {t("settings:modules.optionalTitle")}
            </Title>
            <Stack gap="sm">
              {toggleableModules.map((module, index) => (
                <div key={module.key}>
                  {index > 0 && <Divider mb="sm" />}
                  <Group justify="space-between" align="center" gap="xl" wrap="nowrap">
                    <div>
                      <Text component="label" htmlFor={`module-${module.key}`} size="sm" fw={500}>
                        {name(module.key)}
                      </Text>
                      <Text size="xs" c="dimmed">
                        {description(module.key)}
                      </Text>
                    </div>
                    <Switch
                      id={`module-${module.key}`}
                      aria-label={name(module.key)}
                      checked={!disabledSet.has(module.key)}
                      onChange={(e) => onToggle(module, e.currentTarget.checked)}
                      disabled={!canManage || saveMutation.isPending}
                    />
                  </Group>
                </div>
              ))}
            </Stack>
          </Card>

          <Card withBorder padding="lg">
            <Title order={2} size="h5" mb={4}>
              {t("settings:modules.coreTitle")}
            </Title>
            <Text size="sm" c="dimmed" mb="md">
              {t("settings:modules.coreHint")}
            </Text>
            <Stack gap="sm">
              {coreModules.map((module) => (
                <Group key={module.key} justify="space-between" align="center" gap="xl" wrap="nowrap">
                  <div>
                    <Text size="sm" fw={500}>
                      {name(module.key)}
                    </Text>
                    <Text size="xs" c="dimmed">
                      {description(module.key)}
                    </Text>
                  </div>
                  <Badge variant="light" color="gray" leftSection={<Lock size={12} />}>
                    {t("settings:modules.coreBadge")}
                  </Badge>
                </Group>
              ))}
            </Stack>
          </Card>
        </>
      )}

      <ConfirmDialog
        open={pendingDisable !== null}
        onOpenChange={(open) => {
          if (!open) setPendingDisable(null);
        }}
        title={pendingDisable ? t("settings:modules.confirmTitle", { module: name(pendingDisable.key) }) : undefined}
        description={t("settings:modules.confirmDescription")}
        confirmLabel={t("settings:modules.confirmAction")}
        onConfirm={() => {
          if (pendingDisable) setEnabled(pendingDisable.key, false);
          setPendingDisable(null);
        }}
      />
    </div>
  );
}
