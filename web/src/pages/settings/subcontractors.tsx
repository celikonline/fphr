import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Building, Plus } from "lucide-react";
import { Badge, Button, Card, Group, Input, Modal, Stack, Switch, Text, TextInput, Title } from "@mantine/core";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { DatePicker } from "@/components/ui/date-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import {
  createSubcontractor,
  getSubcontractors,
  setSubcontractorActive,
  updateSubcontractor,
  type Subcontractor,
} from "@/services/employees.service";

const SUBCONTRACTORS_QUERY_KEY = ["employees", "subcontractors"] as const;

export default function SubcontractorsPage() {
  const canView = usePermission("employee.employees.read");
  if (!canView) return <NoAccess />;
  return <SubcontractorsAdmin />;
}

function SubcontractorsAdmin() {
  const { t, i18n } = useTranslation(["settings", "common"]);
  const canManage = usePermission("employee.subcontractor.manage");
  const queryClient = useQueryClient();

  const [includeInactive, setIncludeInactive] = useState(true);
  const query = useQuery({
    queryKey: [...SUBCONTRACTORS_QUERY_KEY, includeInactive],
    queryFn: () => getSubcontractors(includeInactive),
  });

  const [dialogTarget, setDialogTarget] = useState<Subcontractor | "new" | null>(null);

  const toggleActiveMutation = useMutation({
    mutationFn: (subcontractor: Subcontractor) => setSubcontractorActive(subcontractor.id, !subcontractor.isActive),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: SUBCONTRACTORS_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("settings:subcontractors.statusSaved") });
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("settings:subcontractors.errors.generic")) }),
  });

  const date = (value: string | null) => (value ? formatDateOnly(value, i18n.language) : "—");

  const columns: ColumnDef<Subcontractor>[] = [
    {
      id: "name",
      accessorKey: "name",
      header: t("settings:subcontractors.columns.name"),
      meta: { label: t("settings:subcontractors.columns.name") },
    },
    {
      id: "taxNumber",
      accessorKey: "taxNumber",
      header: t("settings:subcontractors.columns.taxNumber"),
      meta: { label: t("settings:subcontractors.columns.taxNumber") },
      cell: ({ row }) => row.original.taxNumber ?? "—",
    },
    {
      id: "contact",
      header: t("settings:subcontractors.columns.contact"),
      meta: { label: t("settings:subcontractors.columns.contact") },
      enableSorting: false,
      cell: ({ row }) => (
        <Stack gap={0}>
          <Text size="sm">{row.original.contactName ?? "—"}</Text>
          <Text size="xs" c="dimmed">
            {[row.original.contactEmail, row.original.contactPhone].filter(Boolean).join(" · ") || "—"}
          </Text>
        </Stack>
      ),
    },
    {
      id: "contract",
      header: t("settings:subcontractors.columns.contract"),
      meta: { label: t("settings:subcontractors.columns.contract") },
      enableSorting: false,
      cell: ({ row }) => `${date(row.original.contractStartDate)} – ${date(row.original.contractEndDate)}`,
    },
    {
      id: "status",
      accessorKey: "isActive",
      header: t("settings:subcontractors.columns.status"),
      meta: { label: t("settings:subcontractors.columns.status"), headerClassName: "w-32" },
      cell: ({ row }) => (
        <Badge variant={row.original.isActive ? "light" : "outline"} color={row.original.isActive ? "green" : "gray"}>
          {row.original.isActive ? t("settings:subcontractors.active") : t("settings:subcontractors.inactive")}
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
              {row.original.isActive ? t("settings:subcontractors.deactivate") : t("settings:subcontractors.reactivate")}
            </Button>
          </Group>
        ),
    },
  ];

  const table = useDataTable({ columns, data: query.data ?? [], getRowId: (s) => s.id });

  return (
    <div className="max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Building size={24} /> {t("settings:subcontractors.title")}
          </Title>
          <Text c="dimmed">{t("settings:subcontractors.subtitle")}</Text>
        </div>
        {canManage && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={() => setDialogTarget("new")}>
            {t("settings:subcontractors.add")}
          </Button>
        )}
      </Group>

      <Card withBorder padding="lg">
        <Group justify="space-between" mb="md">
          <Switch
            label={t("settings:subcontractors.showInactive")}
            checked={includeInactive}
            onChange={(e) => setIncludeInactive(e.currentTarget.checked)}
          />
        </Group>
        {query.isError ? (
          <Text size="sm" c="red" ta="center" p="lg">
            {t("settings:subcontractors.errors.loadFailed")}
          </Text>
        ) : (
          <DataTable table={table} isLoading={query.isLoading} emptyMessage={t("settings:subcontractors.empty")} />
        )}
      </Card>

      {dialogTarget && <SubcontractorDialog target={dialogTarget} onClose={() => setDialogTarget(null)} />}
    </div>
  );
}

