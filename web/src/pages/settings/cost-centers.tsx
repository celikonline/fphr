import { useMemo, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Landmark, Plus } from "lucide-react";
import { Badge, Button, Card, Group, Modal, Stack, Switch, Text, TextInput, Title } from "@mantine/core";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";
import {
  createCostCenter,
  getCostCenters,
  renameCostCenter,
  setCostCenterActive,
  type CostCenter,
} from "@/services/organization.service";

const COST_CENTERS_QUERY_KEY = ["organization", "cost-centers"] as const;

function costCenterErrorMessage(error: unknown, t: (key: string) => string): string {
  const problem = getApiProblem(error);
  if (problem?.code === "organization.cost_center.code_taken") return t("settings:costCenters.errors.codeTaken");
  return getApiErrorMessage(error, t("settings:costCenters.errors.generic"));
}

export default function CostCentersPage() {
  const canView = usePermission("organization.cost_centers.view");
  if (!canView) return <NoAccess />;
  return <CostCentersAdmin />;
}

function CostCentersAdmin() {
  const { t } = useTranslation(["settings", "common"]);
  const canManage = usePermission("organization.cost_centers.manage");
  const queryClient = useQueryClient();

  const [includeInactive, setIncludeInactive] = useState(true);
  const costCentersQuery = useQuery({
    queryKey: [...COST_CENTERS_QUERY_KEY, includeInactive],
    queryFn: () => getCostCenters(includeInactive),
  });
  const costCenters = useMemo(() => costCentersQuery.data ?? [], [costCentersQuery.data]);

  const [dialogTarget, setDialogTarget] = useState<CostCenter | "new" | null>(null);

  const toggleActiveMutation = useMutation({
    mutationFn: (costCenter: CostCenter) => setCostCenterActive(costCenter.id, !costCenter.isActive),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: COST_CENTERS_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("settings:costCenters.statusSaved") });
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: costCenterErrorMessage(error, t) }),
  });

  const columns: ColumnDef<CostCenter>[] = [
    {
      id: "name",
      accessorKey: "name",
      header: t("settings:costCenters.columns.name"),
      meta: { label: t("settings:costCenters.columns.name") },
    },
    {
      id: "code",
      accessorKey: "code",
      header: t("settings:costCenters.columns.code"),
      meta: { label: t("settings:costCenters.columns.code") },
      cell: ({ row }) => (
        <Text size="sm" ff="monospace">
          {row.original.code}
        </Text>
      ),
    },
    {
      id: "status",
      accessorKey: "isActive",
      header: t("settings:costCenters.columns.status"),
      meta: { label: t("settings:costCenters.columns.status"), headerClassName: "w-32" },
      cell: ({ row }) => (
        <Badge variant={row.original.isActive ? "light" : "outline"} color={row.original.isActive ? "green" : "gray"}>
          {row.original.isActive ? t("settings:costCenters.active") : t("settings:costCenters.inactive")}
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
            <Button type="button" size="xs" variant="subtle" onClick={() => setDialogTarget(row.original)}>
              {t("common:edit")}
            </Button>
            <Button
              type="button"
              size="xs"
              variant="subtle"
              color={row.original.isActive ? "red" : "teal"}
              loading={toggleActiveMutation.isPending && toggleActiveMutation.variables?.id === row.original.id}
              onClick={() => toggleActiveMutation.mutate(row.original)}
            >
              {row.original.isActive ? t("settings:costCenters.deactivate") : t("settings:costCenters.reactivate")}
            </Button>
          </Group>
        ),
    },
  ];

  const table = useDataTable({ columns, data: costCenters, getRowId: (r) => r.id });

  return (
    <div className="max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Landmark size={24} /> {t("settings:costCenters.title")}
          </Title>
          <Text c="dimmed">{t("settings:costCenters.subtitle")}</Text>
        </div>
        {canManage && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={() => setDialogTarget("new")}>
            {t("settings:costCenters.add")}
          </Button>
        )}
      </Group>

      <Card withBorder padding="lg">
        <Group justify="space-between" mb="md">
          <Switch
            label={t("settings:costCenters.showInactive")}
            checked={includeInactive}
            onChange={(e) => setIncludeInactive(e.currentTarget.checked)}
          />
        </Group>
        {costCentersQuery.isError ? (
          <Text size="sm" c="red" ta="center" p="lg">
            {t("settings:costCenters.errors.loadFailed")}
          </Text>
        ) : (
          <>
            {/* Geniş ekran: tablo */}
            <div className="hidden md:block">
              <DataTable table={table} isLoading={costCentersQuery.isLoading} emptyMessage={t("settings:costCenters.empty")} />
            </div>

            {/* Dar ekran (≤390px dahil): kart listesi */}
            <div className="md:hidden">
              {costCentersQuery.isLoading ? (
                <Text size="sm" c="dimmed">
                  {t("common:loading")}
                </Text>
              ) : costCenters.length === 0 ? (
                <Text size="sm" c="dimmed" ta="center" p="lg">
                  {t("settings:costCenters.empty")}
                </Text>
              ) : (
                <Stack gap="sm">
                  {costCenters.map((costCenter) => (
                    <Card key={costCenter.id} withBorder padding="sm" radius="md">
                      <Group justify="space-between" align="flex-start" wrap="nowrap">
                        <div>
                          <Text fw={500} size="sm">
                            {costCenter.name}
                          </Text>
                          <Text size="xs" c="dimmed" ff="monospace">
                            {costCenter.code}
                          </Text>
                        </div>
                        <Badge variant={costCenter.isActive ? "light" : "outline"} color={costCenter.isActive ? "green" : "gray"}>
                          {costCenter.isActive ? t("settings:costCenters.active") : t("settings:costCenters.inactive")}
                        </Badge>
                      </Group>
                      {canManage && (
                        <Group gap="xs" justify="flex-end" mt="sm">
                          <Button type="button" size="xs" variant="subtle" onClick={() => setDialogTarget(costCenter)}>
                            {t("common:edit")}
                          </Button>
                          <Button
                            type="button"
                            size="xs"
                            variant="subtle"
                            color={costCenter.isActive ? "red" : "teal"}
                            loading={toggleActiveMutation.isPending && toggleActiveMutation.variables?.id === costCenter.id}
                            onClick={() => toggleActiveMutation.mutate(costCenter)}
                          >
                            {costCenter.isActive ? t("settings:costCenters.deactivate") : t("settings:costCenters.reactivate")}
                          </Button>
                        </Group>
                      )}
                    </Card>
                  ))}
                </Stack>
              )}
            </div>
          </>
        )}
      </Card>

      {dialogTarget && <CostCenterDialog target={dialogTarget} onClose={() => setDialogTarget(null)} />}
    </div>
  );
}

