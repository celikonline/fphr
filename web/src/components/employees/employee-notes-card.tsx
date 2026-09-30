import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Anchor,
  Badge,
  Button,
  Card,
  Group,
  Input,
  Modal,
  NativeSelect,
  Skeleton,
  Stack,
  Text,
  Textarea,
  TextInput,
  Title,
} from "@mantine/core";
import { usePermission } from "@/components/permission-guard";
import { ConfirmModal } from "@/components/ui/confirm-modal";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  NOTE_CONTENT_MAX,
  NOTE_IMPORTANCES,
  NOTE_TITLE_MAX,
  createEmployeeNote,
  deleteEmployeeNote,
  deleteNoteAttachment,
  listEmployeeNotes,
  updateEmployeeNote,
  uploadNoteAttachment,
  validateNoteAttachment,
  type EmployeeNote,
  type EmployeeNoteImportance,
} from "@/services/employee-notes.service";

const IMPORTANCE_COLOR: Record<EmployeeNoteImportance, string> = {
  low: "gray",
  normal: "blue",
  high: "red",
};

/** "Çalışan notları" (E7): visible only with employee.notes.view; the self-service (my) permissions never grant it. */
export function EmployeeNotesCard({ employeeId }: { employeeId: string }) {
  const { t } = useTranslation(["employees", "common"]);
  const canView = usePermission("employee.notes.view");
  const canManage = usePermission("employee.notes.manage");
  const queryClient = useQueryClient();
  // undefined = dialog closed, null = creating, note = editing.
  const [editing, setEditing] = useState<EmployeeNote | null | undefined>(undefined);
  const [deleting, setDeleting] = useState<EmployeeNote | null>(null);

  const query = useQuery({
    queryKey: ["employee-notes", employeeId],
    queryFn: () => listEmployeeNotes(employeeId),
    enabled: canView,
  });
  const notifyError = (error: unknown) =>
    toast({
      variant: "destructive",
      title: t("common:error"),
      description: getApiErrorMessage(error, t("employees:employeeNotes.actionFailed")),
    });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ["employee-notes", employeeId] });
  const remove = useMutation({
    mutationFn: deleteEmployeeNote,
    onSuccess: () => {
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("employees:employeeNotes.deleted"),
      });
      void refresh();
    },
    onError: notifyError,
  });

  if (!canView) return null;

  const notes = query.data?.items ?? [];
  const truncated = (query.data?.totalCount ?? 0) > notes.length;

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" mb="md">
        <Title order={4}>{t("employees:employeeNotes.title")}</Title>
        {canManage && (
          <Button size="xs" onClick={() => setEditing(null)}>
            {t("employees:employeeNotes.add")}
          </Button>
        )}
      </Group>
      {query.isLoading && <Skeleton h={64} />}
      {query.isError && (
        <Text c="red" size="sm">
          {t("employees:employeeNotes.loadError")}
        </Text>
      )}
      {query.isSuccess && notes.length === 0 && (
        <Text size="sm" c="dimmed">
          {t("employees:employeeNotes.empty")}
        </Text>
      )}
      {truncated && (
        <Text size="xs" c="dimmed" mb="xs">
          {t("employees:employeeNotes.truncated", { count: notes.length })}
        </Text>
      )}
      <Stack gap="sm">
        {notes.map((note) => (
          <Card key={note.id} withBorder padding="sm">
            <Group justify="space-between" align="flex-start" wrap="nowrap">
              <Stack gap={2}>
                <Group gap="xs">
                  <Text fw={600}>{note.title}</Text>
                  <Badge color={IMPORTANCE_COLOR[note.importance]} variant="light">
                    {t(`employees:employeeNotes.importances.${note.importance}`)}
                  </Badge>
                </Group>
                {note.content && <Text size="sm">{note.content}</Text>}
                {note.attachment && (
                  <Anchor href={note.attachment.downloadUrl} target="_blank" rel="noreferrer" size="sm">
                    {note.attachment.fileName}
                  </Anchor>
                )}
                <Text size="xs" c="dimmed">
                  {[note.authorName, new Date(note.createdAt).toLocaleString()]
                    .filter(Boolean)
                    .join(" · ")}
                </Text>
              </Stack>
              {canManage && (
                <Group gap="xs" wrap="nowrap">
                  <Button size="compact-xs" variant="default" onClick={() => setEditing(note)}>
                    {t("employees:employeeNotes.edit")}
                  </Button>
                  <Button
                    size="compact-xs"
                    variant="default"
                    color="red"
                    onClick={() => setDeleting(note)}
                  >
                    {t("employees:employeeNotes.delete")}
                  </Button>
                </Group>
              )}
            </Group>
          </Card>
        ))}
      </Stack>
      <ConfirmModal
        opened={deleting !== null}
        onClose={() => setDeleting(null)}
        title={t("employees:employeeNotes.delete")}
        message={t("employees:employeeNotes.confirmDelete")}
        confirmLabel={t("employees:employeeNotes.delete")}
        danger
        onConfirm={async () => {
          if (deleting) await remove.mutateAsync(deleting.id).catch(() => undefined);
          setDeleting(null);
        }}
      />
      {editing !== undefined && (
        <NoteDialog
          employeeId={employeeId}
          note={editing}
          onClose={() => setEditing(undefined)}
          onSaved={() => {
            setEditing(undefined);
            void refresh();
          }}
          onError={notifyError}
        />
      )}
    </Card>
  );
}