function SubcontractorDialog({ target, onClose }: { target: Subcontractor | "new"; onClose: () => void }) {
  const { t } = useTranslation(["settings", "common"]);
  const queryClient = useQueryClient();
  const editing = target === "new" ? null : target;

  const [name, setName] = useState(editing?.name ?? "");
  const [taxNumber, setTaxNumber] = useState(editing?.taxNumber ?? "");
  const [contactName, setContactName] = useState(editing?.contactName ?? "");
  const [contactEmail, setContactEmail] = useState(editing?.contactEmail ?? "");
  const [contactPhone, setContactPhone] = useState(editing?.contactPhone ?? "");
  const [contractStartDate, setContractStartDate] = useState(editing?.contractStartDate ?? "");
  const [contractEndDate, setContractEndDate] = useState(editing?.contractEndDate ?? "");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () => {
      const input = {
        name: name.trim(),
        taxNumber: taxNumber.trim() || null,
        contactName: contactName.trim() || null,
        contactEmail: contactEmail.trim() || null,
        contactPhone: contactPhone.trim() || null,
        contractStartDate: contractStartDate || null,
        contractEndDate: contractEndDate || null,
      };
      return editing ? updateSubcontractor(editing.id, input) : createSubcontractor(input);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: SUBCONTRACTORS_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("settings:subcontractors.saved") });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("settings:subcontractors.errors.generic"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return setError(t("settings:subcontractors.errors.nameRequired"));
    setError(null);
    mutation.mutate();
  }

  return (
    <Modal opened onClose={onClose} title={editing ? t("settings:subcontractors.editTitle") : t("settings:subcontractors.add")} size="40rem">
      <form onSubmit={submit}>
        <Stack gap="md">
          <div className="grid gap-4 sm:grid-cols-2">
            <TextInput
              id="subcontractor-name"
              label={t("settings:subcontractors.columns.name")}
              withAsterisk
              value={name}
              maxLength={200}
              onChange={(event) => setName(event.currentTarget.value)}
              disabled={mutation.isPending}
            />
            <TextInput
              id="subcontractor-tax"
              label={t("settings:subcontractors.columns.taxNumber")}
              value={taxNumber}
              maxLength={20}
              onChange={(event) => setTaxNumber(event.currentTarget.value)}
              disabled={mutation.isPending}
            />
            <TextInput
              id="subcontractor-contactName"
              label={t("settings:subcontractors.contactName")}
              value={contactName}
              maxLength={150}
              onChange={(event) => setContactName(event.currentTarget.value)}
              disabled={mutation.isPending}
            />
            <TextInput
              id="subcontractor-contactEmail"
              label={t("settings:subcontractors.contactEmail")}
              type="email"
              value={contactEmail}
              onChange={(event) => setContactEmail(event.currentTarget.value)}
              disabled={mutation.isPending}
            />
            <TextInput
              id="subcontractor-contactPhone"
              label={t("settings:subcontractors.contactPhone")}
              value={contactPhone}
              onChange={(event) => setContactPhone(event.currentTarget.value)}
              disabled={mutation.isPending}
            />
            <Input.Wrapper id="subcontractor-start" label={t("settings:subcontractors.contractStart")}>
              <DatePicker id="subcontractor-start" value={contractStartDate} onChange={setContractStartDate} clearable disabled={mutation.isPending} />
            </Input.Wrapper>
            <Input.Wrapper id="subcontractor-end" label={t("settings:subcontractors.contractEnd")}>
              <DatePicker id="subcontractor-end" value={contractEndDate} onChange={setContractEndDate} clearable disabled={mutation.isPending} />
            </Input.Wrapper>
          </div>
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
