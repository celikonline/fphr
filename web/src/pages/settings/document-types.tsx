import { useMemo, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { FileStack, Plus } from "lucide-react";
import { Badge, Button, Card, Group, Modal, NumberInput, Select, Stack, Switch, Text, TextInput, Title } from "@mantine/core";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";
import {
  createDocumentType,
  listDocumentTypes,
  setDocumentTypeActive,
  updateDocumentType,
  DOCUMENT_REQUIREMENT_CONDITIONS,
  DOCUMENT_TYPE_STAGES,
  type DocumentRequirementCondition,
  type DocumentType,
  type DocumentTypeStage,
} from "@/services/documents.service";

const TYPES_QUERY_KEY = ["documents", "document-types"] as const;
const NONE = "__none__";

function typeErrorMessage(error: unknown, t: (key: string) => string): string {
  const problem = getApiProblem(error);
  if (problem?.code === "document_type.code_required") return t("documents:documentTypes.errors.codeRequired");
  if (problem?.code === "document_type.code_too_long") return t("documents:documentTypes.errors.codeTooLong");
  if (problem?.code === "document_type.name_required") return t("documents:documentTypes.errors.nameRequired");
  if (problem?.code === "document_type.condition_required_for_conditional") return t("documents:documentTypes.errors.conditionRequired");
  if (problem?.code === "document_type.condition_not_allowed") return t("documents:documentTypes.errors.conditionNotAllowed");
  if (problem?.code === "document_type.invalid_quantity") return t("documents:documentTypes.errors.invalidQuantity");
  if (problem?.code === "document_type.invalid_renewal_period") return t("documents:documentTypes.errors.invalidRenewalPeriod");
  if (problem?.code === "document_type.invalid_retention") return t("documents:documentTypes.errors.invalidRetention");
  return getApiErrorMessage(error, t("documents:documentTypes.errors.generic"));
}

export default function DocumentTypesPage() {
  const canView = usePermission("documents.document_types.view");
  const canManage = usePermission("documents.document_types.manage");
  if (!canView && !canManage) return <NoAccess />;
  return <DocumentTypesAdmin />;
}

function DocumentTypesAdmin() {
  const { t } = useTranslation(["documents", "common"]);
  const canManage = usePermission("documents.document_types.manage");
  const queryClient = useQueryClient();

  const [includeInactive, setIncludeInactive] = useState(true);
  const [stageFilter, setStageFilter] = useState<DocumentTypeStage | typeof NONE>(NONE);
  const typesQuery = useQuery({
    queryKey: [...TYPES_QUERY_KEY, includeInactive, stageFilter],
    queryFn: () =>
      listDocumentTypes({
        stage: stageFilter === NONE ? undefined : stageFilter,
        isActive: includeInactive ? undefined : true,
      }),
  });
  const types = useMemo(() => typesQuery.data ?? [], [typesQuery.data]);

  const [dialogTarget, setDialogTarget] = useState<DocumentType | "new" | null>(null);

  const toggleActiveMutation = useMutation({
    mutationFn: (type: DocumentType) => setDocumentTypeActive(type.id, !type.isActive),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: TYPES_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("documents:documentTypes.statusSaved") });
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: typeErrorMessage(error, t) }),
  });

  const columns: ColumnDef<DocumentType>[] = [
    {
      id: "name",
      accessorKey: "name",
      header: t("documents:documentTypes.columns.name"),
      meta: { label: t("documents:documentTypes.columns.name") },
    },
    {
      id: "code",
      accessorKey: "code",
      header: t("documents:documentTypes.columns.code"),
      meta: { label: t("documents:documentTypes.columns.code") },
      cell: ({ row }) => (
        <Text size="sm" ff="monospace">
          {row.original.code}
        </Text>
      ),
    },
    {
      id: "stage",
      accessorKey: "stage",
      header: t("documents:documentTypes.columns.stage"),
      meta: { label: t("documents:documentTypes.columns.stage") },
      cell: ({ row }) => t(`documents:enum.stage.${row.original.stage}`),
    },
    {
      id: "condition",
      header: t("documents:documentTypes.columns.condition"),
      meta: { label: t("documents:documentTypes.columns.condition") },
      enableSorting: false,
      cell: ({ row }) => (row.original.condition ? t(`documents:enum.condition.${row.original.condition}`) : "—"),
    },
    {
      id: "isRequired",
      header: t("documents:documentTypes.columns.required"),
      meta: { label: t("documents:documentTypes.columns.required"), headerClassName: "w-24" },
      enableSorting: false,
      cell: ({ row }) => (
        <Badge size="xs" variant={row.original.isRequired ? "filled" : "light"} color={row.original.isRequired ? "red" : "gray"}>
          {row.original.isRequired ? t("common:yes") : t("common:no")}
        </Badge>
      ),
    },
    {
      id: "origin",
      header: t("documents:documentTypes.columns.origin"),
      meta: { label: t("documents:documentTypes.columns.origin"), headerClassName: "w-32" },
      enableSorting: false,
      cell: ({ row }) => (
        <Badge size="xs" variant="light" color={row.original.isSystem ? "blue" : "teal"}>
          {row.original.isSystem ? t("documents:documentTypes.system") : t("documents:documentTypes.custom")}
        </Badge>
      ),
    },
    {
      id: "status",
      accessorKey: "isActive",
      header: t("documents:documentTypes.columns.status"),
      meta: { label: t("documents:documentTypes.columns.status"), headerClassName: "w-32" },
      cell: ({ row }) => (
        <Badge variant={row.original.isActive ? "light" : "outline"} color={row.original.isActive ? "green" : "gray"}>
          {row.original.isActive ? t("documents:documentTypes.active") : t("documents:documentTypes.inactive")}
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
              {row.original.isActive ? t("documents:documentTypes.deactivate") : t("documents:documentTypes.reactivate")}
            </Button>
          </Group>
        ),
    },
  ];

  const table = useDataTable({ columns, data: types, getRowId: (r) => r.id });

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <FileStack size={24} /> {t("documents:documentTypes.title")}
          </Title>
          <Text c="dimmed">{t("documents:documentTypes.subtitle")}</Text>
        </div>
        {canManage && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={() => setDialogTarget("new")}>
            {t("documents:documentTypes.add")}
          </Button>
        )}
      </Group>

      <Card withBorder padding="lg">
        <Group justify="space-between" mb="md">
          <Select
            w={220}
            data={[
              { value: NONE, label: t("documents:documentTypes.allStages") },
              ...DOCUMENT_TYPE_STAGES.map((stage) => ({ value: stage, label: t(`documents:enum.stage.${stage}`) })),
            ]}
            value={stageFilter}
            onChange={(value) => setStageFilter((value as DocumentTypeStage | null) ?? NONE)}
            allowDeselect={false}
          />
          <Switch
            label={t("documents:documentTypes.showInactive")}
            checked={includeInactive}
            onChange={(e) => setIncludeInactive(e.currentTarget.checked)}
          />
        </Group>
        {typesQuery.isError ? (
          <Text size="sm" c="red" ta="center" p="lg">
            {t("documents:documentTypes.errors.loadFailed")}
          </Text>
        ) : (
          <DataTable table={table} isLoading={typesQuery.isLoading} emptyMessage={t("documents:documentTypes.empty")} />
        )}
      </Card>

      {dialogTarget && <TypeDialog target={dialogTarget} onClose={() => setDialogTarget(null)} />}
    </div>
  );
}