interface NoteDialogProps {
  employeeId: string;
  note: EmployeeNote | null;
  onClose: () => void;
  onSaved: () => void;
  onError: (error: unknown) => void;
}

function NoteDialog({ employeeId, note, onClose, onSaved, onError }: NoteDialogProps) {
  const { t } = useTranslation(["employees", "common"]);
  const queryClient = useQueryClient();
  // Becomes the created note when its attachment upload fails, so a retry updates instead of creating a duplicate.
  const [current, setCurrent] = useState<EmployeeNote | null>(note);
  const [title, setTitle] = useState(note?.title ?? "");
  const [content, setContent] = useState(note?.content ?? "");
  const [importance, setImportance] = useState<EmployeeNoteImportance>(note?.importance ?? "normal");
  const [file, setFile] = useState<File | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const [removeFile, setRemoveFile] = useState(false);

  const pickFile = (picked: File | null) => {
    const problem = picked ? validateNoteAttachment(picked) : null;
    setFileError(problem ? t(`employees:employeeNotes.fileInvalid.${problem}`) : null);
    setFile(picked && !problem ? picked : null);
  };

  const save = useMutation({
    mutationFn: async () => {
      const input = { title: title.trim(), content, importance };
      const saved = current
        ? await updateEmployeeNote(current.id, input)
        : await createEmployeeNote(employeeId, input);
      setCurrent(saved);
      if (file) {
        try {
          await uploadNoteAttachment(saved.id, file);
        } catch (error) {
          setFile(null);
          void queryClient.invalidateQueries({ queryKey: ["employee-notes", employeeId] });
          throw new AttachmentUploadError(error);
        }
      } else if (removeFile && current?.attachment) {
        await deleteNoteAttachment(current.id);
      }
    },
    onSuccess: () => {
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("employees:employeeNotes.saved"),
      });
      onSaved();
    },
    onError: (error) => {
      if (error instanceof AttachmentUploadError) {
        toast({
          variant: "destructive",
          title: t("common:error"),
          description: t("employees:employeeNotes.attachmentFailed"),
        });
        return;
      }
      onError(error);
    },
  });

  return (
    <Modal
      opened
      onClose={onClose}
      title={t(current ? "employees:employeeNotes.edit" : "employees:employeeNotes.add")}
    >
      <Stack gap="sm">
        <TextInput
          label={t("employees:employeeNotes.noteTitle")}
          value={title}
          maxLength={NOTE_TITLE_MAX}
          onChange={(event) => setTitle(event.currentTarget.value)}
          required
        />
        <Textarea
          label={t("employees:employeeNotes.content")}
          value={content}
          maxLength={NOTE_CONTENT_MAX}
          autosize
          minRows={3}
          onChange={(event) => setContent(event.currentTarget.value)}
        />
        <NativeSelect
          label={t("employees:employeeNotes.importance")}
          value={importance}
          onChange={(event) => setImportance(event.currentTarget.value as EmployeeNoteImportance)}
          data={NOTE_IMPORTANCES.map((value) => ({
            value,
            label: t(`employees:employeeNotes.importances.${value}`),
          }))}
        />
        <Input.Wrapper
          id="employee-note-file"
          label={t("employees:employeeNotes.attach")}
          error={fileError}
        >
          <Input
            id="employee-note-file"
            type="file"
            accept=".pdf,.png,.jpg,.jpeg,.txt,.docx,.xlsx"
            onChange={(event: React.ChangeEvent<HTMLInputElement>) =>
              pickFile(event.currentTarget.files?.[0] ?? null)
            }
          />
        </Input.Wrapper>
        {current?.attachment && !file && !removeFile && (
          <Group gap="xs">
            <Text size="sm">{current.attachment.fileName}</Text>
            <Button size="compact-xs" variant="default" onClick={() => setRemoveFile(true)}>
              {t("employees:employeeNotes.removeAttachment")}
            </Button>
          </Group>
        )}
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            {t("employees:employeeNotes.cancel")}
          </Button>
          <Button
            onClick={() => save.mutate()}
            disabled={!title.trim() || save.isPending}
            loading={save.isPending}
          >
            {t("employees:employeeNotes.save")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

/** Marks a failure of the attachment step after the note itself was already saved. */
class AttachmentUploadError extends Error {
  constructor(readonly cause: unknown) {
    super("attachment upload failed");
  }
}
