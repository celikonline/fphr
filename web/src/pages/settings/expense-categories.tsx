import { useMemo, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Receipt, Plus, Pencil } from "lucide-react";
import {
  ActionIcon,
  Badge,
  Button,
  Card,
  Group,
  Modal,
  NumberInput,
  Skeleton,
  Stack,
  Switch,
  Text,
  TextInput,
  Title,
  Tooltip,
} from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { expenseCategoryName, isSystemExpenseCategory } from "@/lib/expense-category";
import { formatMoney } from "@/lib/money";
import {
  createExpenseCategory,
  listExpenseCategories,
  setExpenseCategoryActive,
  updateExpenseCategory,
  type ExpenseCategory,
} from "@/services/expense.service";

const CATEGORIES_QUERY_KEY = ["expense", "categories", "admin"] as const;

/**
 * HR/Finance category catalog management (`expense.categories.manage`) - the same catalog employees pick from
 * in the self-service claim form (`payments.tsx`). Mirrors `termination-reasons.tsx`'s list+modal pattern.
 */
export default function ExpenseCategoriesPage() {
  const canView = usePermission("expense.categories.view");
  if (!canView) return <NoAccess />;
  return <ExpenseCategoriesAdmin />;
}

function ExpenseCategoriesAdmin() {
  const { t, i18n } = useTranslation(["expense", "common"]);
  const canManage = usePermission("expense.categories.manage");
  const queryClient = useQueryClient();
  // Dar telefon (Faz 7 gün açılışı tur 1 FE md.3): tablo yerine kart listesi.
  const isMobile = useMediaQuery("(max-width: 390px)");

  const [includeInactive, setIncludeInactive] = useState(true);
  const categoriesQuery = useQuery({
    queryKey: [...CATEGORIES_QUERY_KEY, includeInactive],
    queryFn: () => listExpenseCategories(includeInactive),
  });
  const categories = useMemo(() => categoriesQuery.data ?? [], [categoriesQuery.data]);

  const [dialogOpen, setDialogOpen] = useState(false);
  const [editTarget, setEditTarget] = useState<ExpenseCategory | null>(null);

  const toggleActiveMutation = useMutation({
    mutationFn: (category: ExpenseCategory) => setExpenseCategoryActive(category.id, !category.isActive),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: CATEGORIES_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("expense:categoriesAdmin.statusSaved") });
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("expense:categoriesAdmin.errors.generic")) }),
  });

  const columns: ColumnDef<ExpenseCategory>[] = [
    {
      id: "nameKey",
      accessorKey: "nameKey",
      header: t("expense:categoriesAdmin.columns.name"),
      meta: { label: t("expense:categoriesAdmin.columns.name") },
      cell: ({ row }) => expenseCategoryName(t, row.original),
    },
    {
      id: "code",
      accessorKey: "code",
      header: t("expense:categoriesAdmin.columns.code"),
      meta: { label: t("expense:categoriesAdmin.columns.code") },
      cell: ({ row }) => (
        <Text size="sm" ff="monospace">
          {row.original.code}
        </Text>
      ),
    },
    {
      id: "defaultTaxRate",
      accessorKey: "defaultTaxRate",
      header: t("expense:categoriesAdmin.columns.defaultTaxRate"),
      meta: { label: t("expense:categoriesAdmin.columns.defaultTaxRate"), headerClassName: "w-40" },
      cell: ({ row }) => (row.original.defaultTaxRate == null ? "—" : `%${row.original.defaultTaxRate}`),
    },
    {
      id: "maxAmount",
      accessorKey: "maxAmount",
      header: t("expense:categoriesAdmin.columns.maxAmount"),
      meta: { label: t("expense:categoriesAdmin.columns.maxAmount"), headerClassName: "w-40" },
      cell: ({ row }) =>
        row.original.maxAmount === null || row.original.maxAmount === undefined
          ? "—"
          : formatMoney(row.original.maxAmount, "TRY", i18n.language),
    },
    {
      id: "status",
      accessorKey: "isActive",
      header: t("expense:categoriesAdmin.columns.status"),
      meta: { label: t("expense:categoriesAdmin.columns.status"), headerClassName: "w-32" },
      cell: ({ row }) => (
        <Badge variant={row.original.isActive ? "light" : "outline"} color={row.original.isActive ? "green" : "gray"}>
          {row.original.isActive ? t("expense:categoriesAdmin.active") : t("expense:categoriesAdmin.inactive")}
        </Badge>
      ),
    },
    {
      id: "actions",
      header: "",
      meta: { label: t("common:actions"), headerClassName: "w-40" },
      enableSorting: false,
      cell: ({ row }) =>
        canManage && (
          <Group gap="xs" justify="flex-end" wrap="nowrap">
            <Tooltip label={t("common:edit")}>
              <ActionIcon type="button" variant="subtle" aria-label={t("common:edit")} onClick={() => setEditTarget(row.original)}>
                <Pencil size={16} />
              </ActionIcon>
            </Tooltip>
            <Button
              type="button"
              size="xs"
              variant="subtle"
              color={row.original.isActive ? "red" : "teal"}
              loading={toggleActiveMutation.isPending && toggleActiveMutation.variables?.id === row.original.id}
              onClick={() => toggleActiveMutation.mutate(row.original)}
            >
              {row.original.isActive ? t("expense:categoriesAdmin.deactivate") : t("expense:categoriesAdmin.reactivate")}
            </Button>
          </Group>
        ),
    },
  ];

  const table = useDataTable({ columns, data: categories, getRowId: (r) => r.id });

  return (
    <div className="max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Receipt size={24} /> {t("expense:categoriesAdmin.title")}
          </Title>
          <Text c="dimmed">{t("expense:categoriesAdmin.subtitle")}</Text>
        </div>
        {canManage && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={() => setDialogOpen(true)}>
            {t("expense:categoriesAdmin.add")}
          </Button>
        )}
      </Group>

      <Card withBorder padding="lg">
        <Group justify="space-between" mb="md">
          <Switch
            label={t("expense:categoriesAdmin.showInactive")}
            checked={includeInactive}
            onChange={(e) => setIncludeInactive(e.currentTarget.checked)}
          />
        </Group>
        {categoriesQuery.isError ? (
          <Text size="sm" c="red" ta="center" p="lg">
            {t("expense:categoriesAdmin.errors.loadFailed")}
          </Text>
        ) : isMobile ? (
          <CategoriesMobileList
            categories={categories}
            isLoading={categoriesQuery.isLoading}
            canManage={canManage}
            onEdit={setEditTarget}
            onToggleActive={(category) => toggleActiveMutation.mutate(category)}
            togglePendingId={toggleActiveMutation.isPending ? toggleActiveMutation.variables?.id : undefined}
          />
        ) : (
          <DataTable table={table} isLoading={categoriesQuery.isLoading} emptyMessage={t("expense:categoriesAdmin.empty")} />
        )}
      </Card>

      {dialogOpen && <CategoryDialog onClose={() => setDialogOpen(false)} />}
      {editTarget && <EditCategoryDialog category={editTarget} onClose={() => setEditTarget(null)} />}
    </div>
  );
}

