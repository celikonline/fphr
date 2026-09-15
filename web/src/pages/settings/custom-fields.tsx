import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { ListChecks, Plus } from "lucide-react";
import {
  Badge,
  Button,
  Card,
  Checkbox,
  Group,
  Modal,
  NumberInput,
  Select,
  Stack,
  Switch,
  TagsInput,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";
import {
  CUSTOM_FIELD_SECTIONS,
  createCustomFieldDefinition,
  deactivateCustomFieldDefinition,
  isSelectDataType,
  listCustomFieldDefinitions,
  reactivateCustomFieldDefinition,
  updateCustomFieldDefinition,
  CUSTOM_FIELD_DATA_TYPES,
  type CustomFieldDataType,
  type CustomFieldDefinition,
  type CustomFieldDefinitionInput,
  type CustomFieldSection,
} from "@/services/custom-fields.service";

const DEFINITIONS_QUERY_KEY = ["employees", "custom-field-definitions"] as const;

function customFieldErrorMessage(error: unknown, t: (key: string) => string): string {
  const problem = getApiProblem(error);
  if (problem?.code === "employee.custom_field.key_taken") return t("settings:customFields.errors.keyTaken");
  return getApiErrorMessage(error, t("settings:customFields.errors.generic"));
}

export default function CustomFieldsPage() {
  const canView = usePermission("employee.customfield.view");
  if (!canView) return <NoAccess />;
  return <CustomFieldsAdmin />;
}

function CustomFieldsAdmin() {
  const { t } = useTranslation(["settings", "common"]);
  const canManage = usePermission("employee.customfield.manage");
  const queryClient = useQueryClient();

  const [includeInactive, setIncludeInactive] = useState(true);
  const definitionsQuery = useQuery({
    queryKey: [...DEFINITIONS_QUERY_KEY, includeInactive],
    queryFn: () => listCustomFieldDefinitions(includeInactive),
  });
  const definitions = useMemo(
    () => [...(definitionsQuery.data ?? [])].sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name)),
    [definitionsQuery.data],
  );

  const [dialogTarget, setDialogTarget] = useState<CustomFieldDefinition | "new" | null>(null);

  const toggleActiveMutation = useMutation({
    mutationFn: (definition: CustomFieldDefinition) =>
      definition.isActive ? deactivateCustomFieldDefinition(definition.id) : reactivateCustomFieldDefinition(definition.id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: DEFINITIONS_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("settings:customFields.statusSaved") });
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: customFieldErrorMessage(error, t) }),
  });

  const columns: ColumnDef<CustomFieldDefinition>[] = [
    {
      id: "key",
      accessorKey: "key",
      header: t("settings:customFields.columns.key"),
      meta: { label: t("settings:customFields.columns.key") },
      cell: ({ row }) => (
        <Text size="sm" fw={500} ff="monospace">
          {row.original.key}
        </Text>
      ),
    },
    {
      id: "name",
      accessorKey: "name",
      header: t("settings:customFields.columns.name"),
      meta: { label: t("settings:customFields.columns.name") },
    },
    {
      id: "dataType",
      accessorKey: "dataType",
      header: t("settings:customFields.columns.dataType"),
      meta: { label: t("settings:customFields.columns.dataType") },
      cell: ({ row }) => t(`settings:customFields.dataType.${row.original.dataType}`),
    },
    {
      id: "section",
      accessorKey: "section",
      header: t("settings:customFields.columns.section"),
      meta: { label: t("settings:customFields.columns.section") },
      cell: ({ row }) => t(`settings:customFields.section.${row.original.section}`),
    },
    {
      id: "flags",
      header: t("settings:customFields.columns.flags"),
      meta: { label: t("settings:customFields.columns.flags") },
      cell: ({ row }) => (
        <Group gap={4} wrap="wrap">
          {row.original.isRequired && <Badge size="xs" variant="light">{t("settings:customFields.flags.required")}</Badge>}
          {row.original.isEmployeeEditable && (
            <Badge size="xs" variant="light" color="teal">
              {t("settings:customFields.flags.employeeEditable")}
            </Badge>
          )}
          {row.original.employeeEditRequiresApproval && (
            <Badge size="xs" variant="light" color="orange">
              {t("settings:customFields.flags.requiresApproval")}
            </Badge>
          )}
          {row.original.isSensitive && (
            <Badge size="xs" variant="light" color="red">
              {t("settings:customFields.flags.sensitive")}
            </Badge>
          )}
          {!row.original.isReportable && (
            <Badge size="xs" variant="outline" color="gray">
              {t("settings:customFields.flags.notReportable")}
            </Badge>
          )}
        </Group>
      ),
    },
    {
      id: "sortOrder",
      accessorKey: "sortOrder",
      header: t("settings:customFields.columns.sortOrder"),
      meta: { label: t("settings:customFields.columns.sortOrder"), headerClassName: "w-24" },
    },
    {
      id: "status",
      accessorKey: "isActive",
      header: t("settings:customFields.columns.status"),
      meta: { label: t("settings:customFields.columns.status"), headerClassName: "w-32" },
      cell: ({ row }) => (
        <Badge variant={row.original.isActive ? "light" : "outline"} color={row.original.isActive ? "green" : "gray"}>
          {row.original.isActive ? t("settings:customFields.active") : t("settings:customFields.inactive")}
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
              {row.original.isActive ? t("settings:customFields.deactivate") : t("settings:customFields.reactivate")}
            </Button>
          </Group>
        ),
    },
  ];

  const table = useDataTable({ columns, data: definitions, getRowId: (d) => d.id });

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <ListChecks size={24} /> {t("settings:customFields.title")}
          </Title>
          <Text c="dimmed">{t("settings:customFields.subtitle")}</Text>
        </div>
        {canManage && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={() => setDialogTarget("new")}>
            {t("settings:customFields.add")}
          </Button>
        )}
      </Group>

      <Card withBorder padding="lg">
        <Group justify="space-between" mb="md">
          <Switch
            label={t("settings:customFields.showInactive")}
            checked={includeInactive}
            onChange={(e) => setIncludeInactive(e.currentTarget.checked)}
          />
        </Group>
        {definitionsQuery.isError ? (
          <Text size="sm" c="red" ta="center" p="lg">
            {t("settings:customFields.errors.loadFailed")}
          </Text>
        ) : (
          <DataTable table={table} isLoading={definitionsQuery.isLoading} emptyMessage={t("settings:customFields.empty")} />
        )}
      </Card>

      {dialogTarget && <DefinitionDialog target={dialogTarget} onClose={() => setDialogTarget(null)} />}
    </div>
  );
}

