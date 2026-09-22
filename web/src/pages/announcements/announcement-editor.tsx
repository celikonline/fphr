import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import { ArrowLeft, Save } from "lucide-react";
import { Button, Card, Group, Select, Stack, Text, TextInput, Title } from "@mantine/core";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { AnnouncementAttachmentsPanel } from "@/components/announcements/announcement-attachments-panel";
import { AnnouncementRichEditor } from "@/components/announcements/announcement-rich-editor";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  ANNOUNCEMENT_PRIORITIES,
  ANNOUNCEMENT_TYPES,
  createAnnouncement,
  enumKey,
  getAnnouncement,
  updateAnnouncement,
  type Announcement,
  type AnnouncementPriority,
  type AnnouncementType,
} from "@/services/announcements.service";

const TITLE_MAX_LENGTH = 200;

/**
 * Tam sayfa duyuru editörü ("Yeni duyuru" ve mevcut taslağı düzenleme, aynı ekran). Bir modal yerine kendi rotası
 * var (`/app/announcements/new`, `/app/announcements/:id/edit`) çünkü zengin metin editörü + resim/dosya yükleme
 * bir modala sığacak kadar küçük değil. İlk kayıttan (id atanana kadar) sonra editör içi resim yükleme ve dosya eki
 * paneli açılır - ikisi de bir `AnnouncementAttachment` id'sine ihtiyaç duyar (bkz. announcement-rich-editor.tsx).
 */
export default function AnnouncementEditorPage() {
  const canManage = usePermission("announcements.announcements.manage");
  if (!canManage) return <NoAccess />;
  return <AnnouncementEditorScreen />;
}

function AnnouncementEditorScreen() {
  const { id } = useParams<{ id?: string }>();
  const isEditing = Boolean(id);
  const { t } = useTranslation(["announcements", "common"]);

  const announcementQuery = useQuery({
    queryKey: ["announcements", id],
    queryFn: () => getAnnouncement(id!),
    enabled: isEditing,
  });

  if (isEditing && announcementQuery.isLoading) {
    return (
      <Stack gap="md" p="md">
        <Text c="dimmed">{t("common:loading")}</Text>
      </Stack>
    );
  }

  if (isEditing && announcementQuery.isError) {
    return (
      <Stack gap="md" p="md">
        <Text c="red">{getApiErrorMessage(announcementQuery.error, t("announcements:manage.loadError"))}</Text>
      </Stack>
    );
  }

  // key={id} tekrar mount eder: her taslak kendi başlangıç state'iyle başlar, arkaplanda gelen bir yeniden
  // sorgulama kullanıcının o an yazdığını üzerine yazmaz (bkz. React hooks kuralı - efekt içinde setState yerine
  // lazy useState init).
  return <AnnouncementEditorForm key={id ?? "new"} id={id} initial={announcementQuery.data ?? null} />;
}

function AnnouncementEditorForm({ id, initial }: { id: string | undefined; initial: Announcement | null }) {
  const isEditing = Boolean(id);
  const navigate = useNavigate();
  const { t } = useTranslation(["announcements", "common"]);

  const [title, setTitle] = useState(initial?.title ?? "");
  const [content, setContent] = useState(initial?.content ?? "");
  const [type, setType] = useState<AnnouncementType>(initial ? enumKey<AnnouncementType>(initial.type) : "general");
  const [priority, setPriority] = useState<AnnouncementPriority>(initial ? enumKey<AnnouncementPriority>(initial.priority) : "normal");

  const createMutation = useMutation({
    mutationFn: () => createAnnouncement({ title: title.trim(), content, type, priority }),
    onSuccess: (newId) => {
      toast({ variant: "success", title: t("common:success"), description: t("announcements:manage.created") });
      navigate(`/app/announcements/${newId}/edit`, { replace: true });
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("announcements:manage.actionFailed")) }),
  });

  const updateMutation = useMutation({
    mutationFn: () => updateAnnouncement(id!, { title: title.trim(), content, type, priority }),
    onSuccess: () => toast({ variant: "success", title: t("common:success"), description: t("announcements:editor.saved") }),
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("announcements:manage.actionFailed")) }),
  });

  const saving = createMutation.isPending || updateMutation.isPending;
  const canSubmit = title.trim().length > 0 && content.trim().length > 0 && !saving;

  return (
    <Stack gap="md" p="md" maw={960}>
      <Link to="/app/announcements" className="inline-flex items-center gap-1 text-sm text-gray-500 hover:underline">
        <ArrowLeft size={14} /> {t("announcements:title")}
      </Link>

      <Title order={2}>{isEditing ? t("announcements:editor.editTitle") : t("announcements:editor.newTitle")}</Title>

      <Card withBorder padding="lg">
        <Stack gap="md">
          <TextInput
            label={t("announcements:fields.title")}
            value={title}
            maxLength={TITLE_MAX_LENGTH}
            required
            onChange={(event) => setTitle(event.currentTarget.value)}
          />

          <Group grow>
            <Select
              label={t("announcements:fields.type")}
              data={ANNOUNCEMENT_TYPES.map((value) => ({ value, label: t(`announcements:types.${value}`) }))}
              value={type}
              allowDeselect={false}
              onChange={(value) => value && setType(value as AnnouncementType)}
            />
            <Select
              label={t("announcements:fields.priority")}
              data={ANNOUNCEMENT_PRIORITIES.map((value) => ({ value, label: t(`announcements:priorities.${value}`) }))}
              value={priority}
              allowDeselect={false}
              onChange={(value) => value && setPriority(value as AnnouncementPriority)}
            />
          </Group>

          <div>
            <Text size="sm" fw={500} mb={4}>
              {t("announcements:fields.content")}
            </Text>
            <AnnouncementRichEditor announcementId={id ?? null} content={content} onChange={setContent} />
          </div>

          {id && <AnnouncementAttachmentsPanel announcementId={id} />}

          <Group justify="flex-end">
            <Button
              leftSection={<Save size={16} />}
              disabled={!canSubmit}
              loading={saving}
              onClick={() => (isEditing ? updateMutation.mutate() : createMutation.mutate())}
            >
              {t("announcements:manage.saveDraft")}
            </Button>
          </Group>
        </Stack>
      </Card>
    </Stack>
  );
}
