import { useCallback, useMemo, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Anchor, Badge, Button, Card, Divider, Group, Modal, Progress, Select, Skeleton, Stack, Text, Title, type BadgeProps } from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { CalendarRange, ListOrdered } from "lucide-react";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
import { formatDateOnly, formatDateTime } from "@/lib/dates";
import {
  getEmployeeLeaveLedger,
  getMyLeaveLedger,
  type EmployeeLeaveBalances,
  type LeaveBalanceItem,
  type LeaveLedgerEntry,
  type LeaveRequest,
  type LeaveRequestStatus,
  type LeaveRuleSet,
} from "@/services/leave.service";

const ALL_TYPES = "__all__";
const PAGE_SIZE = 20;
/** Narrow phones (S-6 mobil kalıbı, `missing-documents-report.tsx`): a card list reads better than a horizontally-scrolled table. */
const MOBILE_QUERY = "(max-width: 390px)";
const REQUESTS_COLUMNS_KEY = "senseik.leave.requests.columns";
const LEDGER_COLUMNS_KEY = "senseik.leave.ledger.columns";
/** Shared empty fallback: a fresh `[]` per render would restart the ledger table's page reset every render. */
const NO_LEDGER_ENTRIES: LeaveLedgerEntry[] = [];
/** Column chooser trigger sized like the surrounding `size="sm"` buttons. */
const COLUMN_CHOOSER_CLASS = "h-9 px-3";

/**
 * Seeded rule sets and types store a resource key as their name ("leave.type.annual"); the API returns it as is, so
 * the web localizes it from the `leave:names` map (dots → underscores). Free names from tenants pass through unchanged.
 */
export function useLeaveName() {
  const { t } = useTranslation(["leave"]);
  return useCallback(
    (name: string) => (name.startsWith("leave.") ? t(`leave:names.${name.slice(6).split(".").join("_")}`, { defaultValue: name }) : name),
    [t],
  );
}

export function useDays() {
  const { i18n } = useTranslation();
  return useCallback(
    (value: number | null | undefined) =>
      value === null || value === undefined ? "—" : new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 2 }).format(value),
    [i18n.language],
  );
}

/** Leave type id → display name, from the balances (PerYear types) and, when readable, the employee's rule set (all types). */
export function useLeaveTypeNames(balances: EmployeeLeaveBalances | undefined, ruleSet: LeaveRuleSet | undefined) {
  const leaveName = useLeaveName();
  const { t } = useTranslation(["leave"]);
  const names = useMemo(() => {
    const map = new Map<string, string>();
    ruleSet?.types.forEach((type) => map.set(type.id, leaveName(type.settings.name)));
    balances?.balances.forEach((item) => map.set(item.balance.leaveTypeId, leaveName(item.leaveTypeName)));
    return map;
  }, [balances, ruleSet, leaveName]);
  return useCallback(
    (request: Pick<LeaveRequest, "leaveTypeId" | "leaveTypeCode" | "leaveTypeName">) =>
      request.leaveTypeName ?? names.get(request.leaveTypeId) ?? t(`leave:code.${request.leaveTypeCode}`),
    [names, t],
  );
}

const STATUS_BADGE: Record<LeaveRequestStatus, Pick<BadgeProps, "variant" | "color">> = {
  pending: { variant: "light" },
  approved: { variant: "filled", color: "green" },
  rejected: { variant: "filled", color: "red" },
  withdrawn: { variant: "outline", color: "gray" },
  cancelled: { variant: "outline", color: "gray" },
};

export function LeaveStatusBadge({ status }: { status: LeaveRequestStatus }) {
  const { t } = useTranslation(["leave"]);
  return <Badge {...STATUS_BADGE[status]}>{t(`leave:status.${status}`)}</Badge>;
}

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <Text component="dt" size="xs" c="dimmed">
        {label}
      </Text>
      <Text component="dd" size="sm" mt={2}>
        {children}
      </Text>
    </div>
  );
}

