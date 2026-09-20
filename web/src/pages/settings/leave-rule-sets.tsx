import { useEffect, useMemo, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import {
  Alert,
  Badge,
  Button,
  Card,
  Checkbox,
  CloseButton,
  Group,
  List,
  NavLink,
  Paper,
  ScrollArea,
  Skeleton,
  Stack,
  Text,
  TextInput,
  Title,
  Tooltip,
} from "@mantine/core";
import { AlertTriangle, Copy, Pencil, Plus, ScrollText, Search, Star, Trash2, UserPlus } from "lucide-react";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
import { FormModal } from "@/components/ui/form-modal";
import ConfirmDialog from "@/components/confirm-dialog";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { useDays, useLeaveName } from "@/components/leave/leave-shared";
import { LeaveTypeDialog } from "@/components/leave/leave-type-dialog";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { listEmployees } from "@/services/employees.service";
import {
  assignLeaveRuleSet,
  cloneLeaveRuleSet,
  deleteLeaveRuleSet,
  listLeaveRuleSets,
  renameLeaveRuleSet,
  setDefaultLeaveRuleSet,
  setLeaveTypeDisabled,
  type LeaveRuleSet,
  type LeaveRuleSetAssignmentResult,
  type LeaveType,
  type LeaveTypeSettings,
} from "@/services/leave.service";

const RULE_SETS_KEY = ["leave", "rule-sets"] as const;
const NAME_MAX_LENGTH = 200;

/**
 * İzinler → Kurallar: rule sets (default first) and the selected set's leave types. Per set: "Kopyala ve düzenle",
 * "Yeniden adlandır", "Varsayılan yap", "Sil" (leave.rule_sets.manage) and "Çalışanlara ata" (leave.rule_sets.assign).
 */
export default function LeaveRuleSetsPage() {
  const canRead = usePermission("leave.rule_sets.read");
  if (!canRead) return <NoAccess />;
  return <LeaveRuleSets />;
}

function LeaveRuleSets() {
  const { t } = useTranslation(["leave", "common"]);
  const leaveName = useLeaveName();
  const canManage = usePermission("leave.rule_sets.manage");
  const canAssign = usePermission("leave.rule_sets.assign");
  const canManageTypes = usePermission("leave.types.manage");
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: RULE_SETS_KEY, queryFn: listLeaveRuleSets });
  const sets = [...(query.data ?? [])].sort((a, b) => Number(b.isDefault) - Number(a.isDefault) || leaveName(a.name).localeCompare(leaveName(b.name)));
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selected = sets.find((set) => set.id === selectedId) ?? sets[0];
  const [cloning, setCloning] = useState<LeaveRuleSet | null>(null);
  const [renaming, setRenaming] = useState<LeaveRuleSet | null>(null);
  const [assigning, setAssigning] = useState<LeaveRuleSet | null>(null);
  const [deleting, setDeleting] = useState<LeaveRuleSet | null>(null);
  const [typeDialog, setTypeDialog] = useState<"new" | LeaveType | null>(null);

  const onError = (error: unknown) =>
    toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("leave:genericError")) });

  const setDefaultMutation = useMutation({
    mutationFn: (set: LeaveRuleSet) => setDefaultLeaveRuleSet(set.id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: RULE_SETS_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("leave:ruleSets.defaultSet") });
    },
    onError,
  });

  const deleteMutation = useMutation({
    mutationFn: (set: LeaveRuleSet) => deleteLeaveRuleSet(set.id),
    onSuccess: async () => {
      setDeleting(null);
      setSelectedId(null);
      await queryClient.invalidateQueries({ queryKey: RULE_SETS_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("leave:ruleSets.deleted") });
    },
    onError: (error) => {
      setDeleting(null);
      onError(error);
    },
  });

  const deleteBlockedReason = selected
    ? selected.isDefault
      ? t("leave:ruleSets.deleteBlockedDefault")
      : selected.assignedEmployeeCount > 0
        ? t("leave:ruleSets.deleteBlockedEmployees", { count: selected.assignedEmployeeCount })
        : null
    : null;

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <ScrollText size={24} /> {t("leave:ruleSets.title")}
        </Title>
        <Text c="dimmed">{t("leave:ruleSets.subtitle")}</Text>
      </div>

      {query.isLoading ? (
        <div className="grid gap-6 lg:grid-cols-[18rem_1fr]">
          <Skeleton h={256} />
          <Skeleton h={256} />
        </div>
      ) : query.isError ? (
        <Text size="sm" c="red">
          {getApiErrorMessage(query.error, t("leave:genericError"))}
        </Text>
      ) : sets.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("leave:ruleSets.empty")}
        </Text>
      ) : (
        <div className="grid gap-6 lg:grid-cols-[18rem_1fr]">
          <Card withBorder padding="xs" className="h-fit">
            <Stack gap={4}>
              {sets.map((set) => {
                const inactive = set.types.filter((type) => type.settings.isDisabled).length;
                return (
                  <NavLink
                    key={set.id}
                    component="button"
                    type="button"
                    active={selected?.id === set.id}
                    variant="light"
                    onClick={() => setSelectedId(set.id)}
                    style={{
                      borderRadius: "var(--mantine-radius-md)",
                      borderInlineStart: `4px solid ${set.isDefault ? "var(--mantine-primary-color-filled)" : "transparent"}`,
                    }}
                    label={
                      <span className="flex flex-wrap items-center gap-1.5">
                        <Text span size="sm" fw={500}>
                          {leaveName(set.name)}
                        </Text>
                        {set.isDefault && <Badge size="sm">{t("leave:ruleSets.default")}</Badge>}
                        {set.isSystemSeed && (
                          <Badge size="sm" variant="outline" color="gray">
                            {t("leave:ruleSets.systemSeed")}
                          </Badge>
                        )}
                      </span>
                    }
                    description={`${t("leave:ruleSets.typeCount", { active: set.types.length - inactive, inactive })} · ${t("leave:ruleSets.assignedCount", { count: set.assignedEmployeeCount })}`}
                  />
                );
              })}
            </Stack>
          </Card>

          {selected && (
            <Card withBorder padding="lg">
              <Group justify="space-between" gap="xs" mb="md">
                <Title order={4}>
                  {leaveName(selected.name)} — {t("leave:ruleSets.typesTitle")}
                </Title>
                {(canManage || canAssign || canManageTypes) && (
                  <Group gap="xs">
                    {canManageTypes && (
                      <Button type="button" size="sm" leftSection={<Plus size={16} />} onClick={() => setTypeDialog("new")}>
                        {t("leave:types.addTitle")}
                      </Button>
                    )}
                    {canAssign && (
                      <Button type="button" size="sm" variant="default" leftSection={<UserPlus size={16} />} onClick={() => setAssigning(selected)}>
                        {t("leave:ruleSets.assign")}
                      </Button>
                    )}
                    {canManage && (
                      <>
                        <Button type="button" size="sm" variant="default" leftSection={<Pencil size={16} />} onClick={() => setRenaming(selected)}>
                          {t("leave:ruleSets.rename")}
                        </Button>
                        {!selected.isDefault && (
                          <Button
                            type="button"
                            size="sm"
                            variant="default"
                            leftSection={<Star size={16} />}
                            disabled={setDefaultMutation.isPending}
                            onClick={() => setDefaultMutation.mutate(selected)}
                          >
                            {t("leave:ruleSets.makeDefault")}
                          </Button>
                        )}
                        <Button type="button" size="sm" variant="default" leftSection={<Copy size={16} />} onClick={() => setCloning(selected)}>
                          {t("leave:ruleSets.clone")}
                        </Button>
                        {deleteBlockedReason ? (
                          <Tooltip label={deleteBlockedReason} multiline maw={280}>
                            {/* Disabled buttons swallow pointer events; the wrapper carries the tooltip. */}
                            <span tabIndex={0} aria-label={deleteBlockedReason}>
                              <Button type="button" size="sm" variant="default" leftSection={<Trash2 size={16} />} disabled style={{ pointerEvents: "none" }}>
                                {t("common:delete")}
                              </Button>
                            </span>
                          </Tooltip>
                        ) : (
                          <Button
                            type="button"
                            size="sm"
                            variant="default"
                            c="red"
                            leftSection={<Trash2 size={16} />}
                            onClick={() => setDeleting(selected)}
                          >
                            {t("common:delete")}
                          </Button>
                        )}
                      </>
                    )}
                  </Group>
                )}
              </Group>
              <div className="overflow-x-auto">
                {selected.types.length === 0 ? (
                  <Text size="sm" c="dimmed">
                    {t("leave:ruleSets.noTypes")}
                  </Text>
                ) : (
                  <LeaveTypesTable set={selected} canManageTypes={canManageTypes} onEdit={setTypeDialog} />
                )}
              </div>
            </Card>
          )}
        </div>
      )}

      {cloning && (
        <CloneRuleSetDialog
          source={cloning}
          onClose={() => setCloning(null)}
          onCloned={(id) => {
            setCloning(null);
            setSelectedId(id);
          }}
        />
      )}

      {renaming && <RenameRuleSetDialog set={renaming} onClose={() => setRenaming(null)} />}

      {assigning && <AssignRuleSetDialog set={assigning} onClose={() => setAssigning(null)} />}

      {typeDialog && selected && (
        <LeaveTypeDialog
          ruleSet={selected}
          editing={typeDialog === "new" ? null : typeDialog}
          onClose={() => setTypeDialog(null)}
        />
      )}

      <ConfirmDialog
        open={Boolean(deleting)}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={t("leave:ruleSets.deleteTitle")}
        description={t("leave:ruleSets.deleteDescription", { name: deleting ? leaveName(deleting.name) : "" })}
        isLoading={deleteMutation.isPending}
        onConfirm={() => deleting && deleteMutation.mutate(deleting)}
      />
    </div>
  );
}

