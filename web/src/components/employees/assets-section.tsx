import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Badge,
  Button,
  Card,
  Group,
  Modal,
  Select,
  Skeleton,
  Stack,
  Text,
  TextInput,
  Textarea,
  Title,
} from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { PackagePlus, Undo2 } from "lucide-react";
import { DatePicker } from "@/components/ui/date-picker";
import { ReturnAssetForm, type ReturnAssetValues } from "@/components/assets/asset-dialogs";
import { ASSET_STATUS_COLOR } from "@/components/assets/asset-inventory";
import { ConditionBadge, UnacknowledgedBadge, WarrantyBadge } from "@/components/assets/asset-badges";
import { PastAssignmentsSection } from "@/components/assets/assignment-history";
import ReplacementDueBadge from "@/components/assets/replacement-due-badge";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import type { EmployeeCardTabProps } from "@/components/employees/card-tabs";
import {
  assignAsset,
  createAsset,
  getEmployeeAssetHistory,
  getEmployeeAssets,
  getMyAssetHistory,
  getMyAssets,
  listAssetCategories,
  listAssets,
  returnAsset,
  type Asset,
  type AssetCategory,
} from "@/services/assets.service";

export const ASSETS_CARD_TAB_PERMISSIONS = ["assets.assets.view", "assets.my.assets"];

const NEW_ASSET = "__new__";

