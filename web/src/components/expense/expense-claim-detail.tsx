import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Alert, Button, Card, Group, Skeleton, Stack, Text } from "@mantine/core";
import { Download } from "lucide-react";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import { formatMoney } from "@/lib/money";
import { getExpenseClaim, reviewExpenseClaim, getExpenseReceiptDownloadUrl, type ExpenseItem } from "@/services/expense.service";

export function ExpenseReceiptList({ claimId, item }: { claimId: string; item: ExpenseItem }) {
  const { t, i18n } = useTranslation(["expense", "common"]);
  const download = useMutation({
    mutationFn: (documentId: string) => getExpenseReceiptDownloadUrl(claimId, item.id, documentId),
    onSuccess: (result) => window.open(result.url, "_blank", "noopener,noreferrer"),
  });
  return <Stack gap="xs" style={{ minWidth: 0 }}>
    <Text size="sm" fw={500}>{t("expense:attachments.label")}</Text>
    {(item.receiptAttachments ?? []).length === 0 && <Text size="xs" c="dimmed">{t("expense:attachments.empty")}</Text>}
    {(item.receiptAttachments ?? []).map(file => <Group key={file.documentId} gap="xs" wrap="nowrap" align="flex-start">
      <div style={{ flex: 1, minWidth: 0 }}>
        <Text size="sm" style={{ overflowWrap: "anywhere" }}>{file.fileName}</Text>
        <Text size="xs" c="dimmed">{file.contentType} · {(file.size / 1024).toLocaleString(i18n.language, { maximumFractionDigits: 1 })} KB</Text>
      </div>
      <Button type="button" size="compact-sm" variant="subtle" aria-label={t("expense:attachments.open", { name: file.fileName })}
        style={{ minWidth: 40, minHeight: 40 }}
        disabled={download.isPending} loading={download.isPending && download.variables === file.documentId}
        onClick={() => download.mutate(file.documentId)}><Download size={16} /></Button>
    </Group>)}
    {download.isError && <Alert color="red" role="alert">{getApiErrorMessage(download.error, t("expense:attachments.downloadError"))}</Alert>}
  </Stack>;
}

/** Same authorized expense detail in requester, approval and HR views. */
export function ExpenseClaimDetail({ claimId, review = false }: { claimId: string; review?: boolean }) {
  const { t, i18n } = useTranslation(["expense", "common"]);
  const claim = useQuery({ queryKey: ["expense", "claim", claimId, review ? "review" : "owner"], queryFn: () => review ? reviewExpenseClaim(claimId) : getExpenseClaim(claimId) });
  if (claim.isLoading) return <Skeleton h={100} />;
  if (claim.isError) return <Alert color="red" role="alert">{getApiErrorMessage(claim.error, t("expense:genericError"))}</Alert>;
  return <Stack gap="sm" style={{ minWidth: 0 }}>
    {claim.data?.description && <Text size="sm" style={{ overflowWrap: "anywhere" }}>{claim.data.description}</Text>}
    {(claim.data?.items ?? []).map(item => <Card key={item.id} withBorder padding="sm">
      <Stack gap="xs">
        <Group justify="space-between">
          <Text size="sm" fw={500} style={{ overflowWrap: "anywhere" }}>{item.description || t("expense:attachments.item")}</Text>
          <Text size="sm" fw={600}>{formatMoney(item.amount, item.currency, i18n.language)}</Text>
        </Group>
        <Text size="xs" c="dimmed">{formatDateOnly(item.receiptDate, i18n.language)}</Text>
        <ExpenseReceiptList claimId={claimId} item={item} />
      </Stack>
    </Card>)}
    <Text fw={600}>{t("expense:claim.total")}: {formatMoney(claim.data?.totalAmount ?? 0, claim.data?.items[0]?.currency ?? "TRY", i18n.language)}</Text>
  </Stack>;
}
