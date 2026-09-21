import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import { Button, Group, Modal, Select, Stack, TextInput, Textarea } from "@mantine/core";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  ANNOUNCEMENT_PRIORITIES,
  ANNOUNCEMENT_TYPES,
  createAnnouncement,
  type AnnouncementPriority,
  type AnnouncementType,
} from "@/services/announcements.service";

/** Server limits (Announcement.Create): title 1..200; content is capped server-side (AnnouncementsLimits). */
const TITLE_MAX_LENGTH = 200;

interface AnnouncementFormDialogProps {
  open: boolean;
  onClose: () => void;
  onCreated: () => void;
}

/** Yeni duyuru taslağı oluşturur; yayınlama ayrı bir adımdır (listede "Yayınla"). */
export default function AnnouncementFormDialog({ open, onClose, onCreated }: AnnouncementFormDialogProps) {
  const { t } = useTranslation(["announcements", "common"]);
  const [title, setTitle] = useState("");
  const [content, setContent] = useState("");
  const [type, setType] = useState<AnnouncementType>("general");
  const [priority, setPriority] = useState<AnnouncementPriority>("normal");

  function close() {
    setTitle("");
    setContent("");
    setType("general");
    setPriority("normal");
    onClose();
  }

  const createMutation = useMutation({
    mutationFn: () => createAnnouncement({ title: title.trim(), content: content.trim(), type, priority }),
    onSuccess: () => {
      toast({ variant: "success", title: t("common:success"), description: t("announcements:manage.created") });
      onCreated();
      close();
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("announcements:manage.actionFailed")),
      }),
  });

  const canSubmit = title.trim().length > 0 && content.trim().length > 0 && !createMutation.isPending;

  return (
    <Modal opened={open} onClose={close} title={t("announcements:manage.create")} centered size="lg">
      <Stack gap="md">
        <TextInput
          id="announcement-title"
          label={t("announcements:fields.title")}
          value={title}
          maxLength={TITLE_MAX_LENGTH}
          required
          onChange={(event) => setTitle(event.currentTarget.value)}
        />
        <Textarea
          id="announcement-content"
          label={t("announcements:fields.content")}
          value={content}
          autosize
          minRows={5}
          required
          onChange={(event) => setContent(event.currentTarget.value)}
        />
        <Group grow>
          <Select
            id="announcement-type"
            label={t("announcements:fields.type")}
            data={ANNOUNCEMENT_TYPES.map((value) => ({ value, label: t(`announcements:types.${value}`) }))}
            value={type}
            allowDeselect={false}
            onChange={(value) => value && setType(value as AnnouncementType)}
          />
          <Select
            id="announcement-priority"
            label={t("announcements:fields.priority")}
            data={ANNOUNCEMENT_PRIORITIES.map((value) => ({ value, label: t(`announcements:priorities.${value}`) }))}
            value={priority}
            allowDeselect={false}
            onChange={(value) => value && setPriority(value as AnnouncementPriority)}
          />
        </Group>
        <Group justify="flex-end" gap="sm">
          <Button variant="default" onClick={close} disabled={createMutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button onClick={() => createMutation.mutate()} disabled={!canSubmit} loading={createMutation.isPending}>
            {t("announcements:manage.saveDraft")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
