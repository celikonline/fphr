import { useRef, useState, type ChangeEvent, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Badge, Button, Card, Group, Input, Modal, Skeleton, Stack, Text, Textarea, Title } from "@mantine/core";
import { RefreshCw, Upload } from "lucide-react";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import { DatePicker } from "@/components/ui/date-picker";
import type { EmployeeCardTabProps } from "@/components/employees/card-tabs";
import {
  clearWaiveDocumentRequirement,
  getEmployeeDocumentRequirements,
  getMyDocumentRequirements,
  linkDocumentToRequirement,
  linkMyDocumentToRequirement,
  reevaluateEmployeeDocumentRequirements,
  uploadDocumentFile,
  waiveDocumentRequirement,
  type DocumentRequirementStatus,
  type EmployeeDocumentRequirement,
} from "@/services/documents.service";

export const DOCUMENTS_CARD_TAB_PERMISSIONS = ["documents.requirements.view", "documents.my.requirements"];

const STATUS_COLOR: Record<DocumentRequirementStatus, string> = {
  missing: "red",
  uploaded: "green",
  waived: "gray",
};

/** "Dosyalar" (Faz 4 D2): the employee's personnel document requirements, HR waive/clear-waive + reevaluate. */
export function DocumentsSection({ employee }: EmployeeCardTabProps) {
  const { t, i18n } = useTranslation(["documents", "common"]);
  const canManage = usePermission("documents.requirements.manage");
  const canViewAny = usePermission("documents.requirements.view");
  const canUploadHr = usePermission("documents.files.create") && canManage;
  const canUploadSelf = usePermission("documents.my.files.create");
  // A plain employee has "me" only; HR/admin roles' "documents.*" wildcard covers both, so "hr" wins.
  const uploadMode: "hr" | "me" | null = canUploadHr ? "hr" : canUploadSelf ? "me" : null;
  const queryClient = useQueryClient();
  const [waiving, setWaiving] = useState<EmployeeDocumentRequirement | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [uploadTarget, setUploadTarget] = useState<EmployeeDocumentRequirement | null>(null);
  const [pendingUpload, setPendingUpload] = useState<{ requirement: EmployeeDocumentRequirement; file: File } | null>(null);

  const queryKey = ["documents", "employee-requirements", employee.id, canViewAny] as const;
  const { data: requirements = [], isLoading, isError } = useQuery({
    queryKey,
    queryFn: () => (canViewAny ? getEmployeeDocumentRequirements(employee.id) : getMyDocumentRequirements()),
  });

  const clearWaiveMutation = useMutation({
    mutationFn: (requirement: EmployeeDocumentRequirement) => clearWaiveDocumentRequirement(requirement.id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey });
      toast({ variant: "success", title: t("common:success"), description: t("documents:requirements.saved") });
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("documents:requirements.errors.generic")) }),
  });

  const reevaluateMutation = useMutation({
    mutationFn: () => reevaluateEmployeeDocumentRequirements(employee.id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey });
      toast({ variant: "success", title: t("common:success"), description: t("documents:requirements.reevaluated") });
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("documents:requirements.errors.generic")) }),
  });

  const uploadMutation = useMutation({
    mutationFn: async ({
      requirement,
      file,
      expiryDate,
    }: {
      requirement: EmployeeDocumentRequirement;
      file: File;
      expiryDate?: string | null;
    }) => {
      const documentId = await uploadDocumentFile(file, "documents", uploadMode === "me" ? "me" : "hr", expiryDate);
      return uploadMode === "me" ? linkMyDocumentToRequirement(requirement.id, documentId) : linkDocumentToRequirement(requirement.id, documentId);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey });
      toast({ variant: "success", title: t("common:success"), description: t("documents:requirements.uploaded") });
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("documents:requirements.errors.uploadFailed")) }),
    onSettled: () => {
      setUploadTarget(null);
      setPendingUpload(null);
    },
  });

  function pickFileFor(requirement: EmployeeDocumentRequirement) {
    setUploadTarget(requirement);
    fileInputRef.current?.click();
  }

  function fileSelected(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (file && uploadTarget) setPendingUpload({ requirement: uploadTarget, file });
  }

  if (isLoading) return <Skeleton h={200} />;

  const date = (value: string | null) => (value ? formatDateOnly(value, i18n.language) : "—");

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" wrap="nowrap" mb="md">
        <Title order={4}>{t("documents:requirements.title")}</Title>
        {canManage && (
          <Button
            type="button"
            variant="default"
            size="sm"
            leftSection={<RefreshCw size={16} />}
            loading={reevaluateMutation.isPending}
            onClick={() => reevaluateMutation.mutate()}
          >
            {t("documents:requirements.reevaluate")}
          </Button>
        )}
      </Group>

      {isError ? (
        <Text size="sm" c="red">
          {t("documents:requirements.errors.loadFailed")}
        </Text>
      ) : requirements.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("documents:requirements.empty")}
        </Text>
      ) : (
        <Stack gap="md">
          {requirements.map((requirement) => (
            <div key={requirement.id} className="flex flex-wrap items-start justify-between gap-2">
              <Stack gap={4} className="min-w-0">
                <Group gap="xs" wrap="wrap">
                  <Text size="sm" fw={500}>
                    {requirement.documentTypeName}
                  </Text>
                  <Badge variant="light">{t(`documents:enum.stage.${requirement.stage}`)}</Badge>
                  <Badge variant="filled" color={STATUS_COLOR[requirement.status]}>
                    {t(`documents:enum.status.${requirement.status}`)}
                  </Badge>
                </Group>
                <Text size="xs" c="dimmed">
                  {requirement.dueDate && `${t("documents:requirements.dueDate")}: ${date(requirement.dueDate)}`}
                  {requirement.status === "waived" &&
                    requirement.waivedReason &&
                    `${requirement.dueDate ? " · " : ""}${t("documents:requirements.waivedReason")}: ${requirement.waivedReason}`}
                </Text>
              </Stack>
              {requirement.status !== "uploaded" && (canManage || uploadMode === "me") && (
                <Group gap={4}>
                  {uploadMode && (uploadMode === "hr" || requirement.status === "missing") && (
                    <Button
                      type="button"
                      variant="subtle"
                      size="sm"
                      leftSection={<Upload size={14} />}
                      loading={uploadMutation.isPending && uploadMutation.variables?.requirement.id === requirement.id}
                      onClick={() => pickFileFor(requirement)}
                    >
                      {t("documents:requirements.upload")}
                    </Button>
                  )}
                  {canManage && requirement.status === "missing" && (
                    <Button type="button" variant="subtle" size="sm" onClick={() => setWaiving(requirement)}>
                      {t("documents:requirements.waive")}
                    </Button>
                  )}
                  {canManage && requirement.status === "waived" && (
                    <Button
                      type="button"
                      variant="subtle"
                      size="sm"
                      loading={clearWaiveMutation.isPending && clearWaiveMutation.variables?.id === requirement.id}
                      onClick={() => clearWaiveMutation.mutate(requirement)}
                    >
                      {t("documents:requirements.clearWaive")}
                    </Button>
                  )}
                </Group>
              )}
            </div>
          ))}
        </Stack>
      )}

      {uploadMode && <input ref={fileInputRef} type="file" className="hidden" onChange={fileSelected} />}
      {waiving && <WaiveDialog requirement={waiving} queryKey={queryKey} onClose={() => setWaiving(null)} />}
      {pendingUpload && (
        <UploadExpiryDialog
          pending={pendingUpload}
          isSubmitting={uploadMutation.isPending}
          onCancel={() => setPendingUpload(null)}
          onConfirm={(expiryDate) => uploadMutation.mutate({ ...pendingUpload, expiryDate })}
        />
      )}
    </Card>
  );
}

