import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FileSpreadsheet, PackagePlus, Search } from "lucide-react";
import {
  Badge,
  Button,
  Card,
  Group,
  Select,
  Skeleton,
  Stack,
  Table,
  Text,
  TextInput,
} from "@mantine/core";
import { useDebouncedValue } from "@mantine/hooks";
import ConfirmDialog from "@/components/confirm-dialog";
import EmployeeNameLink from "@/components/employees/employee-name-link";
import ReplacementDueBadge from "@/components/assets/replacement-due-badge";
import { ConditionBadge, UnacknowledgedBadge, WarrantyBadge } from "@/components/assets/asset-badges";
import { formatCost } from "@/components/assets/asset-format";
import AssetSummaryCards from "@/components/assets/asset-summary-cards";
import AssetImportDialog from "@/components/assets/asset-import-dialog";
import { AssetHistoryDialog } from "@/components/assets/assignment-history";
import PaginationFooter from "@/components/pagination-footer";
import { usePermission } from "@/components/permission-guard";
import {
  AssetFormDialog,
  AssignAssetFromInventoryDialog,
  LostAssetDialog,
  ReturnAssetFromInventoryDialog,
} from "@/components/assets/asset-dialogs";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import { DataTransferPermissions } from "@/services/data-transfer.service";
import {
  ASSET_STATUSES,
  listAssetCategories,
  listAssets,
  retireAsset,
  type Asset,
  type AssetStatus,
} from "@/services/assets.service";

const PAGE_SIZE = 20;
const SEARCH_DEBOUNCE_MS = 300;

export const ASSET_STATUS_COLOR: Record<AssetStatus, string> = {
  unassigned: "gray",
  assigned: "green",
  pendingAssignment: "yellow",
  retired: "dark",
  lost: "red",
};

type Dialog =
  | { kind: "form"; asset: Asset | null }
  | { kind: "assign"; asset: Asset }
  | { kind: "return"; asset: Asset }
  | { kind: "retire"; asset: Asset }
  | { kind: "lost"; asset: Asset }
  | { kind: "history"; asset: Asset }
  | null;

