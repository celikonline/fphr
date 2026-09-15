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
  type NotificationPreferenceDto,
} from "@/services/notifications.service";

type Channel = "emailEnabled" | "inAppEnabled";

export default function NotificationPreferencesPage() {
  const canManage = usePermission("notifications.self.preferences");
  if (!canManage) return <NoAccess />;
  return <NotificationPreferences />;
}

function NotificationPreferences() {
  const { t } = useTranslation(["notifications", "common"]);
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: NOTIFICATION_PREFERENCES_KEY, queryFn: getNotificationPreferences });
  const [draft, setDraft] = useState<Record<string, NotificationPreferenceDto>>({});

  const rows = (query.data ?? []).map((row) => draft[row.templateKey] ?? row);
  const dirty = Object.keys(draft).length > 0;

  const save = useMutation({
    mutationFn: () =>
      updateNotificationPreferences(
        rows.map(({ templateKey, emailEnabled, inAppEnabled }) => ({ templateKey, emailEnabled, inAppEnabled }))
      ),
    onSuccess: () => {
      setDraft({});
      queryClient.invalidateQueries({ queryKey: NOTIFICATION_PREFERENCES_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("notifications:preferences.saved") });
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("common:errorGeneric")) }),
  });

  function toggle(row: NotificationPreferenceDto, channel: Channel, value: boolean) {
    setDraft((current) => ({ ...current, [row.templateKey]: { ...row, [channel]: value } }));
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
        <CardContent className="p-0">
          {query.isLoading ? (
            <div className="space-y-2 p-4">
              {Array.from({ length: 4 }, (_, i) => (
                <Skeleton key={i} className="h-10 w-full" />
              ))}
            </div>
          ) : query.isError ? (
            <p className="p-4 text-sm text-destructive">{t("notifications:preferences.loadError")}</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t("notifications:preferences.notification")}</TableHead>
                  <TableHead className="w-28 text-center">{t("notifications:preferences.email")}</TableHead>
                  <TableHead className="w-28 text-center">{t("notifications:preferences.inApp")}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.map((row) => (
                  <TableRow key={row.templateKey}>
                    <TableCell>
                      <div className="font-medium">
                        {t(`notifications:templates.${row.templateKey}.title`, { defaultValue: row.templateKey })}
                      </div>
                      <div className="flex items-center gap-2 text-xs text-muted-foreground">
                        {t(`notifications:category.${row.category}`, { defaultValue: row.category })}
                        {row.isMandatory && <Badge variant="secondary">{t("notifications:preferences.mandatory")}</Badge>}
                      </div>
                    </TableCell>
                    <TableCell className="text-center">
                      <Switch
                        checked={row.emailEnabled}
                        disabled={row.isMandatory}
                        onCheckedChange={(value) => toggle(row, "emailEnabled", value)}
                        aria-label={t("notifications:preferences.email")}
                      />
                    </TableCell>
                    <TableCell className="text-center">
                      <Switch
                        checked={row.inAppEnabled}
                        disabled={row.isMandatory}
                        onCheckedChange={(value) => toggle(row, "inAppEnabled", value)}
                        aria-label={t("notifications:preferences.inApp")}
                      />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      <div className="flex justify-end">
        <Button onClick={() => save.mutate()} disabled={!dirty || save.isPending}>
          {t("notifications:preferences.save")}
        </Button>
      </div>
    </div>
  );
}