/** Today as a backend DateOnly ("yyyy-MM-dd") in the user's local calendar. */
function todayDateOnly(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

/** "Zimmet" (roadmap #65): the employee's currently assigned equipment and, below it, past (closed) assignments; HR can assign a (new or existing unassigned) asset or return an assigned one. */
export function AssetsSection({ employee }: EmployeeCardTabProps) {
  const { t, i18n } = useTranslation(["assets", "common"]);
  const canViewAny = usePermission("assets.assets.view");
  const canManage = usePermission("assets.assets.manage");
  const queryClient = useQueryClient();
  const [assigning, setAssigning] = useState(false);
  const [returning, setReturning] = useState<Asset | null>(null);
  // Narrow phones (Faz 4 tur 2 FE, TALIMAT md.4b): assign/return dialogs take the full screen instead of a cramped modal.
  const isMobile = useMediaQuery("(max-width: 390px)");

  const queryKey = ["assets", "employee-assets", employee.id, canViewAny] as const;
  const {
    data: assets = [],
    isLoading,
    isError,
    error,
  } = useQuery({
    queryKey,
    queryFn: () => (canViewAny ? getEmployeeAssets(employee.id) : getMyAssets()),
  });

  const categoriesQuery = useQuery({
    queryKey: ["assets", "categories"],
    queryFn: listAssetCategories,
    enabled: canViewAny || canManage,
  });

  const categoryName = (categoryId: string) =>
    categoriesQuery.data?.find((c) => c.id === categoryId)?.name ?? categoryId;

  const returnMutation = useMutation({
    mutationFn: ({ asset, values }: { asset: Asset; values: ReturnAssetValues }) =>
      returnAsset(asset.id, values.returnedAt, values.condition, values.note),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey });
      void queryClient.invalidateQueries({ queryKey: ["assets"] });
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("assets:section.returned"),
      });
      setReturning(null);
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("assets:section.errors.generic")),
      }),
  });

  if (isLoading) return <Skeleton h={200} />;

  const date = (value: string | null) => (value ? formatDateOnly(value, i18n.language) : "—");

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" wrap="nowrap" mb="md">
        <Title order={4}>{t("assets:section.title")}</Title>
        {canManage && (
          <Button
            type="button"
            size="sm"
            leftSection={<PackagePlus size={16} />}
            onClick={() => setAssigning(true)}
          >
            {t("assets:section.assign")}
          </Button>
        )}
      </Group>

      {isError ? (
        <Text size="sm" c="red">
          {getApiErrorMessage(error, t("assets:section.errors.loadFailed"))}
        </Text>
      ) : assets.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("assets:section.empty")}
        </Text>
      ) : (
        <Stack gap="md">
          {assets.map((asset) => (
            <div key={asset.id} className="flex flex-wrap items-start justify-between gap-2">
              <Stack gap={4} className="min-w-0">
                <Group gap="xs" wrap="wrap">
                  <Text size="sm" fw={500}>
                    {asset.name}
                  </Text>
                  <Badge variant="light">{categoryName(asset.categoryId)}</Badge>
                  <Badge variant="filled" color={ASSET_STATUS_COLOR[asset.status]}>
                    {t(`assets:enum.status.${asset.status}`)}
                  </Badge>
                  <UnacknowledgedBadge asset={asset} />
                  <ConditionBadge condition={asset.condition} />
                  <WarrantyBadge asset={asset} />
                  <ReplacementDueBadge asset={asset} />
                </Group>
                <Text size="xs" c="dimmed">
                  {[
                    asset.serialNumber &&
                      `${t("assets:section.serialNumber")}: ${asset.serialNumber}`,
                    asset.issuedAt && `${t("assets:section.issuedAt")}: ${date(asset.issuedAt)}`,
                  ]
                    .filter(Boolean)
                    .join(" · ")}
                </Text>
              </Stack>
              {canManage && asset.status === "assigned" && (
                <Button
                  type="button"
                  variant="subtle"
                  size="sm"
                  aria-label={t("assets:inventory.rowAction", {
                    action: t("assets:section.return"),
                    name: asset.name,
                  })}
                  leftSection={<Undo2 size={14} />}
                  onClick={() => setReturning(asset)}
                >
                  {t("assets:section.return")}
                </Button>
              )}
            </div>
          ))}
        </Stack>
      )}

      {assigning && (
        <AssignAssetDialog
          employeeId={employee.id}
          categories={categoriesQuery.data ?? []}
          queryKey={queryKey}
          isMobile={isMobile}
          onClose={() => setAssigning(false)}
        />
      )}

      {returning && (
        <Modal
          opened
          onClose={() => setReturning(null)}
          title={t("assets:section.returnTitle", { name: returning.name })}
          size="28rem"
          fullScreen={isMobile}
        >
          <ReturnAssetForm
            busy={returnMutation.isPending}
            minDate={returning.issuedAt ?? undefined}
            defaultCondition={returning.condition}
            onCancel={() => setReturning(null)}
            onSubmit={(values) => returnMutation.mutate({ asset: returning, values })}
          />
        </Modal>
      )}

      {!isError && (
        <div className="mt-6">
          <PastAssignmentsSection
            queryKey={["assets", "employee-history", employee.id, canViewAny]}
            load={() => (canViewAny ? getEmployeeAssetHistory(employee.id) : getMyAssetHistory())}
            heading={t("assets:section.history")}
            headingOrder={5}
            emptyText={t("assets:section.historyEmpty")}
            categoryName={canViewAny ? (id) => categoriesQuery.data?.find((c) => c.id === id)?.name : undefined}
          />
        </div>
      )}
    </Card>
  );
}

interface AssignAssetDialogProps {
  employeeId: string;
  categories: AssetCategory[];
  queryKey: readonly unknown[];
  isMobile?: boolean;
  onClose: () => void;
}