/** Used share of what the employee has been entitled to so far (used + current balance); hidden when nothing was entitled. */
function BalanceUsage({ used, currentBalance }: { used: number; currentBalance: number }) {
  const { t } = useTranslation(["leave"]);
  const days = useDays();
  const entitled = used + currentBalance;
  if (entitled <= 0 && used <= 0) return null;
  const percent = entitled <= 0 ? 100 : Math.min(100, (used / entitled) * 100);
  const label = `${t("leave:summary.totalUsed")}: ${days(used)} / ${days(entitled)} ${t("leave:days")}`;
  return (
    <div>
      <Progress value={percent} color={currentBalance < 0 ? "red" : undefined} size="sm" aria-label={label} />
      <Text size="xs" c="dimmed" mt={4}>
        {label}
      </Text>
    </div>
  );
}

/** "Özet": the official İzin Özeti fields per PerYear type. */
export function LeaveBalanceSummary({
  data,
  isLoading,
  emptyMessage,
  action,
  ruleSet,
}: {
  data: EmployeeLeaveBalances | undefined;
  isLoading: boolean;
  emptyMessage?: string;
  action?: ReactNode;
  /** Rule set field (name plus an optional change action), shown next to the hire dates. */
  ruleSet?: ReactNode;
}) {
  const { t, i18n } = useTranslation(["leave"]);
  const leaveName = useLeaveName();
  const days = useDays();
  const date = (value: string) => formatDateOnly(value, i18n.language);

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" gap="sm" mb="md">
        <Group gap="xs">
          <CalendarRange size={16} color="var(--mantine-color-dimmed)" />
          <Title order={4}>{t("leave:summary.title")}</Title>
        </Group>
        {action}
      </Group>
      <Stack gap="lg">
        {isLoading ? (
          <Skeleton h={128} />
        ) : !data ? (
          <Text size="sm" c="dimmed">
            {emptyMessage ?? t("leave:summary.unavailable")}
          </Text>
        ) : (
          <>
            <dl className="grid gap-4 sm:grid-cols-3">
              <Field label={t("leave:summary.hireDate")}>{date(data.hireDate)}</Field>
              <Field label={t("leave:summary.accrualStartDate")}>{date(data.leaveAccrualStartDate)}</Field>
              {ruleSet && <Field label={t("leave:summary.ruleSet")}>{ruleSet}</Field>}
            </dl>
            {data.balances.length === 0 ? (
              <Text size="sm" c="dimmed">
                {t("leave:summary.noBalances")}
              </Text>
            ) : (
              data.balances.map((item: LeaveBalanceItem) => {
                const b = item.balance;
                return (
                  <Stack key={b.leaveTypeId} gap="sm">
                    <Divider />
                    <Group justify="space-between" align="baseline" gap="xs">
                      <Text component="h3" fw={600}>
                        {leaveName(item.leaveTypeName)}
                      </Text>
                      <Text size="sm">
                        {t("leave:summary.available")}:{" "}
                        <Text span fw={600}>
                          {days(b.availableBalance)}
                        </Text>{" "}
                        {t("leave:days")}
                      </Text>
                    </Group>
                    <BalanceUsage used={b.totalUsed} currentBalance={b.currentBalance} />
                    <dl className="grid gap-4 sm:grid-cols-3">
                      <Field label={t("leave:summary.currentPeriod")}>
                        {date(b.currentPeriodFrom)} – {date(b.currentPeriodTo)}
                      </Field>
                      <Field label={t("leave:summary.totalAccrued")}>{days(b.totalAccrued)}</Field>
                      <Field label={t("leave:summary.totalUsed")}>{days(b.totalUsed)}</Field>
                      <Field label={t("leave:summary.futureApprovedInPeriod")}>{days(b.futureApprovedInPeriod)}</Field>
                      <Field label={t("leave:summary.currentBalance")}>{days(b.currentBalance)}</Field>
                      <Field label={t("leave:summary.availableBalance")}>{days(b.availableBalance)}</Field>
                      <Field label={t("leave:summary.lastPeriodAccrued")}>{days(b.lastPeriodAccrued)}</Field>
                      <Field label={t("leave:summary.currentPeriodUsed")}>{days(b.currentPeriodUsed)}</Field>
                      <Field label={t("leave:summary.nextPeriodApproved")}>{days(b.nextPeriodApproved)}</Field>
                      <Field label={t("leave:summary.carriedOver")}>{days(b.carriedOverIntoPeriod)}</Field>
                      <Field label={t("leave:summary.extraGrants")}>{days(b.extraGrants)}</Field>
                      <Field label={t("leave:summary.nextAccrualDate")}>{date(b.nextAccrualDate)}</Field>
                    </dl>
                  </Stack>
                );
              })
            )}
          </>
        )}
      </Stack>
    </Card>
  );
}

