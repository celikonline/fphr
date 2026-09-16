import { useMemo, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { LogOut, Plus } from "lucide-react";
import { Badge, Button, Card, Group, Modal, NumberInput, Select, Stack, Switch, Text, TextInput, Title } from "@mantine/core";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";
import {
  createTerminationReason,
  getTerminationReasons,
  setTerminationReasonActive,
  updateTerminationReason,
  TERMINATION_TYPES,
  type TerminationReason,
  type TerminationType,
} from "@/services/employees.service";

const REASONS_QUERY_KEY = ["employees", "termination-reasons"] as const;
const NONE = "__none__";

function reasonErrorMessage(error: unknown, t: (key: string) => string): string {
  const problem = getApiProblem(error);
  if (problem?.code === "employee.termination_reason_code_taken") return t("settings:terminationReasons.errors.codeTaken");
  return getApiErrorMessage(error, t("settings:terminationReasons.errors.generic"));
}

export default function TerminationReasonsPage() {
  const canView = usePermission("employee.employees.read");
  if (!canView) return <NoAccess />;
  return <TerminationReasonsAdmin />;
}

function TerminationReasonsAdmin() {
  const { t } = useTranslation(["settings", "employees", "common"]);
  const canManage = usePermission("employee.termination_reason.manage");
  const queryClient = useQueryClient();

  const [includeInactive, setIncludeInactive] = useState(true);
  const reasonsQuery = useQuery({
    queryKey: [...REASONS_QUERY_KEY, includeInactive],
    queryFn: () => getTerminationReasons(includeInactive),
  });
  const reasons = useMemo(() => reasonsQuery.data ?? [], [reasonsQuery.data]);

  const [dialogTarget, setDialogTarget] = useState<TerminationReason | "new" | null>(null);

  const toggleActiveMutation = useMutation({
    mutationFn: (reason: TerminationReason) => setTerminationReasonActive(reason.id, !reason.isActive),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: REASONS_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("settings:terminationReasons.statusSaved") });
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: reasonErrorMessage(error, t) }),
  });

  const columns: ColumnDef<TerminationReason>[] = [
    {
      id: "name",
      accessorKey: "name",
      header: t("settings:terminationReasons.columns.name"),
      meta: { label: t("settings:terminationReasons.columns.name") },
    },
    {
      id: "code",
      accessorKey: "code",
      header: t("settings:terminationReasons.columns.code"),
      meta: { label: t("settings:terminationReasons.columns.code") },
      cell: ({ row }) => (
        <Text size="sm" ff="monospace">
          {row.original.code}
        </Text>
      ),
    },
    {
      id: "defaultTerminationType",
      accessorKey: "defaultTerminationType",
      header: t("settings:terminationReasons.columns.type"),
      meta: { label: t("settings:terminationReasons.columns.type") },
      cell: ({ row }) =>
        row.original.defaultTerminationType ? t(`employees:enum.terminationType.${row.original.defaultTerminationType}`) : "—",
    },
    {
      id: "defaultSgkExitCode",
      accessorKey: "defaultSgkExitCode",
      header: t("settings:terminationReasons.columns.sgkExitCode"),
      meta: { label: t("settings:terminationReasons.columns.sgkExitCode"), headerClassName: "w-32" },
      cell: ({ row }) => row.original.defaultSgkExitCode ?? "—",
    },
    {
      id: "origin",
      header: t("settings:terminationReasons.columns.origin"),
      meta: { label: t("settings:terminationReasons.columns.origin"), headerClassName: "w-32" },
      enableSorting: false,
      cell: ({ row }) => (
        <Badge size="xs" variant="light" color={row.original.isSystem ? "blue" : "teal"}>
          {row.original.isSystem ? t("settings:terminationReasons.system") : t("settings:terminationReasons.custom")}
        </Badge>
      ),
    },
    {
      id: "status",
      accessorKey: "isActive",
      header: t("settings:terminationReasons.columns.status"),
      meta: { label: t("settings:terminationReasons.columns.status"), headerClassName: "w-32" },
      cell: ({ row }) => (
        <Badge variant={row.original.isActive ? "light" : "outline"} color={row.original.isActive ? "green" : "gray"}>
          {row.original.isActive ? t("settings:terminationReasons.active") : t("settings:terminationReasons.inactive")}
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
              {row.original.isActive ? t("settings:terminationReasons.deactivate") : t("settings:terminationReasons.reactivate")}
            </Button>
          </Group>
        ),
    },
  ];

  const table = useDataTable({ columns, data: reasons, getRowId: (r) => r.id });

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <LogOut size={24} /> {t("settings:terminationReasons.title")}
          </Title>
          <Text c="dimmed">{t("settings:terminationReasons.subtitle")}</Text>
        </div>
        {canManage && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={() => setDialogTarget("new")}>
            {t("settings:terminationReasons.add")}
          </Button>
        )}
      </Group>

      <Card withBorder padding="lg">
        <Group justify="space-between" mb="md">
          <Switch
            label={t("settings:terminationReasons.showInactive")}
            checked={includeInactive}
            onChange={(e) => setIncludeInactive(e.currentTarget.checked)}
          />
        </Group>
        {reasonsQuery.isError ? (
          <Text size="sm" c="red" ta="center" p="lg">
            {t("settings:terminationReasons.errors.loadFailed")}
          </Text>
        ) : (
          <>
            {/* Geniş ekran: tablo */}
            <div className="hidden md:block">
              <DataTable table={table} isLoading={reasonsQuery.isLoading} emptyMessage={t("settings:terminationReasons.empty")} />
            </div>

            {/* Dar ekran (≤390px dahil): kart listesi */}
            <div className="md:hidden">
              {reasonsQuery.isLoading ? (
                <Text size="sm" c="dimmed">
                  {t("common:loading")}
                </Text>
              ) : reasons.length === 0 ? (
                <Text size="sm" c="dimmed" ta="center" p="lg">
                  {t("settings:terminationReasons.empty")}
                </Text>
              ) : (
                <Stack gap="sm">
                  {reasons.map((reason) => (
                    <Card key={reason.id} withBorder padding="sm" radius="md">
                      <Group justify="space-between" align="flex-start" wrap="nowrap">
                        <div>
                          <Text fw={500} size="sm">
                            {reason.name}
                          </Text>
                          <Text size="xs" c="dimmed" ff="monospace">
                            {reason.code}
                          </Text>
                          {reason.defaultSgkExitCode && (
                            <Text size="xs" c="dimmed">
                              {t("settings:terminationReasons.columns.sgkExitCode")}: {reason.defaultSgkExitCode}
                            </Text>
                          )}
                        </div>
                        <Badge variant={reason.isActive ? "light" : "outline"} color={reason.isActive ? "green" : "gray"}>
                          {reason.isActive ? t("settings:terminationReasons.active") : t("settings:terminationReasons.inactive")}
                        </Badge>
                      </Group>
                      {canManage && (
                        <Group gap="xs" justify="flex-end" mt="sm">
                          <Button type="button" size="xs" variant="subtle" onClick={() => setDialogTarget(reason)}>
                            {t("common:edit")}
                          </Button>
                          <Button
                            type="button"
                            size="xs"
                            variant="subtle"
                            color={reason.isActive ? "red" : "teal"}
                            loading={toggleActiveMutation.isPending && toggleActiveMutation.variables?.id === reason.id}
                            onClick={() => toggleActiveMutation.mutate(reason)}
                          >
                            {reason.isActive ? t("settings:terminationReasons.deactivate") : t("settings:terminationReasons.reactivate")}
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

      {dialogTarget && <ReasonDialog target={dialogTarget} onClose={() => setDialogTarget(null)} />}
    </div>
  );
}

function ReasonDialog({ target, onClose }: { target: TerminationReason | "new"; onClose: () => void }) {
  const { t } = useTranslation(["settings", "employees", "common"]);
  const queryClient = useQueryClient();
  const editing = target === "new" ? null : target;

  const [code, setCode] = useState(editing?.code ?? "");
  const [name, setName] = useState(editing?.name ?? "");
  const [sgkExitCode, setSgkExitCode] = useState(editing?.defaultSgkExitCode ?? "");
  const [type, setType] = useState<TerminationType | null>(editing?.defaultTerminationType ?? null);
  const [sortOrder, setSortOrder] = useState(String(editing ? 0 : 500));
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () => {
      const input = {
        name: name.trim(),
        defaultSgkExitCode: sgkExitCode.trim() || null,
        defaultTerminationType: type,
        sortOrder: Number(sortOrder) || 0,
      };
      return editing ? updateTerminationReason(editing.id, input) : createTerminationReason({ ...input, code: code.trim() });
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: REASONS_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("settings:terminationReasons.saved") });
      onClose();
    },
    onError: (err) => setError(reasonErrorMessage(err, t)),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return setError(t("settings:terminationReasons.errors.nameRequired"));
    if (!editing && !code.trim()) return setError(t("settings:terminationReasons.errors.codeRequired"));
    setError(null);
    mutation.mutate();
  }

  return (
    <Modal opened onClose={onClose} title={editing ? t("settings:terminationReasons.editTitle") : t("settings:terminationReasons.add")} size="36rem">
      <form onSubmit={submit}>
        <Stack gap="md">
          <TextInput
            id="reason-name"
            label={t("settings:terminationReasons.columns.name")}
            withAsterisk
            value={name}
            maxLength={150}
            onChange={(event) => setName(event.currentTarget.value)}
            disabled={mutation.isPending}
          />
          <TextInput
            id="reason-code"
            label={t("settings:terminationReasons.columns.code")}
            description={editing ? t("settings:terminationReasons.codeLocked") : t("settings:terminationReasons.codeHint")}
            withAsterisk={!editing}
            value={code}
            maxLength={100}
            onChange={(event) => setCode(event.currentTarget.value)}
            disabled={Boolean(editing) || mutation.isPending}
            inputWrapperOrder={["label", "input", "description"]}
          />
          <Select
            id="reason-type"
            label={t("settings:terminationReasons.columns.type")}
            description={t("settings:terminationReasons.typeHint")}
            data={[
              { value: NONE, label: t("employees:enum.notSet") },
              ...TERMINATION_TYPES.map((option) => ({ value: option, label: t(`employees:enum.terminationType.${option}`) })),
            ]}
            value={type ?? NONE}
            onChange={(value) => setType(value === null || value === NONE ? null : (value as TerminationType))}
            allowDeselect={false}
            disabled={mutation.isPending}
            inputWrapperOrder={["label", "input", "description"]}
          />
          <Group grow>
            <TextInput
              id="reason-sgk"
              label={t("settings:terminationReasons.columns.sgkExitCode")}
              value={sgkExitCode}
              maxLength={20}
              onChange={(event) => setSgkExitCode(event.currentTarget.value)}
              disabled={mutation.isPending}
            />
            <NumberInput
              id="reason-sort"
              label={t("settings:terminationReasons.columns.sortOrder")}
              value={sortOrder}
              onChange={(value) => setSortOrder(String(value ?? 0))}
              disabled={mutation.isPending}
            />
          </Group>
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