function CostCenterDialog({ target, onClose }: { target: CostCenter | "new"; onClose: () => void }) {
  const { t } = useTranslation(["settings", "common"]);
  const queryClient = useQueryClient();
  const editing = target === "new" ? null : target;

  const [code, setCode] = useState(editing?.code ?? "");
  const [name, setName] = useState(editing?.name ?? "");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: async () => {
      if (editing) {
        await renameCostCenter(editing.id, name.trim());
      } else {
        await createCostCenter({ code: code.trim(), name: name.trim() });
      }
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: COST_CENTERS_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("settings:costCenters.saved") });
      onClose();
    },
    onError: (err) => setError(costCenterErrorMessage(err, t)),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return setError(t("settings:costCenters.errors.nameRequired"));
    if (!editing && !code.trim()) return setError(t("settings:costCenters.errors.codeRequired"));
    setError(null);
    mutation.mutate();
  }

  return (
    <Modal
      opened
      onClose={onClose}
      title={editing ? t("settings:costCenters.editTitle") : t("settings:costCenters.add")}
      size="32rem"
    >
      <form onSubmit={submit}>
        <Stack gap="md">
          <TextInput
            id="cost-center-name"
            label={t("settings:costCenters.columns.name")}
            withAsterisk
            value={name}
            maxLength={200}
            onChange={(event) => setName(event.currentTarget.value)}
            disabled={mutation.isPending}
          />
          <TextInput
            id="cost-center-code"
            label={t("settings:costCenters.columns.code")}
            description={editing ? t("settings:costCenters.codeLocked") : t("settings:costCenters.codeHint")}
            withAsterisk={!editing}
            value={code}
            maxLength={30}
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