function RenameRuleSetDialog({ set, onClose }: { set: LeaveRuleSet; onClose: () => void }) {
  const { t } = useTranslation(["leave", "common"]);
  const leaveName = useLeaveName();
  const queryClient = useQueryClient();
  const [name, setName] = useState(leaveName(set.name).slice(0, NAME_MAX_LENGTH));
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (value: string) => renameLeaveRuleSet(set.id, value),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["leave"] });
      toast({ variant: "success", title: t("common:success"), description: t("leave:ruleSets.renamed") });
      onClose();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("leave:genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return setError(t("leave:ruleSets.nameRequired"));
    setError(null);
    mutation.mutate(name.trim());
  }

  return (
    <FormModal opened onClose={onClose} size="28rem" title={t("leave:ruleSets.renameTitle")} closeOnClickOutside={false}>
      <form onSubmit={submit}>
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("leave:ruleSets.renameDescription", { name: leaveName(set.name) })}
          </Text>
          <TextInput
            id="rule-set-rename"
            label={t("leave:ruleSets.name")}
            withAsterisk
            value={name}
            maxLength={NAME_MAX_LENGTH}
            disabled={mutation.isPending}
            onChange={(e) => setName(e.currentTarget.value)}
          />
          {error && (
            <Text size="sm" c="red" role="alert">
              {error}
            </Text>
          )}
          <Group justify="flex-end" gap="xs">
            <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" loading={mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        </Stack>
      </form>
    </FormModal>
  );
}