/** ≤390px kart listesi (Faz 7 gün açılışı tur 1 FE md.3): ad, varsa üst limit, aktif rozeti, aksiyonlar. */
function CategoriesMobileList({
  categories,
  isLoading,
  canManage,
  onEdit,
  onToggleActive,
  togglePendingId,
}: {
  categories: ExpenseCategory[];
  isLoading: boolean;
  canManage: boolean;
  onEdit: (category: ExpenseCategory) => void;
  onToggleActive: (category: ExpenseCategory) => void;
  togglePendingId: string | undefined;
}) {
  const { t, i18n } = useTranslation(["expense", "common"]);

  if (isLoading) return <Skeleton h={160} />;

  if (categories.length === 0) {
    return (
      <Text size="sm" c="dimmed" ta="center" p="lg">
        {t("expense:categoriesAdmin.empty")}
      </Text>
    );
  }

  return (
    <Stack gap="sm">
      {categories.map((category) => (
        <Card key={category.id} withBorder padding="sm" radius="md">
          <Group justify="space-between" align="flex-start" wrap="nowrap">
            <Text size="sm" fw={500} truncate="end">
              {expenseCategoryName(t, category)}
            </Text>
            <Badge variant={category.isActive ? "light" : "outline"} color={category.isActive ? "green" : "gray"}>
              {category.isActive ? t("expense:categoriesAdmin.active") : t("expense:categoriesAdmin.inactive")}
            </Badge>
          </Group>
          {category.maxAmount != null && (
            <Text size="xs" c="dimmed" mt={4}>
              {t("expense:categoriesAdmin.columns.maxAmount")}: {formatMoney(category.maxAmount, "TRY", i18n.language)}
            </Text>
          )}
          {canManage && (
            <Group justify="flex-end" gap="xs" mt="xs">
              <Button type="button" size="xs" variant="subtle" leftSection={<Pencil size={14} />} onClick={() => onEdit(category)}>
                {t("common:edit")}
              </Button>
              <Button
                type="button"
                size="xs"
                variant="subtle"
                color={category.isActive ? "red" : "teal"}
                loading={togglePendingId === category.id}
                onClick={() => onToggleActive(category)}
              >
                {category.isActive ? t("expense:categoriesAdmin.deactivate") : t("expense:categoriesAdmin.reactivate")}
              </Button>
            </Group>
          )}
        </Card>
      ))}
    </Stack>
  );
}

function CategoryDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation(["expense", "common"]);
  const queryClient = useQueryClient();

  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [defaultTaxRate, setDefaultTaxRate] = useState<string | number>("");
  const [maxAmount, setMaxAmount] = useState<string | number>("");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () =>
      createExpenseCategory({
        code: code.trim(),
        nameKey: name.trim(),
        defaultTaxRate: defaultTaxRate === "" ? null : Number(defaultTaxRate),
        maxAmount: maxAmount === "" ? null : Number(maxAmount),
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: CATEGORIES_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("expense:categoriesAdmin.saved") });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("expense:categoriesAdmin.errors.generic"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return setError(t("expense:categoriesAdmin.errors.nameRequired"));
    if (!code.trim()) return setError(t("expense:categoriesAdmin.errors.codeRequired"));
    setError(null);
    mutation.mutate();
  }

  return (
    <Modal opened onClose={onClose} title={t("expense:categoriesAdmin.addTitle")} size="32rem">
      <form onSubmit={submit}>
        <Stack gap="md">
          <TextInput
            id="category-name"
            label={t("expense:categoriesAdmin.columns.name")}
            withAsterisk
            value={name}
            maxLength={200}
            onChange={(event) => setName(event.currentTarget.value)}
            disabled={mutation.isPending}
          />
          <TextInput
            id="category-code"
            label={t("expense:categoriesAdmin.columns.code")}
            description={t("expense:categoriesAdmin.codeHint")}
            withAsterisk
            value={code}
            maxLength={100}
            onChange={(event) => setCode(event.currentTarget.value)}
            disabled={mutation.isPending}
            inputWrapperOrder={["label", "input", "description"]}
          />
          <NumberInput
            id="category-tax-rate"
            label={t("expense:categoriesAdmin.columns.defaultTaxRate")}
            suffix="%"
            min={0}
            max={100}
            value={defaultTaxRate}
            onChange={setDefaultTaxRate}
            disabled={mutation.isPending}
          />
          <NumberInput
            id="category-max-amount"
            label={t("expense:categoriesAdmin.columns.maxAmount")}
            description={t("expense:categoriesAdmin.maxAmountHint")}
            suffix=" ₺"
            min={0}
            decimalScale={2}
            value={maxAmount}
            onChange={setMaxAmount}
            disabled={mutation.isPending}
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

function EditCategoryDialog({ category, onClose }: { category: ExpenseCategory; onClose: () => void }) {
  const { t } = useTranslation(["expense", "common"]);
  const queryClient = useQueryClient();

  const isSystem = isSystemExpenseCategory(category);
  const [name, setName] = useState(category.nameKey);
  const [maxAmount, setMaxAmount] = useState<string | number>(category.maxAmount ?? "");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () =>
      updateExpenseCategory(category.id, {
        // Seeded categories keep their resource key: the server rejects a rename (409 system_immutable).
        name: isSystem ? category.nameKey : name.trim(),
        maxAmount: maxAmount === "" ? null : Number(maxAmount),
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: CATEGORIES_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("expense:categoriesAdmin.saved") });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("expense:categoriesAdmin.errors.generic"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return setError(t("expense:categoriesAdmin.errors.nameRequired"));
    setError(null);
    mutation.mutate();
  }

  return (
    <Modal opened onClose={onClose} title={t("expense:categoriesAdmin.editTitle")} size="32rem">
      <form onSubmit={submit}>
        <Stack gap="md">
          <TextInput
            id="edit-category-name"
            label={t("expense:categoriesAdmin.columns.name")}
            withAsterisk
            value={isSystem ? expenseCategoryName(t, category) : name}
            maxLength={200}
            onChange={(event) => setName(event.currentTarget.value)}
            disabled={mutation.isPending || isSystem}
          />
          <NumberInput
            id="edit-category-max-amount"
            label={t("expense:categoriesAdmin.columns.maxAmount")}
            description={t("expense:categoriesAdmin.maxAmountHint")}
            suffix=" ₺"
            min={0}
            decimalScale={2}
            value={maxAmount}
            onChange={setMaxAmount}
            disabled={mutation.isPending}
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
