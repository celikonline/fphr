import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Info, KeyRound } from "lucide-react";
import { useAnyPermission, usePermission } from "@/components/permission-guard";
import NoAccess from "@/components/no-access";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
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
    <div className="mx-auto max-w-4xl space-y-6 p-6 md:p-10">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-2xl font-bold">
            <KeyRound className="h-6 w-6" /> {t("settings:access.title")}
          </h1>
          <p className="text-muted-foreground">{t("settings:access.subtitle")}</p>
        </div>
        {canManage && (
          <div className="flex gap-2">
            {dirty && (
              <Button type="button" variant="outline" onClick={() => setEdits(null)} disabled={saveMutation.isPending}>
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
          </div>
        )}
      </div>

      <Alert>
        <Info className="h-4 w-4" />
        <AlertDescription>{t("settings:access.scopeNotice")}</AlertDescription>
      </Alert>

      {!canManage && (
        <Alert variant="warning">
          <AlertDescription>{t("settings:access.readOnly")}</AlertDescription>
        </Alert>
      )}

      {isLoading ? (
        <div className="space-y-4">
          <Skeleton className="h-40 w-full" />
          <Skeleton className="h-48 w-full" />
          <Skeleton className="h-28 w-full" />
        </div>
      ) : isError || !draft ? (
        <Alert variant="destructive">
          <AlertDescription>{t("settings:access.loadError")}</AlertDescription>
        </Alert>
      ) : (
        ACCESS_SETTING_GROUPS.map(({ group, keys }) => (
          <Card key={group}>
            <CardHeader className="pb-2">
              <CardTitle className="text-base">{t(`settings:access.groups.${group}`)}</CardTitle>
            </CardHeader>
            <CardContent className="divide-y divide-border">
              {keys.map((key) => (
                <div key={key} className="flex items-start justify-between gap-6 py-4 first:pt-0 last:pb-0">
                  <div className="space-y-1">
                    <Label htmlFor={`access-${key}`} className="text-sm font-medium">
                      {t(`settings:access.keys.${key}.label`)}
                    </Label>
                    <p className="text-sm text-muted-foreground">{t(`settings:access.keys.${key}.help`)}</p>
                  </div>
                  <Switch
                    id={`access-${key}`}
                    checked={draft[key]}
                    onCheckedChange={(value) => toggle(key, value)}
                    disabled={!canManage || saveMutation.isPending}
                  />
                </div>
              ))}
            </CardContent>
          </Card>
        ))
      )}
    </div>
  );
}
