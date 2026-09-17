import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMediaQuery } from "@mantine/hooks";
import { Badge, Button, Card, Group, Skeleton, Stack, Switch, Table, Text, Title } from "@mantine/core";
import { BellRing } from "lucide-react";
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
  // ≤390px: kaydet butonu tam genişlik (Faz 1 tur 3 FE, bildirim tercihleri mobil uyumu).
  const isMobile = useMediaQuery("(max-width: 390px)");
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
        <Title order={2} className="flex items-center gap-2">
          <BellRing size={24} /> {t("notifications:preferences.title")}
        </Title>
        <Text c="dimmed">{t("notifications:preferences.subtitle")}</Text>
      </div>

      <Card withBorder padding={0}>
        {query.isLoading ? (
          <Stack gap="xs" p="md">
            {Array.from({ length: 4 }, (_, i) => (
              <Skeleton key={i} h={40} />
            ))}
          </Stack>
        ) : query.isError ? (
          <Text size="sm" c="red" p="md">
            {t("notifications:preferences.loadError")}
          </Text>
        ) : rows.length === 0 ? (
          <Text size="sm" c="dimmed" p="md">
            {t("notifications:preferences.empty")}
          </Text>
        ) : (
          <>
            {/* Geniş ekran: tablo */}
            <div className="hidden md:block">
              <Table.ScrollContainer minWidth={480}>
                <Table verticalSpacing="sm" horizontalSpacing="md">
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>{t("notifications:preferences.notification")}</Table.Th>
                      {CHANNELS.map(({ channel, labelKey }) => (
                        <Table.Th key={channel} w={112} ta="center">
                          {t(labelKey)}
                        </Table.Th>
                      ))}
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {rows.map((row) => (
                      <Table.Tr key={row.templateKey}>
                        <Table.Td>
                          <Text size="sm" fw={500}>
                            {t(`notifications:template.${row.templateKey}`, { defaultValue: row.templateKey })}
                          </Text>
                          <Group gap="xs">
                            <Text size="xs" c="dimmed">
                              {t(`notifications:category.${row.category}`, { defaultValue: row.category })}
                            </Text>
                            {row.isMandatory && (
                              <Badge variant="light" color="gray" size="sm">
                                {t("notifications:preferences.mandatory")}
                              </Badge>
                            )}
                          </Group>
                        </Table.Td>
                        {CHANNELS.map(({ channel, labelKey }) => {
                          const setting = row.channels.find((c) => c.channel === channel);
                          return (
                            <Table.Td key={channel}>
                              {setting ? (
                                <Group justify="center">
                                  <Switch
                                    checked={isEnabled(row, channel, setting.enabled)}
                                    disabled={!canManage || setting.locked || row.isMandatory}
                                    onChange={(event) => toggle(row, channel, event.currentTarget.checked)}
                                    aria-label={t(labelKey)}
                                  />
                                </Group>
                              ) : (
                                <Text span c="dimmed" display="block" ta="center">
                                  -
                                </Text>
                              )}
                            </Table.Td>
                          );
                        })}
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              </Table.ScrollContainer>
            </div>

            {/* Dar ekran (≤390px dahil): olay türü başına kart */}
            <div className="md:hidden">
              <Stack gap="sm" p="md">
                {rows.map((row) => (
                  <Card key={row.templateKey} withBorder padding="sm" radius="md">
                    <Text size="sm" fw={500}>
                      {t(`notifications:template.${row.templateKey}`, { defaultValue: row.templateKey })}
                    </Text>
                    <Group gap="xs" mb="xs">
                      <Text size="xs" c="dimmed">
                        {t(`notifications:category.${row.category}`, { defaultValue: row.category })}
                      </Text>
                      {row.isMandatory && (
                        <Badge variant="light" color="gray" size="sm">
                          {t("notifications:preferences.mandatory")}
                        </Badge>
                      )}
                    </Group>
                    <Stack gap={0}>
                      {CHANNELS.map(({ channel, labelKey }) => {
                        const setting = row.channels.find((c) => c.channel === channel);
                        if (!setting) return null;
                        return (
                          <Group key={channel} justify="space-between" wrap="nowrap" style={{ minHeight: 40 }}>
                            <Text size="sm">{t(labelKey)}</Text>
                            <Switch
                              checked={isEnabled(row, channel, setting.enabled)}
                              disabled={!canManage || setting.locked || row.isMandatory}
                              onChange={(event) => toggle(row, channel, event.currentTarget.checked)}
                              aria-label={t(labelKey)}
                            />
                          </Group>
                        );
                      })}
                    </Stack>
                  </Card>
                ))}
              </Stack>
            </div>
          </>
        )}
      </Card>

      {canManage && (
        <Group justify="flex-end">
          <Button
            fullWidth={isMobile}
            onClick={() => save.mutate()}
            disabled={changes.length === 0 || save.isPending}
          >
            {t("notifications:preferences.save")}
          </Button>
        </Group>
      )}
    </div>
  );
}