function TypeDialog({ target, onClose }: { target: DocumentType | "new"; onClose: () => void }) {
  const { t } = useTranslation(["documents", "common"]);
  const queryClient = useQueryClient();
  const editing = target === "new" ? null : target;

  const [code, setCode] = useState(editing?.code ?? "");
  const [name, setName] = useState(editing?.name ?? "");
  const [stage, setStage] = useState<DocumentTypeStage>(editing?.stage ?? "onboarding");
  const [condition, setCondition] = useState<DocumentRequirementCondition | null>(editing?.condition ?? null);
  const [isRequired, setIsRequired] = useState(editing?.isRequired ?? true);
  const [renewalPeriodDays, setRenewalPeriodDays] = useState(editing?.renewalPeriodDays != null ? String(editing.renewalPeriodDays) : "");
  const [quantity, setQuantity] = useState(String(editing?.quantity ?? 1));
  const [isSensitive, setIsSensitive] = useState(editing?.isSensitive ?? false);
  const [defaultRetentionDays, setDefaultRetentionDays] = useState(
    editing?.defaultRetentionDays != null ? String(editing.defaultRetentionDays) : "",
  );
  const [sortOrder, setSortOrder] = useState(String(editing ? editing.sortOrder : 500));
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () => {
      const shared = {
        name: name.trim(),
        isRequired,
        renewalPeriodDays: renewalPeriodDays.trim() ? Number(renewalPeriodDays) : null,
        quantity: Number(quantity) || 1,
        isSensitive,
        defaultRetentionDays: defaultRetentionDays.trim() ? Number(defaultRetentionDays) : null,
        sortOrder: Number(sortOrder) || 0,
      };
      return editing
        ? updateDocumentType(editing.id, shared)
        : createDocumentType({ ...shared, code: code.trim(), stage, condition: stage === "conditional" ? condition : null });
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: TYPES_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("documents:documentTypes.saved") });
      onClose();
    },
    onError: (err) => setError(typeErrorMessage(err, t)),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return setError(t("documents:documentTypes.errors.nameRequired"));
    if (!editing && !code.trim()) return setError(t("documents:documentTypes.errors.codeRequired"));
    if (!editing && stage === "conditional" && !condition) return setError(t("documents:documentTypes.errors.conditionRequired"));
    setError(null);
    mutation.mutate();
  }

  return (
    <Modal
      opened
      onClose={onClose}
      title={editing ? t("documents:documentTypes.editTitle") : t("documents:documentTypes.add")}
      size="36rem"
    >
      <form onSubmit={submit}>
        <Stack gap="md">
          <TextInput
            id="doctype-name"
            label={t("documents:documentTypes.columns.name")}
            withAsterisk
            value={name}
            maxLength={255}
            onChange={(event) => setName(event.currentTarget.value)}
            disabled={mutation.isPending}
          />
          <TextInput
            id="doctype-code"
            label={t("documents:documentTypes.columns.code")}
            description={editing ? t("documents:documentTypes.codeLocked") : t("documents:documentTypes.codeHint")}
            withAsterisk={!editing}
            value={code}
            maxLength={100}
            onChange={(event) => setCode(event.currentTarget.value)}
            disabled={Boolean(editing) || mutation.isPending}
            inputWrapperOrder={["label", "input", "description"]}
          />
          <Group grow>
            <Select
              id="doctype-stage"
              label={t("documents:documentTypes.columns.stage")}
              description={editing ? t("documents:documentTypes.stageLocked") : undefined}
              data={DOCUMENT_TYPE_STAGES.map((option) => ({ value: option, label: t(`documents:enum.stage.${option}`) }))}
              value={stage}
              onChange={(value) => value && setStage(value as DocumentTypeStage)}
              allowDeselect={false}
              disabled={Boolean(editing) || mutation.isPending}
              inputWrapperOrder={["label", "input", "description"]}
            />
            <Select
              id="doctype-condition"
              label={t("documents:documentTypes.columns.condition")}
              data={[
                { value: NONE, label: t("documents:enum.notSet") },
                ...DOCUMENT_REQUIREMENT_CONDITIONS.map((option) => ({ value: option, label: t(`documents:enum.condition.${option}`) })),
              ]}
              value={condition ?? NONE}
              onChange={(value) => setCondition(value === null || value === NONE ? null : (value as DocumentRequirementCondition))}
              allowDeselect={false}
              disabled={Boolean(editing) || mutation.isPending || stage !== "conditional"}
              inputWrapperOrder={["label", "input", "description"]}
            />
          </Group>
          <Group grow>
            <NumberInput
              id="doctype-renewal"
              label={t("documents:documentTypes.columns.renewalPeriodDays")}
              description={t("documents:documentTypes.renewalHint")}
              value={renewalPeriodDays}
              min={1}
              onChange={(value) => setRenewalPeriodDays(value === "" || value === undefined ? "" : String(value))}
              disabled={mutation.isPending}
              inputWrapperOrder={["label", "input", "description"]}
            />
            <NumberInput
              id="doctype-quantity"
              label={t("documents:documentTypes.columns.quantity")}
              value={quantity}
              min={1}
              onChange={(value) => setQuantity(String(value ?? 1))}
              disabled={mutation.isPending}
            />
            <NumberInput
              id="doctype-sort"
              label={t("documents:documentTypes.columns.sortOrder")}
              value={sortOrder}
              onChange={(value) => setSortOrder(String(value ?? 0))}
              disabled={mutation.isPending}
            />
          </Group>
          <NumberInput
            id="doctype-retention"
            label={t("documents:documentTypes.columns.defaultRetentionDays")}
            description={t("documents:documentTypes.retentionHint")}
            value={defaultRetentionDays}
            min={1}
            onChange={(value) => setDefaultRetentionDays(value === "" || value === undefined ? "" : String(value))}
            disabled={mutation.isPending}
            inputWrapperOrder={["label", "input", "description"]}
          />
          <Group>
            <Switch
              label={t("documents:documentTypes.columns.required")}
              checked={isRequired}
              onChange={(event) => setIsRequired(event.currentTarget.checked)}
              disabled={mutation.isPending}
            />
            <Switch
              label={t("documents:documentTypes.sensitive")}
              checked={isSensitive}
              onChange={(event) => setIsSensitive(event.currentTarget.checked)}
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