/** "Çalışanlara ata": searchable employee multi-select; the result lists the employees that could not be moved. */
function AssignRuleSetDialog({ set, onClose }: { set: LeaveRuleSet; onClose: () => void }) {
  const { t } = useTranslation(["leave", "common", "employees"]);
  const leaveName = useLeaveName();
  const queryClient = useQueryClient();
  const [search, setSearch] = useState("");
  const [debounced, setDebounced] = useState("");
  const [selected, setSelected] = useState<Map<string, string>>(new Map());
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<LeaveRuleSetAssignmentResult | null>(null);

  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(search.trim()), 250);
    return () => window.clearTimeout(timer);
  }, [search]);

  const employeesQuery = useQuery({
    queryKey: ["employees", "rule-set-assign", debounced],
    queryFn: () => listEmployees({ page: 1, pageSize: 20, q: debounced || undefined, status: "Active", sort: "displayName" }),
    enabled: result === null,
    retry: false,
  });

  const mutation = useMutation({
    mutationFn: (employeeIds: string[]) => assignLeaveRuleSet(set.id, employeeIds),
    onSuccess: async (data) => {
      setResult(data);
      await queryClient.invalidateQueries({ queryKey: ["leave"] });
    },
    onError: (err) => setError(getApiErrorMessage(err, t("leave:genericError"))),
  });

  const toggle = (id: string, name: string, checked: boolean) =>
    setSelected((current) => {
      const next = new Map(current);
      if (checked) next.set(id, name);
      else next.delete(id);
      return next;
    });

  const chips = useMemo(() => [...selected.entries()], [selected]);
  const busy = mutation.isPending;

  function submit(event: FormEvent) {
    event.preventDefault();
    if (selected.size === 0) return setError(t("leave:ruleSets.assignNoSelection"));
    setError(null);
    mutation.mutate([...selected.keys()]);
  }

  return (
    <FormModal opened onClose={onClose} size="32rem" title={t("leave:ruleSets.assignTitle")} closeOnClickOutside={false}>
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          {t("leave:ruleSets.assignDescription", { name: leaveName(set.name) })}
        </Text>
        {result ? (
          <Stack gap="md">
            <Text size="sm" fw={500}>
              {t("leave:ruleSets.assignedResult", { count: result.assignedCount })}
            </Text>
            {result.warnings.length > 0 && (
              <Alert
                color="yellow"
                variant="light"
                icon={<AlertTriangle size={16} />}
                title={t("leave:ruleSets.assignWarnings", { count: result.warnings.length })}
              >
                <List size="sm" spacing={4}>
                  {result.warnings.map((warning) => (
                    <List.Item key={`${warning.employeeId}-${warning.code}`}>
                      <Text span inherit fw={500}>
                        {selected.get(warning.employeeId) ?? warning.employeeId}
                      </Text>
                      : {warning.message}
                    </List.Item>
                  ))}
                </List>
              </Alert>
            )}
            <Group justify="flex-end">
              <Button type="button" onClick={onClose}>
                {t("common:close")}
              </Button>
            </Group>
          </Stack>
        ) : (
          <form onSubmit={submit}>
            <Stack gap="md">
              {chips.length > 0 && (
                <Group gap={6}>
                  {chips.map(([id, name]) => (
                    <Badge
                      key={id}
                      variant="light"
                      tt="none"
                      pr={3}
                      rightSection={
                        <CloseButton
                          size="xs"
                          radius="xl"
                          variant="transparent"
                          aria-label={t("leave:ruleSets.assignRemove", { name })}
                          onClick={() => toggle(id, name, false)}
                        />
                      }
                    >
                      {name}
                    </Badge>
                  ))}
                </Group>
              )}
              <TextInput
                leftSection={<Search size={16} />}
                placeholder={t("employees:picker.search")}
                aria-label={t("employees:picker.search")}
                value={search}
                disabled={busy}
                onChange={(e) => setSearch(e.currentTarget.value)}
              />
              <Paper withBorder>
                <ScrollArea.Autosize mah={288}>
                  {employeesQuery.isLoading ? (
                    <Skeleton h={96} m="xs" w="auto" />
                  ) : employeesQuery.isError ? (
                    <Text size="sm" c="red" p="sm">
                      {getApiErrorMessage(employeesQuery.error, t("leave:genericError"))}
                    </Text>
                  ) : (employeesQuery.data?.items ?? []).length === 0 ? (
                    <Text size="sm" c="dimmed" p="sm">
                      {t("employees:picker.empty")}
                    </Text>
                  ) : (
                    <Stack gap={0} py={4}>
                      {(employeesQuery.data?.items ?? []).map((employee) => (
                        <Checkbox
                          key={employee.id}
                          id={`assign-${employee.id}`}
                          px="sm"
                          py={8}
                          checked={selected.has(employee.id)}
                          disabled={busy}
                          onChange={(e) => toggle(employee.id, employee.displayName, e.currentTarget.checked)}
                          styles={{ body: { alignItems: "center" }, labelWrapper: { minWidth: 0, flex: 1 } }}
                          label={
                            <>
                              <Text span size="sm" display="block" truncate="end">
                                {employee.displayName}
                              </Text>
                              {employee.workEmail && (
                                <Text span size="xs" c="dimmed" display="block" truncate="end">
                                  {employee.workEmail}
                                </Text>
                              )}
                            </>
                          }
                        />
                      ))}
                    </Stack>
                  )}
                </ScrollArea.Autosize>
              </Paper>
              {error && (
                <Text size="sm" c="red" role="alert">
                  {error}
                </Text>
              )}
              <Group justify="flex-end" gap="xs">
                <Button type="button" variant="default" onClick={onClose} disabled={busy}>
                  {t("common:cancel")}
                </Button>
                <Button type="submit" loading={busy} disabled={selected.size === 0}>
                  {busy ? t("common:saving") : t("leave:ruleSets.assignSubmit", { count: selected.size })}
                </Button>
              </Group>
            </Stack>
          </form>
        )}
      </Stack>
    </FormModal>
  );
}