interface DefinitionFormState {
  key: string;
  name: string;
  dataType: CustomFieldDataType;
  section: CustomFieldSection;
  options: string[];
  isRequired: boolean;
  isEmployeeEditable: boolean;
  employeeEditRequiresApproval: boolean;
  isReportable: boolean;
  isSensitive: boolean;
  sortOrder: string;
}

function stateFrom(definition: CustomFieldDefinition | null): DefinitionFormState {
  return {
    key: definition?.key ?? "",
    name: definition?.name ?? "",
    dataType: definition?.dataType ?? "text",
    section: definition?.section ?? "other",
    options: definition?.options ?? [],
    isRequired: definition?.isRequired ?? false,
    isEmployeeEditable: definition?.isEmployeeEditable ?? false,
    employeeEditRequiresApproval: definition?.employeeEditRequiresApproval ?? false,
    isReportable: definition?.isReportable ?? true,
    isSensitive: definition?.isSensitive ?? false,
    sortOrder: String(definition?.sortOrder ?? 0),
  };
}

function DefinitionDialog({ target, onClose }: { target: CustomFieldDefinition | "new"; onClose: () => void }) {
  const { t } = useTranslation(["settings", "common"]);
  const queryClient = useQueryClient();
  const editing = target !== "new";
  const [values, setValues] = useState<DefinitionFormState>(() => stateFrom(editing ? target : null));
  const [submitted, setSubmitted] = useState(false);

  const mutation = useMutation({
    mutationFn: () => {
      const input: CustomFieldDefinitionInput = {
        key: values.key,
        name: values.name.trim(),
        dataType: values.dataType,
        section: values.section,
        options: isSelectDataType(values.dataType) ? values.options : null,
        isRequired: values.isRequired,
        isEmployeeEditable: values.isEmployeeEditable,
        employeeEditRequiresApproval: values.employeeEditRequiresApproval,
        isReportable: values.isReportable,
        isSensitive: values.isSensitive,
        sortOrder: Number(values.sortOrder) || 0,
      };
      return editing ? updateCustomFieldDefinition(target.id, input) : createCustomFieldDefinition(input);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: DEFINITIONS_QUERY_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("settings:customFields.saved") });
      onClose();
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: customFieldErrorMessage(error, t) }),
  });

  const keyMissing = !editing && !values.key.trim();
  const keyInvalid = !editing && values.key.trim().length > 0 && !/^[a-z][a-z0-9_]*$/.test(values.key.trim());
  const nameMissing = !values.name.trim();
  const optionsMissing = isSelectDataType(values.dataType) && values.options.length === 0;

  return (
    <Modal
      opened
      onClose={() => !mutation.isPending && onClose()}
      title={editing ? t("settings:customFields.editTitle") : t("settings:customFields.addTitle")}
      size="lg"
    >
      <form
        onSubmit={(e) => {
          e.preventDefault();
          setSubmitted(true);
          if (!keyMissing && !keyInvalid && !nameMissing && !optionsMissing) mutation.mutate();
        }}
      >
        <Stack gap="md">
          <TextInput
            id="cf-key"
            data-autofocus={!editing}
            label={t("settings:customFields.fields.key")}
            description={editing ? t("settings:customFields.fields.keyImmutable") : t("settings:customFields.fields.keyHint")}
            placeholder="ornek_alan"
            value={values.key}
            disabled={editing}
            onChange={(e) => setValues((prev) => ({ ...prev, key: e.currentTarget.value.trim().toLowerCase() }))}
            error={submitted && (keyMissing ? t("common:required") : keyInvalid ? t("settings:customFields.fields.keyInvalid") : undefined)}
          />
          <TextInput
            id="cf-name"
            label={t("settings:customFields.fields.name")}
            maxLength={150}
            value={values.name}
            onChange={(e) => setValues((prev) => ({ ...prev, name: e.currentTarget.value }))}
            error={submitted && nameMissing ? t("common:required") : undefined}
          />
          <Group grow>
            <Select
              label={t("settings:customFields.fields.dataType")}
              description={editing ? t("settings:customFields.fields.dataTypeImmutable") : undefined}
              data={CUSTOM_FIELD_DATA_TYPES.map((dt) => ({ value: dt, label: t(`settings:customFields.dataType.${dt}`) }))}
              value={values.dataType}
              disabled={editing}
              allowDeselect={false}
              onChange={(v) => v && setValues((prev) => ({ ...prev, dataType: v as CustomFieldDataType }))}
            />
            <Select
              label={t("settings:customFields.fields.section")}
              data={CUSTOM_FIELD_SECTIONS.map((s) => ({ value: s, label: t(`settings:customFields.section.${s}`) }))}
              value={values.section}
              allowDeselect={false}
              onChange={(v) => v && setValues((prev) => ({ ...prev, section: v as CustomFieldSection }))}
            />
          </Group>
          {isSelectDataType(values.dataType) && (
            <TagsInput
              label={t("settings:customFields.fields.options")}
              description={t("settings:customFields.fields.optionsHint")}
              placeholder={t("settings:customFields.fields.optionsPlaceholder")}
              value={values.options}
              onChange={(options) => setValues((prev) => ({ ...prev, options }))}
              error={submitted && optionsMissing ? t("settings:customFields.fields.optionsRequired") : undefined}
            />
          )}
          <NumberInput
            label={t("settings:customFields.fields.sortOrder")}
            value={values.sortOrder}
            onChange={(v) => setValues((prev) => ({ ...prev, sortOrder: String(v) }))}
          />
          <Stack gap="xs">
            <Checkbox
              label={t("settings:customFields.fields.isRequired")}
              checked={values.isRequired}
              onChange={(e) => setValues((prev) => ({ ...prev, isRequired: e.currentTarget.checked }))}
            />
            <Checkbox
              label={t("settings:customFields.fields.isEmployeeEditable")}
              description={t("settings:customFields.fields.isEmployeeEditableHint")}
              checked={values.isEmployeeEditable}
              onChange={(e) => setValues((prev) => ({ ...prev, isEmployeeEditable: e.currentTarget.checked }))}
            />
            <Checkbox
              label={t("settings:customFields.fields.employeeEditRequiresApproval")}
              description={t("settings:customFields.fields.employeeEditRequiresApprovalHint")}
              checked={values.employeeEditRequiresApproval}
              disabled={!values.isEmployeeEditable}
              onChange={(e) => setValues((prev) => ({ ...prev, employeeEditRequiresApproval: e.currentTarget.checked }))}
            />
            <Checkbox
              label={t("settings:customFields.fields.isSensitive")}
              description={t("settings:customFields.fields.isSensitiveHint")}
              checked={values.isSensitive}
              onChange={(e) => setValues((prev) => ({ ...prev, isSensitive: e.currentTarget.checked }))}
            />
            <Checkbox
              label={t("settings:customFields.fields.isReportable")}
              checked={values.isReportable}
              onChange={(e) => setValues((prev) => ({ ...prev, isReportable: e.currentTarget.checked }))}
            />
          </Stack>
          <Group justify="flex-end" gap="sm">
            <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
