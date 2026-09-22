import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ActionIcon, Group, List, Text } from "@mantine/core";
import { FileText, Trash2 } from "lucide-react";
import { FileDropzone } from "@/components/ui/file-dropzone";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  deleteAnnouncementAttachment,
  listAnnouncementAttachments,
  uploadAnnouncementAttachment,
} from "@/services/announcements.service";

const MAX_FILE_BYTES = 10 * 1024 * 1024;
const ALLOWED_FILE_TYPES = ".pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt";

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** İndirilebilir dosya ekleri (gömülü resimler hariç - onlar editörün kendi içinde yönetilir). Yalnızca kaydedilmiş (id'si olan) bir taslak için gösterilir. */
export function AnnouncementAttachmentsPanel({ announcementId }: { announcementId: string }) {
  const { t } = useTranslation(["announcements", "common"]);
  const queryClient = useQueryClient();
  const [uploading, setUploading] = useState(false);
  const queryKey = ["announcements", announcementId, "attachments"] as const;

  const attachmentsQuery = useQuery({
    queryKey,
    queryFn: () => listAnnouncementAttachments(announcementId),
  });
  const files = (attachmentsQuery.data ?? []).filter((attachment) => attachment.kind === "file");

  const uploadMutation = useMutation({
    mutationFn: (file: File) => uploadAnnouncementAttachment(announcementId, file, "file"),
    onMutate: () => setUploading(true),
    onSettled: () => setUploading(false),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey }),
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("announcements:editor.fileUploadFailed")),
      }),
  });

  const deleteMutation = useMutation({
    mutationFn: (attachmentId: string) =>
      deleteAnnouncementAttachment(announcementId, attachmentId),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey }),
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("announcements:editor.attachmentDeleteFailed")),
      }),
  });

  return (
    <div>
      <Text size="sm" fw={500} mb="xs">
        {t("announcements:editor.attachments")}
      </Text>
      {files.length > 0 && (
        <List spacing={4} size="sm" mb="sm">
          {files.map((file) => (
            <List.Item key={file.id} icon={<FileText size={16} />}>
              <Group gap="xs" justify="space-between" wrap="nowrap">
                <Text
                  size="sm"
                  style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}
                >
                  {file.fileName}{" "}
                  <Text component="span" size="xs" c="dimmed">
                    ({formatSize(file.sizeBytes)})
                  </Text>
                </Text>
                <ActionIcon
                  variant="subtle"
                  color="red"
                  size="sm"
                  aria-label={t("announcements:editor.removeAttachment")}
                  loading={deleteMutation.isPending && deleteMutation.variables === file.id}
                  onClick={() => deleteMutation.mutate(file.id)}
                >
                  <Trash2 size={14} />
                </ActionIcon>
              </Group>
            </List.Item>
          ))}
        </List>
      )}
      <FileDropzone
        accept={ALLOWED_FILE_TYPES}
        maxBytes={MAX_FILE_BYTES}
        busy={uploading}
        onFile={(file) => uploadMutation.mutate(file)}
        onReject={(reason) =>
          toast({
            variant: "destructive",
            title: t("common:error"),
            description: t(
              reason === "size"
                ? "announcements:editor.fileTooLarge"
                : "announcements:editor.fileTypeNotAllowed"
            ),
          })
        }
        title={t("announcements:editor.dropFileHere")}
        hint={t("announcements:editor.allowedFileTypes")}
      />
    </div>
  );
}