function limitText(settings: LeaveTypeSettings, t: (key: string) => string, days: (value: number | null | undefined) => string) {
  const amount = settings.limitType === "perYear" ? settings.annualDays : settings.limitType === "perRequest" ? settings.perRequestMaxDays : null;
  const label = t(`leave:limitType.${settings.limitType}`);
  return amount === null || amount === undefined ? label : `${label} (${days(amount)} ${t("leave:days")})`;
}

type LeaveTypeRow = LeaveRuleSet["types"][number];

export function LeaveTypesTable({
  set,
  canManageTypes,
  onEdit,
}: {
  set: LeaveRuleSet;
  canManageTypes: boolean;
  onEdit: (type: LeaveType) => void;
}) {
  const { t } = useTranslation(["leave", "common"]);
  const leaveName = useLeaveName();
  const days = useDays();
  const queryClient = useQueryClient();
  const [disabling, setDisabling] = useState<LeaveTypeRow | null>(null);

  const toggleDisabledMutation = useMutation({
    mutationFn: (row: LeaveTypeRow) => setLeaveTypeDisabled(set.id, row.id, !row.settings.isDisabled),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: RULE_SETS_KEY }),
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("leave:genericError")) }),
    onSettled: () => setDisabling(null),
  });

  /** Enabling applies right away; disabling affects every assigned employee, so it asks first (B9). */
  function requestToggle(row: LeaveTypeRow) {
    if (row.settings.isDisabled) toggleDisabledMutation.mutate(row);
    else setDisabling(row);
  }

  // Disabled types render muted; DataTable rows take no class, so every cell carries it.
  const text = (row: LeaveTypeRow, value: string) => (
    <Text span inherit c={row.settings.isDisabled ? "dimmed" : undefined}>
      {value}
    </Text>
  );
  const accrualText = ({ settings }: LeaveTypeRow) =>
    settings.limitType === "perYear" ? t(`leave:accrualFrequency.${settings.accrualFrequency}`) : "—";
  const carryOverText = ({ settings }: LeaveTypeRow) =>
    settings.limitType === "perYear"
      ? settings.carryOverMode === "limited" && settings.carryOverMaxDays != null
        ? `${t("leave:carryOverMode.limited")} (${days(settings.carryOverMaxDays)} ${t("leave:days")})`
        : t(`leave:carryOverMode.${settings.carryOverMode}`)
      : "—";

  /** A sortable column whose cell is the (muted when disabled) text of `value`. */
  const textColumn = (id: string, label: string, value: (row: LeaveTypeRow) => string): ColumnDef<LeaveTypeRow> => ({
    id,
    accessorFn: value,
    header: label,
    meta: { label },
    cell: ({ row }) => text(row.original, value(row.original)),
  });

  const columns: ColumnDef<LeaveTypeRow>[] = [
    {
      id: "name",
      accessorFn: (row) => leaveName(row.settings.name),
      enableHiding: false,
      header: t("leave:ruleSets.col.name"),
      meta: { label: t("leave:ruleSets.col.name") },
      cell: ({ row }) => (
        <div>
          <Text span inherit fw={500} c={row.original.settings.isDisabled ? "dimmed" : undefined}>
            {leaveName(row.original.settings.name)}
          </Text>
          {row.original.settings.description && (
            <Text size="xs" c="dimmed" mt={2}>
              {row.original.settings.description}
            </Text>
          )}
        </div>
      ),
    },
    textColumn("code", t("leave:ruleSets.col.code"), (row) => t(`leave:code.${row.settings.code}`)),
    textColumn("paid", t("leave:ruleSets.col.paid"), (row) => (row.settings.isPaid ? t("leave:ruleSets.paid") : t("leave:ruleSets.unpaid"))),
    textColumn("limit", t("leave:ruleSets.col.limit"), (row) => limitText(row.settings, t, days)),
    textColumn("accrual", t("leave:ruleSets.col.accrual"), accrualText),
    textColumn("carryOver", t("leave:ruleSets.col.carryOver"), carryOverText),
    textColumn("unit", t("leave:ruleSets.col.unit"), (row) => t(`leave:unit.${row.settings.unit}`)),
    textColumn("calendar", t("leave:ruleSets.col.calendar"), (row) =>
      row.settings.showTypeNameOnCalendar ? t("leave:ruleSets.visible") : t("leave:ruleSets.hidden"),
    ),
    {
      id: "status",
      accessorFn: (row) => (row.settings.isDisabled ? t("leave:ruleSets.disabled") : t("leave:ruleSets.active")),
      header: t("leave:ruleSets.col.status"),
      meta: { label: t("leave:ruleSets.col.status") },
      cell: ({ row }) => (
        <Badge variant={row.original.settings.isDisabled ? "outline" : "filled"} color={row.original.settings.isDisabled ? "gray" : "green"}>
          {row.original.settings.isDisabled ? t("leave:ruleSets.disabled") : t("leave:ruleSets.active")}
        </Badge>
      ),
    },
    ...(canManageTypes
      ? [
          {
            id: "actions",
            header: "",
            meta: { label: t("common:actions"), headerClassName: "w-40" },
            enableSorting: false,
            cell: ({ row }: { row: { original: LeaveTypeRow } }) => (
              <Group gap="xs" justify="flex-end" wrap="nowrap">
                <Button type="button" size="xs" variant="subtle" onClick={() => onEdit(row.original)}>
                  {t("common:edit")}
                </Button>
                <Button
                  type="button"
                  size="xs"
                  variant="subtle"
                  color={row.original.settings.isDisabled ? "teal" : "red"}
                  loading={toggleDisabledMutation.isPending && toggleDisabledMutation.variables?.id === row.original.id}
                  onClick={() => requestToggle(row.original)}
                >
                  {row.original.settings.isDisabled ? t("leave:ruleSets.enable") : t("leave:ruleSets.disable")}
                </Button>
              </Group>
            ),
          } satisfies ColumnDef<LeaveTypeRow>,
        ]
      : []),
  ];

  const table = useDataTable({
    columns,
    data: set.types,
    getRowId: (row) => row.id,
    columnVisibilityKey: "senseik.leave.ruleSetTypes.columns",
  });

  return (
    <Stack gap="xs">
      <Group justify="flex-end">
        <DataTableViewOptions table={table} className="h-9 px-3" />
      </Group>
      <DataTable table={table} />
      <ConfirmDialog
        open={Boolean(disabling)}
        onOpenChange={(open) => !open && setDisabling(null)}
        title={t("leave:ruleSets.disableTitle")}
        description={t("leave:ruleSets.disableDescription", {
          type: disabling ? leaveName(disabling.settings.name) : "",
          count: set.assignedEmployeeCount,
        })}
        confirmLabel={t("leave:ruleSets.disable")}
        isLoading={toggleDisabledMutation.isPending}
        onConfirm={() => disabling && toggleDisabledMutation.mutate(disabling)}
      />
    </Stack>
  );
}

