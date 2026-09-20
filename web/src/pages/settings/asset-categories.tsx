import { useMemo, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Package, Plus } from "lucide-react";
import { Button, Card, Group, Modal, Skeleton, Stack, Text, TextInput, Title } from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";
import { createAssetCategory, listAssetCategories, updateAssetCategory, type AssetCategory } from "@/services/assets.service";

const CATEGORIES_QUERY_KEY = ["assets", "categories"] as const;

function categoryErrorMessage(error: unknown, t: (key: string) => string): string {
  const problem = getApiProblem(error);
  if (problem?.code === "asset_category.code_taken") return t("assets:categories.errors.codeTaken");
  return getApiErrorMessage(error, t("assets:categories.errors.generic"));
}

export default function AssetCategoriesPage() {
  const canView = usePermission("assets.categories.view");
  const canManage = usePermission("assets.categories.manage");
  if (!canView && !canManage) return <NoAccess />;
  return <AssetCategoriesAdmin />;
}

function AssetCategoriesAdmin() {
  const { t } = useTranslation(["assets", "common"]);
  const canManage = usePermission("assets.categories.manage");
  // Narrow phones (Faz 4 gün açılışı tur 1 FE md.1): card list instead of a horizontally-scrolled table.
  const isMobile = useMediaQuery("(max-width: 390px)");

  const categoriesQuery = useQuery({
    queryKey: CATEGORIES_QUERY_KEY,
    queryFn: () => listAssetCategories(),
  });
  const categories = useMemo(() => categoriesQuery.data ?? [], [categoriesQuery.data]);

  const [dialogTarget, setDialogTarget] = useState<AssetCategory | "new" | null>(null);

  const columns: ColumnDef<AssetCategory>[] = [
    {
      id: "name",
      accessorKey: "name",
      header: t("assets:categories.columns.name"),
      meta: { label: t("assets:categories.columns.name") },
    },
    {
      id: "code",
      accessorKey: "code",
      header: t("assets:categories.columns.code"),
      meta: { label: t("assets:categories.columns.code") },
      cell: ({ row }) => (
        <Text size="sm" ff="monospace">
          {row.original.code}
        </Text>
      ),
    },
    {
      id: "actions",
      header: "",
      meta: { label: t("common:actions"), headerClassName: "w-32" },
      enableSorting: false,
      cell: ({ row }) =>
        canManage && (
          <Group gap="xs" justify="flex-end" wrap="nowrap">
            <Button type="button" size="xs" variant="subtle" onClick={() => setDialogTarget(row.original)}>
              {t("common:edit")}
            </Button>
          </Group>
        ),
    },
  ];

  const table = useDataTable({ columns, data: categories, getRowId: (c) => c.id });

  return (
    <div className="max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Package size={24} /> {t("assets:categories.title")}
          </Title>
          <Text c="dimmed">{t("assets:categories.subtitle")}</Text>
        </div>
        {canManage && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={() => setDialogTarget("new")}>
            {t("assets:categories.add")}
          </Button>
        )}
      </Group>

      {categoriesQuery.isError ? (
        <Card withBorder padding="lg">
          <Text size="sm" c="red" ta="center" p="lg">
            {t("assets:categories.errors.loadFailed")}
          </Text>
        </Card>
      ) : isMobile ? (
        <CategoriesMobileList
          categories={categories}
          isLoading={categoriesQuery.isLoading}
          canManage={canManage}
          onEdit={setDialogTarget}
        />
      ) : (
        <Card withBorder padding={0}>
          <DataTable table={table} isLoading={categoriesQuery.isLoading} emptyMessage={t("assets:categories.empty")} />
        </Card>
      )}

      {dialogTarget && <CategoryDialog target={dialogTarget} onClose={() => setDialogTarget(null)} />}
    </div>
  );
}

/** ≤390px card list mirroring the desktop table's columns (Faz 4 gün açılışı tur 1 FE md.1). */
function CategoriesMobileList({
  categories,
  isLoading,
  canManage,
  onEdit,
}: {
  categories: AssetCategory[];
  isLoading: boolean;
  canManage: boolean;
  onEdit: (category: AssetCategory) => void;
}) {
  const { t } = useTranslation(["assets", "common"]);

  if (isLoading) return <Skeleton h={160} />;

  if (categories.length === 0) {
    return (
      <Card withBorder padding="lg">
        <Text size="sm" c="dimmed" ta="center">
          {t("assets:categories.empty")}
        </Text>
      </Card>
    );
  }

  return (
    <Stack gap="sm">
      {categories.map((category) => (
        <Card key={category.id} withBorder padding="md">
          <Group justify="space-between" align="flex-start" wrap="nowrap">
            <Stack gap={4}>
              <Text size="sm" fw={500}>
                {category.name}
              </Text>
              <Text size="xs" c="dimmed" ff="monospace">
                {category.code}
              </Text>
            </Stack>
            {canManage && (
              <Button type="button" size="xs" variant="subtle" onClick={() => onEdit(category)}>
                {t("common:edit")}
              </Button>
            )}
          </Group>
        </Card>
      ))}
    </Stack>
  );
}

function CategoryDialog({ target, onClose }: { target: AssetCategory | "new"; onClose: () => void }) {
  const { t } = useTranslation(["assets", "common"]);
  const queryClient = useQueryClient();
  const editing = target === "new" ? null : target;

  const [code, setCode] = useState(editing?.code ?? "");
  const [name, setName] = useState(editing?.name ?? "");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: async () => {
      if (editing) {
        await updateAssetCategory(editing.id, name.trim());
      } else {
        await createAssetCategory(code.trim(), name.trim());
      }
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: CATEGORIES_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("assets:categories.saved") });
      onClose();
    },
    onError: (err) => setError(categoryErrorMessage(err, t)),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return setError(t("assets:categories.errors.nameRequired"));
    if (!editing && !code.trim()) return setError(t("assets:categories.errors.codeRequired"));
    setError(null);
    mutation.mutate();
  }

  return (
    <Modal opened onClose={onClose} title={editing ? t("assets:categories.editTitle") : t("assets:categories.add")} size="32rem">
      <form onSubmit={submit}>
        <Stack gap="md">
          <TextInput
            id="category-name"
            label={t("assets:categories.columns.name")}
            withAsterisk
            value={name}
            maxLength={200}
            onChange={(event) => setName(event.currentTarget.value)}
            disabled={mutation.isPending}
          />
          <TextInput
            id="category-code"
            label={t("assets:categories.columns.code")}
            description={editing ? t("assets:categories.codeLocked") : t("assets:categories.codeHint")}
            withAsterisk={!editing}
            value={code}
            maxLength={100}
            onChange={(event) => setCode(event.currentTarget.value)}
            disabled={Boolean(editing) || mutation.isPending}
            inputWrapperOrder={["label", "input", "description"]}
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