/** Assign either an existing unassigned asset (picked from a select) or a quickly-created new one. */
function AssignAssetDialog({
  employeeId,
  categories,
  queryKey,
  isMobile,
  onClose,
}: AssignAssetDialogProps) {
  const { t } = useTranslation(["assets", "common"]);
  const queryClient = useQueryClient();
  const [assetId, setAssetId] = useState<string>(NEW_ASSET);
  const [categoryId, setCategoryId] = useState<string | null>(categories[0]?.id ?? null);
  const [name, setName] = useState("");
  const [serialNumber, setSerialNumber] = useState("");
  const [description, setDescription] = useState("");
  const [issuedAt, setIssuedAt] = useState(todayDateOnly());
  const [error, setError] = useState<string | null>(null);

  const unassignedQuery = useQuery({
    queryKey: ["assets", "unassigned"],
    queryFn: () => listAssets({ status: "unassigned", page: 1, pageSize: 100 }),
  });

  const mutation = useMutation({
    mutationFn: async () => {
      const targetId =
        assetId === NEW_ASSET
          ? await createAsset({
              categoryId: categoryId!,
              name: name.trim(),
              serialNumber: serialNumber.trim() || null,
              description: description.trim() || null,
            })
          : assetId;
      await assignAsset(targetId, employeeId, issuedAt);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey });
      void queryClient.invalidateQueries({ queryKey: ["assets", "unassigned"] });
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("assets:section.assigned"),
      });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("assets:section.errors.generic"))),
  });

  const noCategories = categories.length === 0;

  function submit(event: FormEvent) {
    event.preventDefault();
    if (assetId === NEW_ASSET && noCategories) return;
    if (assetId === NEW_ASSET && (!categoryId || !name.trim())) {
      setError(t("assets:section.errors.nameAndCategoryRequired"));
      return;
    }

    setError(null);
    mutation.mutate();
  }

  const busy = mutation.isPending;
  const existingOptions = (unassignedQuery.data?.items ?? []).map((asset) => ({
    value: asset.id,
    label: asset.name,
  }));

  return (
    <Modal
      opened
      onClose={onClose}
      title={t("assets:section.assignTitle")}
      size="32rem"
      fullScreen={isMobile}
    >
      <form onSubmit={submit}>
        <Stack gap="md">
          <Select
            id="asset-select"
            label={t("assets:section.pickAsset")}
            data={[{ value: NEW_ASSET, label: t("assets:section.newAsset") }, ...existingOptions]}
            value={assetId}
            onChange={(v) => v !== null && setAssetId(v)}
            allowDeselect={false}
            disabled={busy}
          />
          {assetId === NEW_ASSET && noCategories && (
            <Text size="sm" c="orange">
              {t("assets:section.noCategories")}
            </Text>
          )}
          {assetId === NEW_ASSET && !noCategories && (
            <>
              <Select
                id="asset-category"
                label={t("assets:section.category")}
                withAsterisk
                data={categories.map((c) => ({ value: c.id, label: c.name }))}
                value={categoryId}
                onChange={setCategoryId}
                disabled={busy}
              />
              <TextInput
                id="asset-name"
                label={t("assets:section.name")}
                withAsterisk
                maxLength={255}
                value={name}
                disabled={busy}
                onChange={(e) => setName(e.currentTarget.value)}
              />
              <TextInput
                id="asset-serialNumber"
                label={t("assets:section.serialNumber")}
                maxLength={100}
                value={serialNumber}
                disabled={busy}
                onChange={(e) => setSerialNumber(e.currentTarget.value)}
              />
              <Textarea
                id="asset-description"
                label={t("assets:section.description")}
                maxLength={1000}
                autosize
                minRows={2}
                value={description}
                disabled={busy}
                onChange={(e) => setDescription(e.currentTarget.value)}
              />
            </>
          )}
          <Stack gap={4}>
            <Text size="sm" fw={500}>
              {t("assets:section.issuedAt")}
            </Text>
            <DatePicker
              id="asset-issuedAt"
              value={issuedAt}
              disabled={busy}
              onChange={setIssuedAt}
            />
          </Stack>
          {error && (
            <Text size="sm" c="red">
              {error}
            </Text>
          )}
          <Group justify="flex-end" gap="sm">
            <Button type="button" variant="default" onClick={onClose} disabled={busy}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" loading={busy} disabled={assetId === NEW_ASSET && noCategories}>
              {t("assets:section.assign")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