function CloneRuleSetDialog({ source, onClose, onCloned }: { source: LeaveRuleSet; onClose: () => void; onCloned: (id: string) => void }) {
  const { t } = useTranslation(["leave", "common"]);
  const leaveName = useLeaveName();
  const queryClient = useQueryClient();
  const [name, setName] = useState(`${leaveName(source.name)} ${t("leave:ruleSets.copySuffix")}`.slice(0, NAME_MAX_LENGTH));
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (value: string) => cloneLeaveRuleSet(source.id, value),
    onSuccess: async (id) => {
      await queryClient.invalidateQueries({ queryKey: RULE_SETS_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("leave:ruleSets.cloned") });
      onCloned(id);
    },
    onError: (err) => setError(getApiErrorMessage(err, t("leave:genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return setError(t("leave:ruleSets.nameRequired"));
    setError(null);
    mutation.mutate(name.trim());
  }

  return (
    <FormModal opened onClose={onClose} size="28rem" title={t("leave:ruleSets.cloneTitle")} closeOnClickOutside={false}>
      <form onSubmit={submit}>
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t("leave:ruleSets.cloneDescription", { name: leaveName(source.name) })}
          </Text>
          <TextInput
            id="rule-set-name"
            label={t("leave:ruleSets.name")}
            withAsterisk
            value={name}
            maxLength={NAME_MAX_LENGTH}
            disabled={mutation.isPending}
            onChange={(e) => setName(e.currentTarget.value)}
          />
          {error && (
            <Text size="sm" c="red" role="alert">
              {error}
            </Text>
          )}
          <Group justify="flex-end" gap="xs">
            <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" loading={mutation.isPending}>
              {mutation.isPending ? t("common:saving") : t("leave:ruleSets.clone")}
            </Button>
          </Group>
        </Stack>
      </form>
    </FormModal>
  );
}