/** Şirket zimmet envanteri (assets.assets.view): özet, arama, durum/kategori filtresi; yönetim işlemleri assets.assets.manage ile. */
export default function AssetInventory() {
  const { t, i18n } = useTranslation(["assets", "common"]);
  const canManage = usePermission("assets.assets.manage");
  const canImportAssets = usePermission("assets.assets.import");
  const canViewImports = usePermission(DataTransferPermissions.View);
  const canRunImports = usePermission(DataTransferPermissions.Run);
  const canImport = canImportAssets && canViewImports && canRunImports;
  const [importOpen, setImportOpen] = useState(false);
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<AssetStatus | null>(null);
  const [categoryId, setCategoryId] = useState<string | null>(null);
  const [searchText, setSearchText] = useState("");
  const [debouncedSearch] = useDebouncedValue(searchText.trim(), SEARCH_DEBOUNCE_MS);
  const [page, setPage] = useState(1);
  const [dialog, setDialog] = useState<Dialog>(null);

  const categoriesQuery = useQuery({
    queryKey: ["assets", "categories"],
    queryFn: listAssetCategories,
  });
  const assetsQuery = useQuery({
    queryKey: ["assets", "inventory", status, categoryId, debouncedSearch, page],
    queryFn: () =>
      listAssets({
        page,
        pageSize: PAGE_SIZE,
        status: status ?? undefined,
        categoryId: categoryId ?? undefined,
        search: debouncedSearch || undefined,
      }),
  });

  const categories = categoriesQuery.data ?? [];
  const categoryName = (id: string) => categories.find((c) => c.id === id)?.name ?? "—";
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ["assets"] });
  const applyStatus = (value: AssetStatus | null) => {
    setStatus(value);
    setPage(1);
  };

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
        description: getApiErrorMessage(error, t("assets:section.errors.retry")),
      }),
  });

  const result = assetsQuery.data;
  const rows = result?.items ?? [];
  const date = (value?: string | null) => (value ? formatDateOnly(value, i18n.language) : "—");
  const isFiltered = status !== null || categoryId !== null || debouncedSearch !== "";

  return (
    <Stack gap="md">
      <AssetSummaryCards status={status} onStatusChange={applyStatus} />

      <Group justify="space-between" wrap="wrap" gap="sm">
        <Group gap="sm" wrap="wrap">
          <TextInput
            aria-label={t("assets:inventory.search")}
            placeholder={t("assets:inventory.searchPlaceholder")}
            leftSection={<Search size={16} />}
            value={searchText}
            onChange={(e) => {
              setSearchText(e.currentTarget.value);
              setPage(1);
            }}
            w={280}
          />
          <Select
            aria-label={t("assets:inventory.statusFilter")}
            placeholder={t("assets:inventory.allStatuses")}
            data={ASSET_STATUSES.map((value) => ({
              value,
              label: t(`assets:enum.status.${value}`),
            }))}
            value={status}
            onChange={(value) => applyStatus(value as AssetStatus | null)}
            clearable
            w={200}
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
        <Group gap="sm" wrap="wrap">
          {canImport && (
            <Button variant="default" leftSection={<FileSpreadsheet size={16} />} onClick={() => setImportOpen(true)}>
              {t("assets:import.action")}
            </Button>
          )}
          {canManage && (
            <Button
              leftSection={<PackagePlus size={16} />}
              onClick={() => setDialog({ kind: "form", asset: null })}
            >
              {t("assets:inventory.create")}
            </Button>
          )}
        </Group>
      </Group>

      {assetsQuery.isLoading && <Skeleton height={200} />}
      {assetsQuery.isError && <Text c="red">{t("assets:section.errors.loadFailed")}</Text>}
      {assetsQuery.isSuccess && rows.length === 0 && (
        <Card withBorder>
          <Text c="dimmed">
            {isFiltered ? t("assets:inventory.empty") : t("assets:inventory.emptyAll")}
          </Text>
        </Card>
      )}
      {rows.length > 0 && (
        <Table.ScrollContainer minWidth={1180}>
          <Table striped highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("assets:section.name")}</Table.Th>
                <Table.Th>{t("assets:section.assetTag")}</Table.Th>
                <Table.Th>{t("assets:section.serialNumber")}</Table.Th>
                <Table.Th>{t("assets:section.category")}</Table.Th>
                <Table.Th>{t("assets:inventory.status")}</Table.Th>
                <Table.Th>{t("assets:inventory.employee")}</Table.Th>
                <Table.Th>{t("assets:section.issuedAt")}</Table.Th>
                <Table.Th>{t("assets:section.purchase")}</Table.Th>
                <Table.Th>{t("assets:section.warrantyEndsOn")}</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {rows.map((asset) => (
                <Table.Tr key={asset.id}>
                  <Table.Td>
                    {asset.name}
                    {(asset.brand || asset.model) && (
                      <Text size="xs" c="dimmed">
                        {[asset.brand, asset.model].filter(Boolean).join(" ")}
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td>{asset.assetTag ?? "—"}</Table.Td>
                  <Table.Td>{asset.serialNumber ?? "—"}</Table.Td>
                  <Table.Td>{categoryName(asset.categoryId)}</Table.Td>
                  <Table.Td>
                    <Group gap={4} wrap="wrap">
                      <Badge color={ASSET_STATUS_COLOR[asset.status]} variant="light">
                        {t(`assets:enum.status.${asset.status}`)}
                      </Badge>
                      <UnacknowledgedBadge asset={asset} />
                      <ConditionBadge condition={asset.condition} />
                    </Group>
                  </Table.Td>
                  <Table.Td>
                    {asset.assignedEmployeeId ? (
                      <EmployeeNameLink id={asset.assignedEmployeeId} tab="assets" />
                    ) : (
                      "—"
                    )}
                  </Table.Td>
                  <Table.Td>
                    {asset.status === "assigned" ? date(asset.issuedAt) : "—"}
                    {asset.replacementDueOn && (
                      <div>
                        <ReplacementDueBadge asset={asset} />
                      </div>
                    )}
                  </Table.Td>
                  <Table.Td>
                    {date(asset.purchaseDate)}
                    {asset.purchaseCost != null && (
                      <Text size="xs" c="dimmed">
                        {formatCost(asset.purchaseCost, i18n.language)}
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td>
                    {date(asset.warrantyEndsOn)}
                    <div>
                      <WarrantyBadge asset={asset} />
                    </div>
                  </Table.Td>
                  <Table.Td>
                    <RowActions asset={asset} canManage={canManage} onOpen={setDialog} />
                  </Table.Td>
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
      {canImport && importOpen && <AssetImportDialog onClose={() => setImportOpen(false)} />}
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
      {dialog?.kind === "lost" && (
        <LostAssetDialog asset={dialog.asset} onClose={() => setDialog(null)} onDone={refresh} />
      )}
      {dialog?.kind === "history" && (
        <AssetHistoryDialog asset={dialog.asset} onClose={() => setDialog(null)} />
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

function RowActions({
  asset,
  canManage,
  onOpen,
}: {
  asset: Asset;
  canManage: boolean;
  onOpen: (dialog: Dialog) => void;
}) {
  const { t } = useTranslation(["assets"]);
  // Retired/lost assets are closed for changes; history stays readable. Pending-approval ones can only be edited
  // (the server refuses to mark them lost).
  const manageable = canManage && asset.status !== "retired" && asset.status !== "lost";
  // Same visible text on every row, so the accessible name carries the asset it acts on.
  const label = (action: string) => t("assets:inventory.rowAction", { action, name: asset.name });
  return (
    <Group gap="xs" justify="flex-end" wrap="nowrap">
      {manageable && asset.status === "unassigned" && (
        <Button
          size="compact-sm"
          variant="light"
          aria-label={label(t("assets:section.assign"))}
          onClick={() => onOpen({ kind: "assign", asset })}
        >
          {t("assets:section.assign")}
        </Button>
      )}
      {manageable && asset.status === "assigned" && (
        <Button
          size="compact-sm"
          variant="light"
          aria-label={label(t("assets:section.return"))}
          onClick={() => onOpen({ kind: "return", asset })}
        >
          {t("assets:section.return")}
        </Button>
      )}
      <Button
        size="compact-sm"
        variant="subtle"
        aria-label={label(t("assets:history.action"))}
        onClick={() => onOpen({ kind: "history", asset })}
      >
        {t("assets:history.action")}
      </Button>
      {manageable && (
        <Button
          size="compact-sm"
          variant="subtle"
          aria-label={label(t("assets:inventory.edit"))}
          onClick={() => onOpen({ kind: "form", asset })}
        >
          {t("assets:inventory.edit")}
        </Button>
      )}
      {manageable && (asset.status === "unassigned" || asset.status === "assigned") && (
        <Button
          size="compact-sm"
          variant="subtle"
          color="red"
          aria-label={label(t("assets:lost.action"))}
          onClick={() => onOpen({ kind: "lost", asset })}
        >
          {t("assets:lost.action")}
        </Button>
      )}
      {manageable && asset.status === "unassigned" && (
        <Button
          size="compact-sm"
          variant="subtle"
          color="red"
          aria-label={label(t("assets:inventory.retire"))}
          onClick={() => onOpen({ kind: "retire", asset })}
        >
          {t("assets:inventory.retire")}
        </Button>
      )}
    </Group>
  );
}
