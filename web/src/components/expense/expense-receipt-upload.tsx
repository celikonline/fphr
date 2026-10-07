import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { ActionIcon, Alert, FileInput, Group, Stack, Text } from "@mantine/core";
import { Paperclip, Trash2 } from "lucide-react";
import { uploadDocumentFile } from "@/services/documents.service";

const TYPES: Record<string, string[]> = {
  pdf: ["application/pdf"], jpg: ["image/jpeg", "image/pjpeg"], jpeg: ["image/jpeg", "image/pjpeg"],
  png: ["image/png"], gif: ["image/gif"], webp: ["image/webp"], heic: ["image/heic", "image/heif"],
};
type StagedReceipt = { key: number; file: File; documentId?: string; status: "selected" | "uploading" | "uploaded" | "failed" };

export function useExpenseReceiptUploads() {
  const { t } = useTranslation("expense");
  const [files, setFiles] = useState<StagedReceipt[]>([]);
  const [selectionError, setSelectionError] = useState<string | null>(null);
  const nextKey = useRef(0);
  const update = (key: number, patch: Partial<StagedReceipt>) => setFiles(current => current.map(file => file.key === key ? { ...file, ...patch } : file));
  function select(selected: File[]) {
    setSelectionError(null);
    if (selected.some(file => file.size === 0 || file.size > 100 * 1024 * 1024)) {
      setSelectionError(t("attachments.sizeError"));
      return;
    }
    if (selected.some(file => {
      const types = TYPES[file.name.split(".").pop()?.toLowerCase() ?? ""];
      return !types || (file.type !== "" && !types.includes(file.type));
    })) {
      setSelectionError(t("attachments.typeError"));
      return;
    }
    setFiles(current => [...current, ...selected.map(file => ({ key: nextKey.current++, file: file.type ? file : new File([file], file.name, { type: TYPES[file.name.split(".").pop()!.toLowerCase()][0] }), status: "selected" as const }))]);
  }
  async function resolveFileIds(): Promise<string[]> {
    const ids: string[] = [];
    let failed = false;
    // Stable snapshot: selection/removal stays disabled until the item request settles.
    for (const receipt of files) {
      if (receipt.documentId) { ids.push(receipt.documentId); continue; }
      update(receipt.key, { status: "uploading" });
      try {
        const documentId = await uploadDocumentFile(receipt.file, "documents", "me");
        update(receipt.key, { documentId, status: "uploaded" });
        ids.push(documentId);
      } catch {
        update(receipt.key, { status: "failed" });
        failed = true;
      }
    }
    if (failed) throw new Error(t("attachments.uploadError"));
    return ids;
  }
  return { files, selectionError, select, resolveFileIds, remove: (key: number) => setFiles(current => current.filter(file => file.key !== key)), reset: () => { setFiles([]); setSelectionError(null); } };
}

export function ExpenseReceiptUpload({ receipts, disabled }: { receipts: ReturnType<typeof useExpenseReceiptUploads>; disabled: boolean }) {
  const { t, i18n } = useTranslation("expense");
  return <Stack gap="xs" style={{ minWidth: 0 }}>
    <FileInput multiple value={[]} onChange={receipts.select} disabled={disabled}
      label={t("attachments.label")} description={t("attachments.hint")}
      placeholder={t("attachments.choose")} accept={Object.keys(TYPES).map(ext => `.${ext}`).join(",")}
      leftSection={<Paperclip size={16} />} />
    {receipts.selectionError && <Alert color="red" role="alert">{receipts.selectionError}</Alert>}
    {receipts.files.map(receipt => <Group key={receipt.key} gap="xs" wrap="nowrap" align="flex-start">
      <div style={{ flex: 1, minWidth: 0 }}>
        <Text size="sm" style={{ overflowWrap: "anywhere" }}>{receipt.file.name}</Text>
        <Text size="xs" c={receipt.status === "failed" ? "red" : "dimmed"} role={receipt.status === "failed" ? "alert" : "status"}>
          {(receipt.file.size / 1024).toLocaleString(i18n.language, { maximumFractionDigits: 1 })} KB · {t(`attachments.status.${receipt.status}`)}
        </Text>
      </div>
      <ActionIcon type="button" size={40} variant="subtle" color="red" disabled={disabled} aria-label={t("attachments.remove", { name: receipt.file.name })} onClick={() => receipts.remove(receipt.key)}><Trash2 size={16} /></ActionIcon>
    </Group>)}
  </Stack>;
}
