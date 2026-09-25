import { useMemo, useRef, useState, type ChangeEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Download, FolderOpen, Trash2, Upload } from "lucide-react";
import { Badge, Button, Card, Checkbox, Group, Modal, MultiSelect, NativeSelect, Progress, Radio, Skeleton, Stack, Switch, Text, Title } from "@mantine/core";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import ConfirmDialog from "@/components/confirm-dialog";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";
import { formatDateTime } from "@/lib/dates";
import SharedFileTypesDialog, { SHARED_FILE_TYPES_QUERY_KEY } from "@/components/shared-file-types-dialog";
import { saveDownloadedFile } from "@/services/data-transfer.service";
import { listSharedFileTypes, type SharedFileType } from "@/services/shared-file-types.service";
import { listRoles, visibleRoles } from "@/services/users.service";
import {
  classifySharedFile,
  deleteSharedFile,
  downloadSharedFilesZip,
  getSharedFile,
  getSharedFileQuota,
  listSharedFiles,
  uploadSharedFile,
  type SharedFile,
  type SharedFileVisibility,
} from "@/services/shared-files.service";

const FILES_QUERY_KEY = ["documents", "shared-files"] as const;
const QUOTA_QUERY_KEY = ["documents", "shared-files-quota"] as const;

/** Client-side upload guard (defense in depth - the server re-validates size/type), mirrors documents-section.tsx. */
const SHARED_FILE_ACCEPT =
  ".pdf,.png,.jpg,.jpeg,.doc,.docx,.xls,.xlsx,.ppt,.pptx,image/png,image/jpeg,application/pdf,application/msword,application/vnd.openxmlformats-officedocument.wordprocessingml.document,application/vnd.ms-excel,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,application/vnd.ms-powerpoint,application/vnd.openxmlformats-officedocument.presentationml.presentation";

function formatBytes(bytes: number): string {
  if (bytes <= 0) return "0 MB";
  const mb = bytes / (1024 * 1024);
  return `${mb.toFixed(2)} MB`;
}

function uploadErrorMessage(error: unknown, t: (key: string) => string): string {
  const problem = getApiProblem(error);
  if (problem?.code === "shared_company_file.quota_exceeded") return t("documents:sharedFiles.errors.quotaExceeded");
  if (problem?.code === "shared_company_file.roles_required_for_restricted_visibility") return t("documents:sharedFiles.errors.rolesRequired");
  return getApiErrorMessage(error, t("documents:sharedFiles.errors.uploadFailed"));
}

export default function SharedFilesPage() {
  const canView = usePermission("documents.company.view");
  if (!canView) return <NoAccess />;
  return <SharedFilesScreen />;
}