/** Optional validity date captured right before the confirm-upload call (S-6 sözleşmesi: license, residence permit, health report, etc.). */
function UploadExpiryDialog({
  pending,
  isSubmitting,
  onCancel,
  onConfirm,
}: {
  pending: { requirement: EmployeeDocumentRequirement; file: File };
  isSubmitting: boolean;
  onCancel: () => void;
  onConfirm: (expiryDate: string | null) => void;
}) {
  const { t } = useTranslation(["documents", "common"]);
  const [expiryDate, setExpiryDate] = useState("");

  return (
    <Modal opened onClose={onCancel} title={t("documents:requirements.uploadDialogTitle", { name: pending.requirement.documentTypeName })} size="28rem">
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          {pending.file.name}
        </Text>
        <Input.Wrapper
          id="upload-expiry-date"
          label={t("documents:requirements.expiryDate")}
          description={t("documents:requirements.expiryDateHint")}
          inputWrapperOrder={["label", "input", "description"]}
        >
          <DatePicker id="upload-expiry-date" value={expiryDate} onChange={setExpiryDate} clearable disabled={isSubmitting} />
        </Input.Wrapper>
        <Group justify="flex-end" gap="sm">
          <Button type="button" variant="default" onClick={onCancel} disabled={isSubmitting}>
            {t("common:cancel")}
          </Button>
          <Button type="button" loading={isSubmitting} onClick={() => onConfirm(expiryDate || null)}>
            {t("documents:requirements.upload")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function WaiveDialog({
  requirement,
  queryKey,
  onClose,
}: {
  requirement: EmployeeDocumentRequirement;
  queryKey: readonly unknown[];
  onClose: () => void;
}) {
  const { t } = useTranslation(["documents", "common"]);
  const queryClient = useQueryClient();
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () => waiveDocumentRequirement(requirement.id, reason.trim()),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey });
      toast({ variant: "success", title: t("common:success"), description: t("documents:requirements.saved") });
      onClose();
    },
    onError: (err) => {
      const problem = getApiProblem(err);
      setError(
        problem?.code === "employee_document_requirement.waive_reason_required"
          ? t("documents:requirements.errors.reasonRequired")
          : getApiErrorMessage(err, t("documents:requirements.errors.generic")),
      );
    },
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!reason.trim()) return setError(t("documents:requirements.errors.reasonRequired"));
    setError(null);
    mutation.mutate();
  }

  return (
    <Modal opened onClose={onClose} title={t("documents:requirements.waiveTitle", { name: requirement.documentTypeName })} size="32rem">
      <form onSubmit={submit}>
        <Stack gap="md">
          <Textarea
            id="waive-reason"
            label={t("documents:requirements.waivedReason")}
            withAsterisk
            autosize
            minRows={3}
            maxLength={500}
            value={reason}
            onChange={(event) => setReason(event.currentTarget.value)}
            disabled={mutation.isPending}
          />
          {error && (
            <Text size="sm" c="red">
              {error}
            </Text>
          )}
          <Group justify="flex-end" gap="sm">
            <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" loading={mutation.isPending}>
              {t("common:save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
