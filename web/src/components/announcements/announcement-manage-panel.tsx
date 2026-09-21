import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus } from "lucide-react";
import { Badge, Button, Card, Group, Input, Modal, Skeleton, Stack, Table, Text } from "@mantine/core";
import ConfirmDialog from "@/components/confirm-dialog";
import { DatePicker } from "@/components/ui/date-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateTime } from "@/lib/dates";
import AnnouncementFormDialog from "@/components/announcements/announcement-form-dialog";
import {
  archiveAnnouncement,
  enumKey,
  getAnnouncementReadCount,
  listAnnouncements,
  publishAnnouncement,
  type Announcement,
  type AnnouncementStatus,
} from "@/services/announcements.service";

const LIST_QUERY_KEY = ["announcements", "list"] as const;

const STATUS_COLORS: Record<AnnouncementStatus, string> = {
  draft: "gray",
  published: "green",
  expired: "yellow",
  archived: "dark",
};

/** End of the chosen local day as an ISO instant, so "expires on 30.09" keeps it visible through that day. */
function endOfDayIso(date: string): string {
  return new Date(`${date}T23:59:59`).toISOString();
}

/** İK duyuru yönetimi: tüm duyurular, oluştur, yayınla (isteğe bağlı bitiş tarihiyle), arşivle, okunma sayısı. */
export default function AnnouncementManagePanel() {
  const { t, i18n } = useTranslation(["announcements", "common"]);
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);
  const [publishTarget, setPublishTarget] = useState<Announcement | null>(null);
  const [expiresOn, setExpiresOn] = useState("");
  const [archiveTarget, setArchiveTarget] = useState<Announcement | null>(null);

  const listQuery = useQuery({ queryKey: LIST_QUERY_KEY, queryFn: listAnnouncements });

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ["announcements"] });
  }

  function showError(error: unknown) {
    toast({
      variant: "destructive",
      title: t("common:error"),
      description: getApiErrorMessage(error, t("announcements:manage.actionFailed")),
    });
  }

  const publishMutation = useMutation({
    mutationFn: ({ id, expires }: { id: string; expires: string }) =>
      publishAnnouncement(id, expires ? endOfDayIso(expires) : null),
    onSuccess: () => {
      toast({ variant: "success", title: t("common:success"), description: t("announcements:manage.published") });
      setPublishTarget(null);
      invalidate();
    },
    onError: showError,
  });

  const archiveMutation = useMutation({
    mutationFn: (id: string) => archiveAnnouncement(id),
    onSuccess: () => {
      toast({ variant: "success", title: t("common:success"), description: t("announcements:manage.archived") });
      setArchiveTarget(null);
      invalidate();
    },
    onError: showError,
  });

  const rows = listQuery.data ?? [];

  return (
    <Stack gap="md">
      <Group justify="flex-end">
        <Button leftSection={<Plus size={16} />} onClick={() => setCreateOpen(true)}>
          {t("announcements:manage.create")}
        </Button>
      </Group>

      {listQuery.isLoading && <Skeleton height={160} />}
      {listQuery.isError && <Text c="red">{t("announcements:manage.loadError")}</Text>}
      {listQuery.isSuccess && rows.length === 0 && (
        <Card withBorder>
          <Text c="dimmed">{t("announcements:manage.empty")}</Text>
        </Card>
      )}
      {rows.length > 0 && (
        <Table.ScrollContainer minWidth={720}>
          <Table striped highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("announcements:fields.title")}</Table.Th>
                <Table.Th>{t("announcements:fields.type")}</Table.Th>
                <Table.Th>{t("announcements:fields.status")}</Table.Th>
                <Table.Th>{t("announcements:fields.publishedAt")}</Table.Th>
                <Table.Th>{t("announcements:fields.readers")}</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {rows.map((row) => {
                const status = enumKey<AnnouncementStatus>(row.status);
                return (
                  <Table.Tr key={row.id}>
                    <Table.Td>{row.title}</Table.Td>
                    <Table.Td>{t(`announcements:types.${enumKey(row.type)}`)}</Table.Td>
                    <Table.Td>
                      <Badge color={STATUS_COLORS[status] ?? "gray"} variant="light">
                        {t(`announcements:statuses.${status}`)}
                      </Badge>
                    </Table.Td>
                    <Table.Td>{row.publishedAt ? formatDateTime(row.publishedAt, i18n.language) : "—"}</Table.Td>
                    <Table.Td>{status === "draft" ? "—" : <ReadCount id={row.id} />}</Table.Td>
                    <Table.Td>
                      <Group gap="xs" justify="flex-end" wrap="nowrap">
                        {status === "draft" && (
                          <Button
                            size="compact-sm"
                            variant="light"
                            onClick={() => {
                              setExpiresOn("");
                              setPublishTarget(row);
                            }}
                          >
                            {t("announcements:manage.publish")}
                          </Button>
                        )}
                        {status !== "archived" && (
                          <Button size="compact-sm" variant="subtle" color="red" onClick={() => setArchiveTarget(row)}>
                            {t("announcements:manage.archive")}
                          </Button>
                        )}
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                );
              })}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}

      <AnnouncementFormDialog open={createOpen} onClose={() => setCreateOpen(false)} onCreated={invalidate} />

      <Modal
        opened={publishTarget !== null}
        onClose={() => setPublishTarget(null)}
        title={t("announcements:manage.publishTitle", { title: publishTarget?.title ?? "" })}
        centered
      >
        <Stack gap="md">
          <Input.Wrapper label={t("announcements:fields.expiresAt")} description={t("announcements:manage.expiresHint")}>
            <DatePicker id="announcement-expires" value={expiresOn} onChange={setExpiresOn} clearable />
          </Input.Wrapper>
          <Group justify="flex-end" gap="sm">
            <Button variant="default" onClick={() => setPublishTarget(null)} disabled={publishMutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button
              loading={publishMutation.isPending}
              onClick={() => publishTarget && publishMutation.mutate({ id: publishTarget.id, expires: expiresOn })}
            >
              {t("announcements:manage.publish")}
            </Button>
          </Group>
        </Stack>
      </Modal>

      <ConfirmDialog
        open={archiveTarget !== null}
        onOpenChange={(open) => !open && setArchiveTarget(null)}
        title={t("announcements:manage.archiveTitle")}
        description={t("announcements:manage.archiveDescription", { title: archiveTarget?.title ?? "" })}
        confirmLabel={t("announcements:manage.archive")}
        onConfirm={() => archiveTarget && archiveMutation.mutate(archiveTarget.id)}
        isLoading={archiveMutation.isPending}
      />
    </Stack>
  );
}

function ReadCount({ id }: { id: string }) {
  const query = useQuery({ queryKey: ["announcements", "read-count", id], queryFn: () => getAnnouncementReadCount(id) });
  return <>{query.data ?? "…"}</>;
}