function SharedFilesScreen() {
  const { t, i18n } = useTranslation(["documents", "common"]);
  const canManage = usePermission("documents.company.manage");
  const queryClient = useQueryClient();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [pendingFile, setPendingFile] = useState<File | null>(null);
  const canManageTypes = usePermission("documents.sharedfile_types.manage");
  const [deleteTarget, setDeleteTarget] = useState<SharedFile | null>(null);
  const [typeFilter, setTypeFilter] = useState("");
  const [mobileOnly, setMobileOnly] = useState(false);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [typesDialogOpen, setTypesDialogOpen] = useState(false);

  const typeId = typeFilter || undefined;
  const typesQuery = useQuery({ queryKey: SHARED_FILE_TYPES_QUERY_KEY, queryFn: () => listSharedFileTypes() });
  const types = useMemo(() => typesQuery.data ?? [], [typesQuery.data]);
  const activeTypes = useMemo(() => types.filter((type) => type.isActive), [types]);
  const filesQuery = useQuery({
    queryKey: [...FILES_QUERY_KEY, { typeId, mobileOnly }],
    queryFn: () => listSharedFiles({ pageSize: 100, typeId, mobileOnly: mobileOnly || undefined }),
  });
  const quotaQuery = useQuery({ queryKey: QUOTA_QUERY_KEY, queryFn: getSharedFileQuota });
  const files = useMemo(() => filesQuery.data?.items ?? [], [filesQuery.data]);

  const deleteMutation = useMutation({
    mutationFn: (file: SharedFile) => deleteSharedFile(file.id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: FILES_QUERY_KEY });
      void queryClient.invalidateQueries({ queryKey: QUOTA_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("documents:sharedFiles.deleted") });
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("documents:sharedFiles.errors.deleteFailed")) }),
    onSettled: () => setDeleteTarget(null),
  });

  const classifyMutation = useMutation({
    mutationFn: (input: { file: SharedFile; typeId?: string; visibleOnMobile: boolean }) =>
      classifySharedFile(input.file.id, { typeId: input.typeId, visibleOnMobile: input.visibleOnMobile }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: FILES_QUERY_KEY }),
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("documents:sharedFiles.classifyFailed")) }),
  });

  const zipMutation = useMutation({
    mutationFn: () => downloadSharedFilesZip(selectedIds.length > 0 ? { fileIds: selectedIds } : { typeId, mobileOnly: mobileOnly || undefined }),
    onSuccess: ({ blob, fileName }) => saveDownloadedFile({ blob, fileName: fileName ?? t("documents:sharedFiles.zipFileName") }),
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("documents:sharedFiles.zipFailed")) }),
  });

  function toggleSelected(id: string, checked: boolean) {
    setSelectedIds((current) => (checked ? [...current, id] : current.filter((x) => x !== id)));
  }

  function fileSelected(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (file) setPendingFile(file);
  }

  const columns: ColumnDef<SharedFile>[] = [
    {
      id: "select",
      header: "",
      meta: { label: t("documents:sharedFiles.selectAll"), headerClassName: "w-10" },
      enableSorting: false,
      cell: ({ row }) => (
        <Checkbox
          aria-label={t("documents:sharedFiles.selectRow", { name: row.original.fileName })}
          checked={selectedIds.includes(row.original.id)}
          onChange={(e) => toggleSelected(row.original.id, e.currentTarget.checked)}
        />
      ),
    },
    {
      id: "fileName",
      accessorKey: "fileName",
      header: t("documents:sharedFiles.columns.name"),
      meta: { label: t("documents:sharedFiles.columns.name") },
    },
    {
      id: "contentType",
      accessorKey: "contentType",
      header: t("documents:sharedFiles.columns.type"),
      meta: { label: t("documents:sharedFiles.columns.type"), headerClassName: "w-48" },
      enableSorting: false,
    },
    {
      id: "size",
      header: t("documents:sharedFiles.columns.size"),
      meta: { label: t("documents:sharedFiles.columns.size"), headerClassName: "w-28" },
      cell: ({ row }) => formatBytes(row.original.size),
    },
    {
      id: "uploadedAt",
      accessorKey: "uploadedAt",
      header: t("documents:sharedFiles.columns.uploadedAt"),
      meta: { label: t("documents:sharedFiles.columns.uploadedAt"), headerClassName: "w-48" },
      cell: ({ row }) => formatDateTime(row.original.uploadedAt, i18n.language),
    },
    {
      id: "visibility",
      header: t("documents:sharedFiles.columns.visibility"),
      meta: { label: t("documents:sharedFiles.columns.visibility"), headerClassName: "w-40" },
      enableSorting: false,
      cell: ({ row }) =>
        row.original.visibility === "everyone" ? (
          <Badge variant="light" color="teal" size="sm">
            {t("documents:sharedFiles.everyone")}
          </Badge>
        ) : (
          <Badge variant="light" color="orange" size="sm" title={row.original.allowedRoles.join(", ")}>
            {t("documents:sharedFiles.restricted")}
          </Badge>
        ),
    },
    {
      id: "fileType",
      header: t("documents:sharedFiles.typeColumn"),
      meta: { label: t("documents:sharedFiles.typeColumn"), headerClassName: "w-44" },
      enableSorting: false,
      cell: ({ row }) => {
        const file = row.original;
        const name = types.find((type) => type.id === file.typeId)?.name;
        if (!canManage) return name ?? "—";
        return (
          <NativeSelect
            size="xs"
            aria-label={t("documents:sharedFiles.typeField")}
            value={file.typeId ?? ""}
            data={typeOptions(activeTypes, file.typeId, name, t("documents:sharedFiles.noType"))}
            onChange={(e) => classifyMutation.mutate({ file, typeId: e.currentTarget.value || undefined, visibleOnMobile: file.visibleOnMobile })}
          />
        );
      },
    },
    {
      id: "visibleOnMobile",
      header: t("documents:sharedFiles.mobileColumn"),
      meta: { label: t("documents:sharedFiles.mobileColumn"), headerClassName: "w-28" },
      enableSorting: false,
      cell: ({ row }) => (
        <Switch
          size="sm"
          aria-label={t("documents:sharedFiles.mobileToggle", { name: row.original.fileName })}
          checked={row.original.visibleOnMobile}
          disabled={!canManage || classifyMutation.isPending}
          onChange={(e) => classifyMutation.mutate({ file: row.original, typeId: row.original.typeId, visibleOnMobile: e.currentTarget.checked })}
        />
      ),
    },
    {
      id: "actions",
      header: "",
      meta: { label: t("common:actions"), headerClassName: "w-28" },
      enableSorting: false,
      cell: ({ row }) => (
        <Group gap="xs" justify="flex-end" wrap="nowrap">
          <SharedFileDownloadButton fileId={row.original.id} />
          {canManage && (
            <Button type="button" size="xs" variant="subtle" color="red" onClick={() => setDeleteTarget(row.original)}>
              <Trash2 size={14} />
            </Button>
          )}
        </Group>
      ),
    },
  ];

  const table = useDataTable({ columns, data: files, getRowId: (r) => r.id });
  const quota = quotaQuery.data;

  return (
    <div className="max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <FolderOpen size={24} /> {t("documents:sharedFiles.title")}
          </Title>
          <Text c="dimmed">{t("documents:sharedFiles.subtitle")}</Text>
        </div>
        <Group gap="sm">
          {canManageTypes && (
            <Button type="button" variant="default" onClick={() => setTypesDialogOpen(true)}>
              {t("documents:sharedFiles.manageTypes")}
            </Button>
          )}
          {canManage && (
            <Button type="button" leftSection={<Upload size={16} />} onClick={() => fileInputRef.current?.click()}>
              {t("documents:sharedFiles.upload")}
            </Button>
          )}
        </Group>
      </Group>

      <Card withBorder padding="lg">
        {quotaQuery.isLoading ? (
          <Skeleton h={32} />
        ) : quota ? (
          <Stack gap={4}>
            <Group justify="space-between">
              <Text size="sm" fw={500}>
                {t("documents:sharedFiles.quotaLabel", {
                  percent: Math.round(quota.usedPercent),
                  used: formatBytes(quota.usedBytes),
                  quota: formatBytes(quota.quotaBytes),
                })}
              </Text>
            </Group>
            <Progress value={quota.usedPercent} color={quota.isExceeded ? "red" : quota.usedPercent > 80 ? "orange" : "teal"} size="lg" radius="xl" />
          </Stack>
        ) : null}
      </Card>

      <Card withBorder padding="lg">
        <Group justify="space-between" align="flex-end" mb="md" gap="sm">
          <Group align="flex-end" gap="md">
            <NativeSelect
              aria-label={t("documents:sharedFiles.typeFilter")}
              value={typeFilter}
              data={[{ value: "", label: t("documents:sharedFiles.allTypes") }, ...types.map((type) => ({ value: type.id, label: type.name }))]}
              onChange={(e) => {
                setTypeFilter(e.currentTarget.value);
                setSelectedIds([]);
              }}
            />
            <Switch
              label={t("documents:sharedFiles.mobileOnlyFilter")}
              checked={mobileOnly}
              onChange={(e) => {
                setMobileOnly(e.currentTarget.checked);
                setSelectedIds([]);
              }}
            />
          </Group>
          <Button type="button" variant="default" leftSection={<Download size={14} />} loading={zipMutation.isPending} disabled={files.length === 0} onClick={() => zipMutation.mutate()}>
            {selectedIds.length > 0 ? t("documents:sharedFiles.zipDownloadCount", { count: selectedIds.length }) : t("documents:sharedFiles.zipDownload")}
          </Button>
        </Group>
        {filesQuery.isError ? (
          <Text size="sm" c="red" ta="center" p="lg">
            {t("documents:sharedFiles.errors.loadFailed")}
          </Text>
        ) : (
          <DataTable table={table} isLoading={filesQuery.isLoading} emptyMessage={t("documents:sharedFiles.empty")} />
        )}
      </Card>

      {canManage && <input ref={fileInputRef} type="file" accept={SHARED_FILE_ACCEPT} className="hidden" onChange={fileSelected} />}
      {typesDialogOpen && <SharedFileTypesDialog onClose={() => setTypesDialogOpen(false)} />}
      {pendingFile && <UploadDialog file={pendingFile} activeTypes={activeTypes} onClose={() => setPendingFile(null)} />}
      {deleteTarget && (
        <ConfirmDialog
          open
          onOpenChange={(open) => !open && setDeleteTarget(null)}
          title={t("documents:sharedFiles.deleteTitle")}
          description={t("documents:sharedFiles.deleteConfirm", { name: deleteTarget.fileName })}
          isLoading={deleteMutation.isPending}
          onConfirm={() => deleteMutation.mutate(deleteTarget)}
        />
      )}
    </div>
  );
}