/** "Detaylar": the employee's leave ledger ("İzin defteri"), optionally filtered by type; `own` reads `/leave/me/ledger`. */
export function LeaveLedgerDialog({
  employeeId,
  own = false,
  typeOptions,
  onClose,
}: {
  employeeId: string;
  own?: boolean;
  typeOptions: { id: string; name: string }[];
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation(["leave", "common"]);
  const days = useDays();
  const [typeId, setTypeId] = useState(ALL_TYPES);
  const filter = typeId === ALL_TYPES ? undefined : typeId;
  const { data: rows = NO_LEDGER_ENTRIES, isLoading } = useQuery({
    queryKey: ["leave", "ledger", own ? "me" : employeeId, filter ?? null],
    queryFn: () => (own ? getMyLeaveLedger(filter) : getEmployeeLeaveLedger(employeeId, filter)),
  });
  const nameOf = (id: string) => typeOptions.find((o) => o.id === id)?.name ?? "—";

  const columns: ColumnDef<LeaveLedgerEntry>[] = [
    {
      id: "effectiveDate",
      accessorKey: "effectiveDate",
      header: t("leave:ledger.effectiveDate"),
      meta: { label: t("leave:ledger.effectiveDate"), cellClassName: "whitespace-nowrap" },
      cell: ({ row }) => formatDateOnly(row.original.effectiveDate, i18n.language),
    },
    {
      id: "leaveType",
      accessorFn: (row) => nameOf(row.leaveTypeId),
      header: t("leave:ledger.type"),
      meta: { label: t("leave:ledger.type") },
    },
    {
      id: "entryType",
      accessorFn: (row) => t(`leave:ledgerType.${row.type}`),
      header: t("leave:ledger.entryType"),
      meta: { label: t("leave:ledger.entryType") },
    },
    {
      id: "amount",
      accessorKey: "amount",
      header: t("leave:ledger.amount"),
      meta: { label: t("leave:ledger.amount"), headerClassName: "text-right", cellClassName: "text-right" },
      cell: ({ row }) => (
        <Text span inherit c={row.original.amount < 0 ? "red" : undefined}>
          {row.original.amount > 0 ? "+" : ""}
          {days(row.original.amount)}
        </Text>
      ),
    },
    {
      id: "source",
      accessorFn: (row) => t(`leave:ledgerSource.${row.source}`),
      header: t("leave:ledger.source"),
      meta: { label: t("leave:ledger.source") },
    },
    {
      id: "reason",
      accessorFn: (row) => row.reason ?? "",
      enableSorting: false,
      header: t("leave:ledger.reason"),
      meta: { label: t("leave:ledger.reason"), cellClassName: "max-w-[16rem] truncate" },
      cell: ({ row }) => <span title={row.original.reason ?? undefined}>{row.original.reason ?? "—"}</span>,
    },
    {
      id: "recordedAt",
      accessorKey: "recordedAt",
      header: t("leave:ledger.recordedAt"),
      meta: { label: t("leave:ledger.recordedAt"), cellClassName: "whitespace-nowrap" },
      cell: ({ row }) => (
        <Text span size="xs" c="dimmed">
          {formatDateTime(row.original.recordedAt, i18n.language)}
        </Text>
      ),
    },
  ];

  const table = useDataTable({
    columns,
    data: rows,
    getRowId: (row) => row.id,
    columnVisibilityKey: LEDGER_COLUMNS_KEY,
    pageSize: PAGE_SIZE,
  });

  return (
    <Modal
      opened
      onClose={onClose}
      size="56rem"
      title={
        <span className="flex items-center gap-2">
          <ListOrdered size={20} /> {t("leave:ledger.title")}
        </span>
      }
    >
      <Stack gap="md">
        <Text size="sm" c="dimmed">
          {t("leave:ledger.description")}
        </Text>
        {(typeOptions.length > 1 || rows.length > 0) && (
          <Group justify="space-between" gap="xs">
            {typeOptions.length > 1 && (
              <Select
                className="w-full max-w-xs"
                aria-label={t("leave:ledger.type")}
                value={typeId}
                onChange={(value) => setTypeId(value ?? ALL_TYPES)}
                allowDeselect={false}
                data={[{ value: ALL_TYPES, label: t("leave:ledger.allTypes") }, ...typeOptions.map((option) => ({ value: option.id, label: option.name }))]}
              />
            )}
            {rows.length > 0 && <DataTableViewOptions table={table} className={`ml-auto ${COLUMN_CHOOSER_CLASS}`} />}
          </Group>
        )}
        {isLoading ? (
          <Skeleton h={160} />
        ) : rows.length === 0 ? (
          <Text size="sm" c="dimmed">
            {t("leave:ledger.empty")}
          </Text>
        ) : (
          <DataTable table={table} />
        )}
        <Group justify="flex-end">
          <Button type="button" variant="default" onClick={onClose}>
            {t("common:close")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

/**
 * Requests list (Tür, Başlangıç, Bitiş, Gün, Durum, Kaynak, Açıklama); `actions` renders a trailing cell per row and
 * `onOpen` makes rows open the request detail.
 */
export function LeaveRequestsTable({
  requests,
  typeName,
  actions,
  onOpen,
}: {
  requests: LeaveRequest[];
  typeName: (request: LeaveRequest) => string;
  actions?: (request: LeaveRequest) => ReactNode;
  onOpen?: (request: LeaveRequest) => void;
}) {
  const { t, i18n } = useTranslation(["leave"]);
  const days = useDays();
  const isMobile = useMediaQuery(MOBILE_QUERY);
  // Newest first until the user sorts by a column.
  const sorted = useMemo(() => [...requests].sort((a, b) => b.startDate.localeCompare(a.startDate)), [requests]);

  const columns: ColumnDef<LeaveRequest>[] = [
    {
      id: "leaveType",
      accessorFn: (request) => typeName(request),
      enableHiding: false,
      header: t("leave:requests.type"),
      meta: { label: t("leave:requests.type") },
      cell: ({ row }) =>
        onOpen ? (
          <Anchor
            component="button"
            type="button"
            inherit
            c="inherit"
            fw={500}
            ta="left"
            underline="hover"
            onClick={(event) => {
              event.stopPropagation();
              onOpen(row.original);
            }}
          >
            {typeName(row.original)}
          </Anchor>
        ) : (
          <Text span inherit fw={500}>
            {typeName(row.original)}
          </Text>
        ),
    },
    {
      id: "startDate",
      accessorKey: "startDate",
      header: t("leave:requests.startDate"),
      meta: { label: t("leave:requests.startDate"), cellClassName: "whitespace-nowrap" },
      cell: ({ row }) => formatDateOnly(row.original.startDate, i18n.language),
    },
    {
      id: "endDate",
      accessorKey: "endDate",
      header: t("leave:requests.endDate"),
      meta: { label: t("leave:requests.endDate"), cellClassName: "whitespace-nowrap" },
      cell: ({ row }) => formatDateOnly(row.original.endDate, i18n.language),
    },
    {
      id: "days",
      accessorKey: "calculatedDays",
      header: t("leave:requests.days"),
      meta: { label: t("leave:requests.days"), headerClassName: "text-right", cellClassName: "text-right" },
      cell: ({ row }) => days(row.original.calculatedDays),
    },
    {
      id: "submittedAt",
      accessorKey: "submittedAt",
      header: t("leave:requests.submittedAt"),
      meta: { label: t("leave:requests.submittedAt"), cellClassName: "whitespace-nowrap" },
      cell: ({ row }) => formatDateOnly(row.original.submittedAt.slice(0, 10), i18n.language),
    },
    {
      id: "status",
      accessorFn: (request) => t(`leave:status.${request.status}`),
      header: t("leave:requests.status"),
      meta: { label: t("leave:requests.status") },
      cell: ({ row }) => (
        <>
          <LeaveStatusBadge status={row.original.status} />
          {row.original.status === "cancelled" && row.original.cancellationReason && (
            <Text size="xs" c="dimmed" mt={4} maw="12rem" truncate="end" title={row.original.cancellationReason}>
              {row.original.cancellationReason}
            </Text>
          )}
        </>
      ),
    },
    {
      id: "source",
      accessorFn: (request) => t(`leave:requestSource.${request.source}`),
      header: t("leave:requests.source"),
      meta: { label: t("leave:requests.source") },
    },
    {
      id: "description",
      accessorFn: (request) => request.description ?? "",
      enableSorting: false,
      header: t("leave:requests.description"),
      meta: { label: t("leave:requests.description"), cellClassName: "max-w-[16rem] truncate" },
      cell: ({ row }) => <span title={row.original.description ?? undefined}>{row.original.description ?? "—"}</span>,
    },
    ...(actions
      ? [
          {
            id: "actions",
            enableSorting: false,
            enableHiding: false,
            header: () => null,
            meta: { headerClassName: "w-0" },
            cell: ({ row }) => <div onClick={(event) => event.stopPropagation()}>{actions(row.original)}</div>,
          } satisfies ColumnDef<LeaveRequest>,
        ]
      : []),
  ];

  const table = useDataTable({
    columns,
    data: sorted,
    getRowId: (request) => request.id,
    columnVisibilityKey: REQUESTS_COLUMNS_KEY,
    pageSize: PAGE_SIZE,
  });

  if (sorted.length === 0) {
    return (
      <Text size="sm" c="dimmed">
        {t("leave:requests.empty")}
      </Text>
    );
  }

  if (isMobile) {
    return <LeaveRequestsMobileList requests={sorted} typeName={typeName} actions={actions} onOpen={onOpen} />;
  }

  return (
    <Stack gap="xs">
      <Group justify="flex-end">
        <DataTableViewOptions table={table} className={COLUMN_CHOOSER_CLASS} />
      </Group>
      <DataTable table={table} onRowClick={onOpen} />
    </Stack>
  );
}

/** ≤390px card list mirroring `LeaveRequestsTable`'s columns (S-6 mobil kalıbı). */
function LeaveRequestsMobileList({
  requests,
  typeName,
  actions,
  onOpen,
}: {
  requests: LeaveRequest[];
  typeName: (request: LeaveRequest) => string;
  actions?: (request: LeaveRequest) => ReactNode;
  onOpen?: (request: LeaveRequest) => void;
}) {
  const { t, i18n } = useTranslation(["leave"]);
  const days = useDays();

  return (
    <Stack gap="sm">
      {requests.map((request) => (
        <Card
          key={request.id}
          withBorder
          padding="md"
          onClick={onOpen ? () => onOpen(request) : undefined}
          style={onOpen ? { cursor: "pointer" } : undefined}
        >
          <Stack gap={4}>
            <Group justify="space-between" align="flex-start" gap="xs" wrap="nowrap">
              <Text size="sm" fw={500}>
                {typeName(request)}
              </Text>
              <LeaveStatusBadge status={request.status} />
            </Group>
            <Text size="xs" c="dimmed">
              {formatDateOnly(request.startDate, i18n.language)} – {formatDateOnly(request.endDate, i18n.language)} · {days(request.calculatedDays)}{" "}
              {t("leave:days")}
            </Text>
            {request.status === "cancelled" && request.cancellationReason && (
              <Text size="xs" c="dimmed">
                {request.cancellationReason}
              </Text>
            )}
            {request.description && (
              <Text size="xs" c="dimmed">
                {request.description}
              </Text>
            )}
            {actions && (
              <Group justify="flex-end" gap={4} wrap="nowrap" onClick={(event) => event.stopPropagation()}>
                {actions(request)}
              </Group>
            )}
          </Stack>
        </Card>
      ))}
    </Stack>
  );
}
