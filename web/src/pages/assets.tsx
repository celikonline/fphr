import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Package } from "lucide-react";
import { Badge, Card, Group, Skeleton, Stack, Tabs, Text, Title } from "@mantine/core";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import AssetInventory, { ASSET_STATUS_COLOR } from "@/components/assets/asset-inventory";
import { formatDateOnly } from "@/lib/dates";
import { getMyAssets, listAssetCategories } from "@/services/assets.service";

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
    <Stack gap="md">
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

/** Kendi zimmetlerim: şu an üzerimde olan ekipman (atama geçmişi tutulmuyor - IAssetRepository notu). */
function MyAssets() {
  const { t, i18n } = useTranslation(["assets"]);
  const canCategories = usePermission("assets.categories.view");
  const assetsQuery = useQuery({ queryKey: ["assets", "my"], queryFn: getMyAssets });
  const categoriesQuery = useQuery({
    queryKey: ["assets", "categories"],
    queryFn: listAssetCategories,
    enabled: canCategories,
  });
  const categoryName = (id: string) => categoriesQuery.data?.find((c) => c.id === id)?.name;
  const assets = assetsQuery.data ?? [];

  if (assetsQuery.isLoading) return <Skeleton height={120} />;
  if (assetsQuery.isError) return <Text c="red">{t("assets:section.errors.loadFailed")}</Text>;
  if (assets.length === 0) {
    return (
      <Card withBorder>
        <Group gap="sm">
          <Package size={18} />
          <Text c="dimmed">{t("assets:page.mineEmpty")}</Text>
        </Group>
      </Card>
    );
  }

  return (
    <Stack gap="sm">
      {assets.map((asset) => {
        const details = [
          categoryName(asset.categoryId),
          asset.serialNumber && `${t("assets:section.serialNumber")}: ${asset.serialNumber}`,
          asset.issuedAt && `${t("assets:section.issuedAt")}: ${formatDateOnly(asset.issuedAt, i18n.language)}`,
        ]
          .filter(Boolean)
          .join(" · ");
        return (
          <Card key={asset.id} withBorder padding="md">
            <Group justify="space-between" wrap="nowrap" align="flex-start">
              <Stack gap={4}>
                <Text fw={600}>{asset.name}</Text>
                {details && (
                  <Text size="xs" c="dimmed">
                    {details}
                  </Text>
                )}
                {asset.description && <Text size="sm">{asset.description}</Text>}
              </Stack>
              <Badge color={ASSET_STATUS_COLOR[asset.status]} variant="light">
                {t(`assets:enum.status.${asset.status}`)}
              </Badge>
            </Group>
          </Card>
        );
      })}
    </Stack>
  );
}
