import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Package } from "lucide-react";
import { Badge, Button, Card, Group, Skeleton, Stack, Tabs, Text, Title } from "@mantine/core";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import AssetInventory, { ASSET_STATUS_COLOR } from "@/components/assets/asset-inventory";
import ReplacementDueBadge from "@/components/assets/replacement-due-badge";
import { ConditionBadge, WarrantyBadge } from "@/components/assets/asset-badges";
import { formatMoment } from "@/components/assets/asset-format";
import { PastAssignmentsSection } from "@/components/assets/assignment-history";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import {
  acknowledgeAsset,
  getMyAssetHistory,
  getMyAssets,
  listAssetCategories,
} from "@/services/assets.service";

/** Zimmet: çalışanın kendi zimmetleri (assets.my.assets) + İK envanteri (assets.assets.view). */
export default function AssetsPage() {
  const canMine = usePermission("assets.my.assets");
  const canInventory = usePermission("assets.assets.view");
  if (!canMine && !canInventory) return <NoAccess />;
  return <AssetsScreen canMine={canMine} canInventory={canInventory} />;
}

function AssetsScreen({ canMine, canInventory }: { canMine: boolean; canInventory: boolean }) {
  const { t } = useTranslation(["assets"]);

  return (
    <Stack gap="md" className="p-6 md:p-10">
      <Title order={2}>{t("assets:page.title")}</Title>
      {canMine && canInventory ? (
        <Tabs defaultValue="inventory" keepMounted={false}>
          <Tabs.List>
            <Tabs.Tab value="inventory">{t("assets:page.inventory")}</Tabs.Tab>
            <Tabs.Tab value="mine">{t("assets:page.mine")}</Tabs.Tab>
          </Tabs.List>
          <Tabs.Panel value="inventory" pt="md">
            <AssetInventory />
          </Tabs.Panel>
          <Tabs.Panel value="mine" pt="md">
            <MyAssets />
          </Tabs.Panel>
        </Tabs>
      ) : canInventory ? (
        <AssetInventory />
      ) : (
        <MyAssets />
      )}
    </Stack>
  );
}

/** Kendi zimmetlerim: şu an üzerimde olan ekipman (teslim onayı dahil) ve altında geçmiş (kapalı) zimmetlerim. */
function MyAssets() {
  const { t, i18n } = useTranslation(["assets", "common"]);
  const queryClient = useQueryClient();
  const canCategories = usePermission("assets.categories.view");
  const assetsQuery = useQuery({ queryKey: ["assets", "my"], queryFn: getMyAssets });
  const categoriesQuery = useQuery({
    queryKey: ["assets", "categories"],
    queryFn: listAssetCategories,
    enabled: canCategories,
  });
  const categoryName = (id: string) => categoriesQuery.data?.find((c) => c.id === id)?.name;
  const assets = assetsQuery.data ?? [];

  const acknowledgeMutation = useMutation({
    mutationFn: (id: string) => acknowledgeAsset(id),
    onSuccess: () => {
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("assets:acknowledgement.success"),
      });
      void queryClient.invalidateQueries({ queryKey: ["assets"] });
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("assets:acknowledgement.failed")),
      }),
  });

  if (assetsQuery.isLoading) return <Skeleton height={120} />;
  if (assetsQuery.isError) return (
      <Text c="red">
        {getApiErrorMessage(assetsQuery.error, t("assets:section.errors.loadFailed"))}
      </Text>
    );
  const history = (
    <PastAssignmentsSection
      queryKey={["assets", "my", "history"]}
      load={getMyAssetHistory}
      heading={t("assets:page.history")}
      emptyText={t("assets:page.historyEmpty")}
      categoryName={canCategories ? categoryName : undefined}
    />
  );

  if (assets.length === 0) {
    return (
      <Stack gap="lg">
        <Card withBorder>
          <Group gap="sm">
            <Package size={18} />
            <Text c="dimmed">{t("assets:page.mineEmpty")}</Text>
          </Group>
        </Card>
        {history}
      </Stack>
    );
  }

  return (
    <Stack gap="lg">
      <Stack gap="sm">
        {assets.map((asset) => {
          const details = [
            categoryName(asset.categoryId),
            [asset.brand, asset.model].filter(Boolean).join(" "),
            asset.assetTag && `${t("assets:section.assetTag")}: ${asset.assetTag}`,
            asset.serialNumber && `${t("assets:section.serialNumber")}: ${asset.serialNumber}`,
            asset.issuedAt &&
              `${t("assets:section.issuedAt")}: ${formatDateOnly(asset.issuedAt, i18n.language)}`,
          ]
            .filter(Boolean)
            .join(" · ");
          const canAcknowledge = asset.status === "assigned" && !asset.acknowledgedAt;
          return (
            <Card key={asset.id} withBorder padding="md">
              <Group justify="space-between" wrap="wrap" align="flex-start" gap="sm">
                <Stack gap={4} className="min-w-0">
                  <Group gap="xs" wrap="wrap">
                    <Text fw={600}>{asset.name}</Text>
                    <ReplacementDueBadge asset={asset} />
                    <WarrantyBadge asset={asset} />
                    <ConditionBadge condition={asset.condition} />
                  </Group>
                  {details && (
                    <Text size="xs" c="dimmed">
                      {details}
                    </Text>
                  )}
                  {asset.description && <Text size="sm">{asset.description}</Text>}
                </Stack>
                <Stack gap="xs" align="flex-end">
                  <Badge color={ASSET_STATUS_COLOR[asset.status]} variant="light">
                    {t(`assets:enum.status.${asset.status}`)}
                  </Badge>
                  {asset.status === "assigned" && asset.acknowledgedAt && (
                    <Badge color="green" variant="outline">
                      {t("assets:acknowledgement.done", {
                        date: formatMoment(asset.acknowledgedAt, i18n.language),
                      })}
                    </Badge>
                  )}
                  {canAcknowledge && (
                    <Button
                      size="compact-sm"
                      aria-label={t("assets:inventory.rowAction", {
                        action: t("assets:acknowledgement.action"),
                        name: asset.name,
                      })}
                      onClick={() => acknowledgeMutation.mutate(asset.id)}
                      loading={acknowledgeMutation.isPending && acknowledgeMutation.variables === asset.id}
                      disabled={acknowledgeMutation.isPending}
                    >
                      {t("assets:acknowledgement.action")}
                    </Button>
                  )}
                </Stack>
              </Group>
            </Card>
          );
        })}
      </Stack>
      {history}
    </Stack>
  );
}
