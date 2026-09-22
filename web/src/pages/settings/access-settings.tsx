import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Info, KeyRound } from "lucide-react";
import { Alert, Button, Card, Divider, Group, Skeleton, Stack, Switch, Text, Title } from "@mantine/core";
import { useAnyPermission, usePermission } from "@/components/permission-guard";
import NoAccess from "@/components/no-access";
import { toast } from "@/hooks/use-toast";
import { useAuthStore } from "@/store/auth.store";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  ACCESS_SETTING_GROUPS,
  getAccessSettings,
  updateAccessSettings,
  type AccessSettingKey,
  type AccessSettings,
} from "@/services/access-settings.service";
import { SETTINGS_PERMISSIONS } from "@/config/navigation";

const QUERY_KEY = ["identity", "access-settings"];

function isDirty(draft: AccessSettings | null, saved: AccessSettings | undefined): boolean {
  if (!draft || !saved) return false;
  return (Object.keys(saved) as AccessSettingKey[]).some((key) => draft[key] !== saved[key]);
}

export default function AccessSettingsPage() {
  const canView = useAnyPermission([...SETTINGS_PERMISSIONS.access]);
  if (!canView) return <NoAccess />;
  return <AccessSettingsForm />;
}

function AccessSettingsForm() {
  const { t } = useTranslation(["settings", "common"]);
  const queryClient = useQueryClient();
  const canManage = usePermission("identity.tenant.manage");
  const refreshMe = useAuthStore((state) => state.refreshMe);

  const { data, isLoading, isError } = useQuery({ queryKey: QUERY_KEY, queryFn: getAccessSettings });
  // Local edits only; null = untouched, so the form always reflects the latest server values until edited.
  const [edits, setEdits] = useState<AccessSettings | null>(null);
  const draft = edits ?? data ?? null;
  const dirty = isDirty(draft, data);

  const saveMutation = useMutation({
    mutationFn: updateAccessSettings,
    onSuccess: async (_, saved) => {
      queryClient.setQueryData(QUERY_KEY, saved);
      setEdits(null);
      await refreshMe();
      toast({ variant: "success", title: t("common:success"), description: t("settings:access.saveSuccess") });
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("settings:access.saveError")),
      }),
  });

  function toggle(key: AccessSettingKey, value: boolean) {
    if (!draft) return;
    setEdits({ ...draft, [key]: value });
  }

  return (
    <div className="max-w-4xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <KeyRound className="h-6 w-6" /> {t("settings:access.title")}
          </Title>
          <Text c="dimmed">{t("settings:access.subtitle")}</Text>
        </div>
        {canManage && (
          <Group gap="xs">
            {dirty && (
              <Button type="button" variant="default" onClick={() => setEdits(null)} disabled={saveMutation.isPending}>
                {t("common:cancel")}
              </Button>
            )}
            <Button
              type="button"
              onClick={() => draft && saveMutation.mutate(draft)}
              disabled={!dirty || saveMutation.isPending}
            >
              {saveMutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        )}
      </Group>

      <Alert variant="light" color="gray" icon={<Info className="h-4 w-4" />}>
        {t("settings:access.scopeNotice")}
      </Alert>

      {!canManage && (
        <Alert variant="light" color="yellow">
          {t("settings:access.readOnly")}
        </Alert>
      )}

      {isLoading ? (
        <Stack>
          <Skeleton h={160} />
          <Skeleton h={192} />
          <Skeleton h={112} />
        </Stack>
      ) : isError || !draft ? (
        <Alert variant="light" color="red">
          {t("settings:access.loadError")}
        </Alert>
      ) : (
        ACCESS_SETTING_GROUPS.map(({ group, keys }) => (
          <Card key={group} withBorder padding="lg">
            <Title order={2} size="h5" mb="md">
              {t(`settings:access.groups.${group}`)}
            </Title>
            <Stack gap="md">
              {keys.map((key, index) => (
                <Stack key={key} gap="md">
                  {index > 0 && <Divider />}
                  <Group justify="space-between" align="flex-start" gap="xl" wrap="nowrap">
                    <div className="space-y-1">
                      <Text component="label" htmlFor={`access-${key}`} size="sm" fw={500}>
                        {t(`settings:access.keys.${key}.label`)}
                      </Text>
                      <Text size="sm" c="dimmed">
                        {t(`settings:access.keys.${key}.help`)}
                      </Text>
                    </div>
                    <Switch
                      id={`access-${key}`}
                      checked={draft[key]}
                      onChange={(e) => toggle(key, e.currentTarget.checked)}
                      disabled={!canManage || saveMutation.isPending}
                    />
                  </Group>
                </Stack>
              ))}
            </Stack>
          </Card>
        ))
      )}
    </div>
  );
}
