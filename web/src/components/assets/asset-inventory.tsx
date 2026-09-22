import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { PackagePlus } from "lucide-react";
import { Badge, Button, Card, Group, Select, Skeleton, Stack, Table, Text } from "@mantine/core";
import ConfirmDialog from "@/components/confirm-dialog";
import EmployeeNameLink from "@/components/employees/employee-name-link";
import PaginationFooter from "@/components/pagination-footer";
import { usePermission } from "@/components/permission-guard";
import {
  AssetFormDialog,
  AssignAssetFromInventoryDialog,
  ReturnAssetFromInventoryDialog,
} from "@/components/assets/asset-dialogs";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import {
  ASSET_STATUSES,
  listAssetCategories,
  listAssets,
  retireAsset,
  type Asset,
  type AssetStatus,
} from "@/services/assets.service";

const PAGE_SIZE = 20;

export const ASSET_STATUS_COLOR: Record<AssetStatus, string> = {
  unassigned: "gray",
  assigned: "green",
  retired: "dark",
};

type Dialog =
  | { kind: "form"; asset: Asset | null }
  | { kind: "assign"; asset: Asset }
  | { kind: "return"; asset: Asset }
  | { kind: "retire"; asset: Asset }
  | null;

/** Şirket zimmet envanteri (assets.assets.view): durum/kategori filtresi; yönetim işlemleri assets.assets.manage ile. */
export default function AssetInventory() {
  const { t, i18n } = useTranslation(["assets", "common"]);
  const canManage = usePermission("assets.assets.manage");
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<AssetStatus | null>(null);
  const [categoryId, setCategoryId] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [dialog, setDialog] = useState<Dialog>(null);

  const categoriesQuery = useQuery({
    queryKey: ["assets", "categories"],
    queryFn: listAssetCategories,
  });
  const assetsQuery = useQuery({
    queryKey: ["assets", "inventory", status, categoryId, page],
    queryFn: () =>
      listAssets({
        page,
        pageSize: PAGE_SIZE,
        status: status ?? undefined,
        categoryId: categoryId ?? undefined,
      }),
  });

  const categories = categoriesQuery.data ?? [];
  const categoryName = (id: string) => categories.find((c) => c.id === id)?.name ?? "—";
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ["assets"] });

  const retireMutation = useMutation({
    mutationFn: (id: string) => retireAsset(id),
    onSuccess: () => {
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("assets:inventory.retired"),
      });
      setDialog(null);
      refresh();
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("assets:section.errors.generic")),
      }),
  });

  const result = assetsQuery.data;
  const rows = result?.items ?? [];
  const date = (value: string | null) => (value ? formatDateOnly(value, i18n.language) : "—");

  return (
    <Stack gap="md">
      <Group justify="space-between" wrap="wrap" gap="sm">
        <Group gap="sm" wrap="wrap">
          <Select
            aria-label={t("assets:inventory.statusFilter")}
            placeholder={t("assets:inventory.allStatuses")}
            data={ASSET_STATUSES.map((value) => ({
              value,
              label: t(`assets:enum.status.${value}`),
            }))}
            value={status}
            onChange={(value) => {
              setStatus(value as AssetStatus | null);
              setPage(1);
            }}
            clearable
            w={180}
          />
          <Select
            aria-label={t("assets:inventory.categoryFilter")}
            placeholder={t("assets:inventory.allCategories")}
            data={categories.map((c) => ({ value: c.id, label: c.name }))}
            value={categoryId}
            onChange={(value) => {
              setCategoryId(value);
              setPage(1);
            }}
            clearable
            searchable
            w={220}
          />
        </Group>
        {canManage && (
          <Button
            leftSection={<PackagePlus size={16} />}
            onClick={() => setDialog({ kind: "form", asset: null })}
          >
            {t("assets:inventory.create")}
          </Button>
        )}
      </Group>

      {assetsQuery.isLoading && <Skeleton height={200} />}
      {assetsQuery.isError && <Text c="red">{t("assets:section.errors.loadFailed")}</Text>}
      {assetsQuery.isSuccess && rows.length === 0 && (
        <Card withBorder>
          <Text c="dimmed">{t("assets:inventory.empty")}</Text>
        </Card>
      )}
      {rows.length > 0 && (
        <Table.ScrollContainer minWidth={820}>
          <Table striped highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("assets:section.name")}</Table.Th>
                <Table.Th>{t("assets:section.category")}</Table.Th>
                <Table.Th>{t("assets:section.serialNumber")}</Table.Th>
                <Table.Th>{t("assets:inventory.status")}</Table.Th>
                <Table.Th>{t("assets:inventory.employee")}</Table.Th>
                <Table.Th>{t("assets:section.issuedAt")}</Table.Th>
                {canManage && <Table.Th />}
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {rows.map((asset) => (
                <Table.Tr key={asset.id}>
                  <Table.Td>{asset.name}</Table.Td>
                  <Table.Td>{categoryName(asset.categoryId)}</Table.Td>
                  <Table.Td>{asset.serialNumber ?? "—"}</Table.Td>
                  <Table.Td>
                    <Badge color={ASSET_STATUS_COLOR[asset.status]} variant="light">
                      {t(`assets:enum.status.${asset.status}`)}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    {asset.assignedEmployeeId ? (
                      <EmployeeNameLink id={asset.assignedEmployeeId} tab="assets" />
                    ) : (
                      "—"
                    )}
                  </Table.Td>
                  <Table.Td>{asset.status === "assigned" ? date(asset.issuedAt) : "—"}</Table.Td>
                  {canManage && (
                    <Table.Td>
                      <RowActions asset={asset} onOpen={setDialog} />
                    </Table.Td>
                  )}
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      {result && result.totalPages > 1 && (
        <PaginationFooter result={result} onPageChange={setPage} />
      )}

      {dialog?.kind === "form" && (
        <AssetFormDialog
          asset={dialog.asset}
          categories={categories}
          onClose={() => setDialog(null)}
          onSaved={refresh}
        />
      )}
      {dialog?.kind === "assign" && (
        <AssignAssetFromInventoryDialog
          asset={dialog.asset}
          onClose={() => setDialog(null)}
          onDone={refresh}
        />
      )}
      {dialog?.kind === "return" && (
        <ReturnAssetFromInventoryDialog
          asset={dialog.asset}
          onClose={() => setDialog(null)}
          onDone={refresh}
        />
      )}
      <ConfirmDialog
        open={dialog?.kind === "retire"}
        onOpenChange={(open) => !open && setDialog(null)}
        title={t("assets:inventory.retireTitle")}
        description={t("assets:inventory.retireDescription", { name: dialog?.asset?.name ?? "" })}
        confirmLabel={t("assets:inventory.retire")}
        onConfirm={() => dialog?.kind === "retire" && retireMutation.mutate(dialog.asset.id)}
        isLoading={retireMutation.isPending}
      />
    </Stack>
  );
}

function RowActions({ asset, onOpen }: { asset: Asset; onOpen: (dialog: Dialog) => void }) {
  const { t } = useTranslation(["assets"]);
  if (asset.status === "retired") return null;
  return (
    <Group gap="xs" justify="flex-end" wrap="nowrap">
      {asset.status === "unassigned" && (
        <Button size="compact-sm" variant="light" onClick={() => onOpen({ kind: "assign", asset })}>
          {t("assets:section.assign")}
        </Button>
      )}
      {asset.status === "assigned" && (
        <Button size="compact-sm" variant="light" onClick={() => onOpen({ kind: "return", asset })}>
          {t("assets:section.return")}
        </Button>
      )}
      <Button size="compact-sm" variant="subtle" onClick={() => onOpen({ kind: "form", asset })}>
        {t("assets:inventory.edit")}
      </Button>
      {asset.status === "unassigned" && (
        <Button
          size="compact-sm"
          variant="subtle"
          color="red"
          onClick={() => onOpen({ kind: "retire", asset })}
        >
          {t("assets:inventory.retire")}
        </Button>
      )}
    </Group>
  );
}
