import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BellRing } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  NOTIFICATION_PREFERENCES_KEY,
  getNotificationPreferences,
  updateNotificationPreferences,
  type NotificationChannel,
  type NotificationPreferenceDto,
  type NotificationPreferenceInput,
} from "@/services/notifications.service";

const CHANNELS: { channel: NotificationChannel; labelKey: string }[] = [
  { channel: "email", labelKey: "notifications:preferences.email" },
  { channel: "in_app", labelKey: "notifications:preferences.inApp" },
];

const draftKey = (templateKey: string, channel: NotificationChannel) => `${templateKey}|${channel}`;

export default function NotificationPreferencesPage() {
  const canView = usePermission("notifications.my.view");
  if (!canView) return <NoAccess />;
  return <NotificationPreferences />;
}

function NotificationPreferences() {
  const { t } = useTranslation(["notifications", "common"]);
  const queryClient = useQueryClient();
  const canManage = usePermission("notifications.my.manage");
  const query = useQuery({ queryKey: NOTIFICATION_PREFERENCES_KEY, queryFn: getNotificationPreferences });
  const [draft, setDraft] = useState<Record<string, NotificationPreferenceInput>>({});

  const rows = query.data ?? [];
  const changes = Object.values(draft).filter((item) => {
    const row = rows.find((r) => r.templateKey === item.templateKey);
    return row?.channels.find((c) => c.channel === item.channel)?.enabled !== item.enabled;
  });

  const save = useMutation({
    mutationFn: () => updateNotificationPreferences(changes),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: NOTIFICATION_PREFERENCES_KEY });
      setDraft({});
      toast({ variant: "success", title: t("common:success"), description: t("notifications:preferences.saved") });
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("common:errorGeneric")) }),
  });

  function isEnabled(row: NotificationPreferenceDto, channel: NotificationChannel, fallback: boolean) {
    return draft[draftKey(row.templateKey, channel)]?.enabled ?? fallback;
  }

  function toggle(row: NotificationPreferenceDto, channel: NotificationChannel, enabled: boolean) {
    setDraft((current) => ({
      ...current,
      [draftKey(row.templateKey, channel)]: { templateKey: row.templateKey, channel, enabled },
    }));
  }

  return (
    <div className="mx-auto max-w-4xl space-y-6 p-6 md:p-10">
      <div>
        <h1 className="flex items-center gap-2 text-2xl font-bold">
          <BellRing className="h-6 w-6" /> {t("notifications:preferences.title")}
        </h1>
        <p className="text-muted-foreground">{t("notifications:preferences.subtitle")}</p>
      </div>

      <Card>
        <CardContent className="overflow-x-auto p-0">
          {query.isLoading ? (
            <div className="space-y-2 p-4">
              {Array.from({ length: 4 }, (_, i) => (
                <Skeleton key={i} className="h-10 w-full" />
              ))}
            </div>
          ) : query.isError ? (
            <p className="p-4 text-sm text-destructive">{t("notifications:preferences.loadError")}</p>
          ) : rows.length === 0 ? (
            <p className="p-4 text-sm text-muted-foreground">{t("notifications:preferences.empty")}</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t("notifications:preferences.notification")}</TableHead>
                  {CHANNELS.map(({ channel, labelKey }) => (
                    <TableHead key={channel} className="w-28 text-center">
                      {t(labelKey)}
                    </TableHead>
                  ))}
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.map((row) => (
                  <TableRow key={row.templateKey}>
                    <TableCell>
                      <div className="font-medium">
                        {t(`notifications:template.${row.templateKey}`, { defaultValue: row.templateKey })}
                      </div>
                      <div className="flex items-center gap-2 text-xs text-muted-foreground">
                        {t(`notifications:category.${row.category}`, { defaultValue: row.category })}
                        {row.isMandatory && <Badge variant="secondary">{t("notifications:preferences.mandatory")}</Badge>}
                      </div>
                    </TableCell>
                    {CHANNELS.map(({ channel, labelKey }) => {
                      const setting = row.channels.find((c) => c.channel === channel);
                      return (
                        <TableCell key={channel} className="text-center">
                          {setting ? (
                            <Switch
                              checked={isEnabled(row, channel, setting.enabled)}
                              disabled={!canManage || setting.locked || row.isMandatory}
                              onCheckedChange={(value) => toggle(row, channel, value)}
                              aria-label={t(labelKey)}
                            />
                          ) : (
                            <span className="text-muted-foreground">-</span>
                          )}
                        </TableCell>
                      );
                    })}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      {canManage && (
        <div className="flex justify-end">
          <Button onClick={() => save.mutate()} disabled={changes.length === 0 || save.isPending}>
            {t("notifications:preferences.save")}
          </Button>
        </div>
      )}
    </div>
  );
}
