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
                    loading={remove.isPending && remove.variables === note.id}
                    onClick={() => {
                      if (window.confirm(t("employees:employeeNotes.confirmDelete")))
                        remove.mutate(note.id);
                    }}
                  >
                    {t("employees:employeeNotes.delete")}
                  </Button>
                </Group>
              )}
            </Group>
          </Card>
        ))}
      </Stack>
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
  const [title, setTitle] = useState(note?.title ?? "");
  const [content, setContent] = useState(note?.content ?? "");
  const [importance, setImportance] = useState<EmployeeNoteImportance>(note?.importance ?? "normal");
  const [file, setFile] = useState<File | null>(null);
  const [removeFile, setRemoveFile] = useState(false);

  const save = useMutation({
    mutationFn: async () => {
      const input = { title: title.trim(), content, importance };
      const saved = note
        ? await updateEmployeeNote(note.id, input)
        : await createEmployeeNote(employeeId, input);
      if (file) await uploadNoteAttachment(saved.id, file);
      else if (removeFile && note) await deleteNoteAttachment(note.id);
    },
    onSuccess: () => {
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("employees:employeeNotes.saved"),
      });
      onSaved();
    },
    onError,
  });

  return (
    <Modal
      opened
      onClose={onClose}
      title={t(note ? "employees:employeeNotes.edit" : "employees:employeeNotes.add")}
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
        <Input.Wrapper id="employee-note-file" label={t("employees:employeeNotes.attach")}>
          <Input
            id="employee-note-file"
            type="file"
            onChange={(event: React.ChangeEvent<HTMLInputElement>) =>
              setFile(event.currentTarget.files?.[0] ?? null)
            }
          />
        </Input.Wrapper>
        {note?.attachment && !file && !removeFile && (
          <Group gap="xs">
            <Text size="sm">{note.attachment.fileName}</Text>
            <Button size="compact-xs" variant="default" onClick={() => setRemoveFile(true)}>
              {t("employees:employeeNotes.removeAttachment")}
            </Button>
          </Group>
        )}
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            {t("employees:employeeNotes.cancel")}
          </Button>
          <Button onClick={() => save.mutate()} disabled={!title.trim()} loading={save.isPending}>
            {t("employees:employeeNotes.save")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
