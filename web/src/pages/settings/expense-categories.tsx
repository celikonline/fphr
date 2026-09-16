import { useMemo, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Receipt, Plus } from "lucide-react";
import { Badge, Button, Card, Group, Modal, NumberInput, Stack, Switch, Text, TextInput, Title } from "@mantine/core";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  createExpenseCategory,
  listExpenseCategories,
  setExpenseCategoryActive,
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
  const { t } = useTranslation(["expense", "common"]);
  const canManage = usePermission("expense.categories.manage");
  const queryClient = useQueryClient();

  const [includeInactive, setIncludeInactive] = useState(true);
  const categoriesQuery = useQuery({
    queryKey: [...CATEGORIES_QUERY_KEY, includeInactive],
    queryFn: () => listExpenseCategories(includeInactive),
  });
  const categories = useMemo(() => categoriesQuery.data ?? [], [categoriesQuery.data]);

  const [dialogOpen, setDialogOpen] = useState(false);

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
      cell: ({ row }) => (row.original.defaultTaxRate === null ? "—" : `%${row.original.defaultTaxRate}`),
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
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
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
        ) : (
          <DataTable table={table} isLoading={categoriesQuery.isLoading} emptyMessage={t("expense:categoriesAdmin.empty")} />
        )}
      </Card>

      {dialogOpen && <CategoryDialog onClose={() => setDialogOpen(false)} />}
    </div>
  );
}

function CategoryDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation(["expense", "common"]);
  const queryClient = useQueryClient();

  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [defaultTaxRate, setDefaultTaxRate] = useState<string | number>("");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () =>
      createExpenseCategory({
        code: code.trim(),
        nameKey: name.trim(),
        defaultTaxRate: defaultTaxRate === "" ? null : Number(defaultTaxRate),
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