/** Presigned download URL is short-lived (15 min) - fetched on demand, not cached in the list query. */
function SharedFileDownloadButton({ fileId }: { fileId: string }) {
  const { t } = useTranslation(["documents"]);
  const mutation = useMutation({
    mutationFn: () => getSharedFile(fileId),
    onSuccess: (detail) => window.open(detail.downloadUrl, "_blank", "noopener,noreferrer"),
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("documents:sharedFiles.errors.downloadFailed")) }),
  });

  return (
    <Button type="button" size="xs" variant="subtle" loading={mutation.isPending} onClick={() => mutation.mutate()}>
      <Download size={14} />
    </Button>
  );
}

/** Active catalog types, plus the file's current type when it has since been deactivated (so the select still shows it). */
function typeOptions(activeTypes: SharedFileType[], currentId: string | undefined, currentName: string | undefined, emptyLabel: string) {
  const options = [{ value: "", label: emptyLabel }, ...activeTypes.map((type) => ({ value: type.id, label: type.name }))];
  if (currentId && !activeTypes.some((type) => type.id === currentId)) options.push({ value: currentId, label: currentName ?? currentId });
  return options;
}

function UploadDialog({ file, activeTypes, onClose }: { file: File; activeTypes: SharedFileType[]; onClose: () => void }) {
  const { t } = useTranslation(["documents", "common"]);
  const queryClient = useQueryClient();
  const [visibility, setVisibility] = useState<SharedFileVisibility>("everyone");
  const [allowedRoles, setAllowedRoles] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [typeId, setTypeId] = useState("");
  const [visibleOnMobile, setVisibleOnMobile] = useState(false);

  const rolesQuery = useQuery({ queryKey: ["identity", "roles"], queryFn: listRoles, enabled: visibility === "restrictedToRoles" });
  const roleOptions = useMemo(
    () => visibleRoles(rolesQuery.data ?? []).map((r) => ({ value: r.code, label: r.name ?? r.code })),
    [rolesQuery.data]
  );

  const mutation = useMutation({
    mutationFn: () => uploadSharedFile(file, {
        visibility,
        allowedRoles: visibility === "restrictedToRoles" ? allowedRoles : undefined,
        typeId: typeId || undefined,
        visibleOnMobile,
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: FILES_QUERY_KEY });
      void queryClient.invalidateQueries({ queryKey: QUOTA_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("documents:sharedFiles.uploaded") });
      onClose();
    },
    onError: (err) => setError(uploadErrorMessage(err, t)),
  });

  function submit() {
    if (visibility === "restrictedToRoles" && allowedRoles.length === 0) {
      setError(t("documents:sharedFiles.errors.rolesRequired"));
      return;
    }
    setError(null);
    mutation.mutate();
  }

  return (
    <Modal opened onClose={onClose} title={t("documents:sharedFiles.uploadDialogTitle")} size="28rem">
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          {file.name}
        </Text>
        <Radio.Group
          label={t("documents:sharedFiles.visibilityLabel")}
          value={visibility}
          onChange={(value) => setVisibility(value as SharedFileVisibility)}
        >
          <Stack gap="xs" mt="xs">
            <Radio value="everyone" label={t("documents:sharedFiles.everyoneHint")} />
            <Radio value="restrictedToRoles" label={t("documents:sharedFiles.restrictedHint")} />
          </Stack>
        </Radio.Group>
        {visibility === "restrictedToRoles" && (
          <MultiSelect
            label={t("documents:sharedFiles.allowedRoles")}
            data={roleOptions}
            value={allowedRoles}
            onChange={setAllowedRoles}
            disabled={rolesQuery.isLoading}
            searchable
          />
        )}
        <NativeSelect
          label={t("documents:sharedFiles.typeField")}
          value={typeId}
          data={typeOptions(activeTypes, undefined, undefined, t("documents:sharedFiles.noType"))}
          onChange={(e) => {
            const id = e.currentTarget.value;
            setTypeId(id);
            const chosen = activeTypes.find((type) => type.id === id);
            if (chosen) setVisibleOnMobile(chosen.visibleOnMobile);
          }}
        />
        <Switch label={t("documents:sharedFiles.mobileField")} checked={visibleOnMobile} onChange={(e) => setVisibleOnMobile(e.currentTarget.checked)} />
        {error && (
          <Text size="sm" c="red">
            {error}
          </Text>
        )}
        <Group justify="flex-end" gap="sm">
          <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button type="button" loading={mutation.isPending} onClick={submit}>
            {t("documents:sharedFiles.upload")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
