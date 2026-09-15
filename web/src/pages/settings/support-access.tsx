import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Check, ShieldCheck, ShieldOff } from "lucide-react";
import { Alert, Badge, Button, Card, Group, Select, Text, Title, type BadgeProps } from "@mantine/core";
import { usePermission } from "@/components/permission-guard";
import NoAccess from "@/components/no-access";
import ConfirmDialog from "@/components/confirm-dialog";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateTime } from "@/lib/dates";
import {
  IMPERSONATION_GRANT_STATUSES,
  approveImpersonationGrant,
  listImpersonationGrants,
  revokeImpersonationGrant,
  type ImpersonationGrant,
  type ImpersonationGrantStatus,
} from "@/services/impersonation.service";

const ALL = "all";

type BadgeStyle = Pick<BadgeProps, "variant" | "color">;

const STATUS_BADGE: Record<ImpersonationGrantStatus, BadgeStyle> = {
  Pending: { variant: "light", color: "gray" },
  Approved: { variant: "filled" },
  Active: { variant: "filled", color: "green" },
  Expired: { variant: "outline", color: "gray" },
  Revoked: { variant: "filled", color: "red" },
};
const FALLBACK_BADGE: BadgeStyle = { variant: "outline", color: "gray" };

const REVOCABLE: ImpersonationGrantStatus[] = ["Pending", "Approved", "Active"];

/** Lifecycle order used when sorting by status. */
const STATUS_ORDER = IMPERSONATION_GRANT_STATUSES;
const PAGE_SIZE = 20;
/** Stable empty list so client paging does not see new data (and reset) on every render while loading. */
const NO_GRANTS: ImpersonationGrant[] = [];
const COLUMN_VISIBILITY_KEY = "senseik.settings.supportAccess.columns";

/** Chronological compare of ISO instants; missing values sort last. */
function compareInstants(a: string | null, b: string | null): number {
  if (a === b) return 0;
  if (!a) return 1;
  if (!b) return -1;
  return new Date(a).getTime() - new Date(b).getTime();
}

type PendingAction = { kind: "approve" | "revoke"; grant: ImpersonationGrant } | null;

export default function SupportAccessPage() {
  const canApprove = usePermission("identity.impersonation.approve");
  if (!canApprove) return <NoAccess />;
  return <SupportAccessList />;
}

function SupportAccessList() {
  const { t, i18n } = useTranslation(["settings", "common"]);
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<string>(ALL);
  const [pending, setPending] = useState<PendingAction>(null);

  const { data, isLoading, isError } = useQuery({
    queryKey: ["identity", "impersonation-grants", status],
    queryFn: () => listImpersonationGrants(status === ALL ? undefined : status),
  });

  const onDone = (successKey: string) => {
    queryClient.invalidateQueries({ queryKey: ["identity", "impersonation-grants"] });
    toast({ variant: "success", title: t("common:success"), description: t(successKey) });
    setPending(null);
  };
  const onFail = (error: unknown) => {
    toast({
      variant: "destructive",
      title: t("common:error"),
      description: getApiErrorMessage(error, t("settings:supportAccess.actionError")),
    });
    setPending(null);
  };

  const approveMutation = useMutation({
    mutationFn: approveImpersonationGrant,
    onSuccess: () => onDone("settings:supportAccess.approveSuccess"),
    onError: onFail,
  });
  const revokeMutation = useMutation({
    mutationFn: revokeImpersonationGrant,
    onSuccess: () => onDone("settings:supportAccess.revokeSuccess"),
    onError: onFail,
  });

  function confirm() {
    if (!pending) return;
    if (pending.kind === "approve") approveMutation.mutate(pending.grant.id);
    else revokeMutation.mutate(pending.grant.id);
  }

  const locale = i18n.language;
  const requesterOf = (grant: ImpersonationGrant) => grant.platformUserEmail ?? t("settings:supportAccess.senseStaff");

  const columns: ColumnDef<ImpersonationGrant>[] = [
    {
      id: "requestedBy",
      accessorFn: requesterOf,
      enableHiding: false,
      header: t("settings:supportAccess.requestedBy"),
      meta: { label: t("settings:supportAccess.requestedBy") },
      sortingFn: (a, b) => requesterOf(a.original).localeCompare(requesterOf(b.original), locale),
      cell: ({ row }) => (
        <Text size="sm" fw={500}>
          {requesterOf(row.original)}
        </Text>
      ),
    },
    {
      id: "reason",
      accessorKey: "reason",
      enableSorting: false,
      header: t("settings:supportAccess.reason"),
      meta: { label: t("settings:supportAccess.reason"), cellClassName: "max-w-xs whitespace-pre-wrap" },
    },
    {
      id: "status",
      accessorKey: "status",
      header: t("settings:supportAccess.status"),
      meta: { label: t("settings:supportAccess.status") },
      sortingFn: (a, b) => STATUS_ORDER.indexOf(a.original.status) - STATUS_ORDER.indexOf(b.original.status),
      cell: ({ row }) => (
        <Badge {...(STATUS_BADGE[row.original.status] ?? FALLBACK_BADGE)}>
          {t(`settings:supportAccess.statuses.${row.original.status}`, row.original.status)}
        </Badge>
      ),
    },
    {
      id: "requestedAt",
      accessorKey: "requestedAt",
      header: t("settings:supportAccess.requestedAt"),
      meta: { label: t("settings:supportAccess.requestedAt"), cellClassName: "whitespace-nowrap" },
      sortingFn: (a, b) => compareInstants(a.original.requestedAt, b.original.requestedAt),
      cell: ({ row }) => formatDateTime(row.original.requestedAt, locale),
    },
    {
      id: "expiresAt",
      accessorKey: "expiresAt",
      header: t("settings:supportAccess.expiresAt"),
      meta: { label: t("settings:supportAccess.expiresAt"), cellClassName: "whitespace-nowrap" },
      sortingFn: (a, b) => compareInstants(a.original.expiresAt, b.original.expiresAt),
      cell: ({ row }) => formatDateTime(row.original.expiresAt, locale),
    },
    {
      id: "actions",
      enableHiding: false,
      enableSorting: false,
      header: t("common:actions"),
      meta: { headerClassName: "text-right", cellClassName: "whitespace-nowrap text-right" },
      cell: ({ row }) => {
        const grant = row.original;
        return (
          <div className="inline-flex gap-2" onClick={(e) => e.stopPropagation()}>
            {grant.status === "Pending" && (
              <Button
                type="button"
                size="xs"
                leftSection={<Check className="h-4 w-4" />}
                onClick={() => setPending({ kind: "approve", grant })}
              >
                {t("settings:supportAccess.approve")}
              </Button>
            )}
            {REVOCABLE.includes(grant.status) && (
              <Button
                type="button"
                size="xs"
                variant="outline"
                color="red"
                leftSection={<ShieldOff className="h-4 w-4" />}
                onClick={() => setPending({ kind: "revoke", grant })}
              >
                {t("settings:supportAccess.revoke")}
              </Button>
            )}
          </div>
        );
      },
    },
  ];

  const table = useDataTable({
    columns,
    data: data ?? NO_GRANTS,
    getRowId: (grant) => grant.id,
    columnVisibilityKey: COLUMN_VISIBILITY_KEY,
    pageSize: PAGE_SIZE,
  });

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <ShieldCheck className="h-6 w-6" /> {t("settings:supportAccess.title")}
        </Title>
        <Text c="dimmed">{t("settings:supportAccess.subtitle")}</Text>
      </div>

      <Alert
        variant="light"
        color="gray"
        icon={<ShieldCheck className="h-4 w-4" />}
        title={t("settings:supportAccess.privacyTitle")}
      >
        {t("settings:supportAccess.privacyText")}
      </Alert>

      <Group gap="sm">
        <Select
          className="w-full max-w-xs"
          aria-label={t("settings:supportAccess.status")}
          value={status}
          onChange={(value) => setStatus(value ?? ALL)}
          allowDeselect={false}
          data={[
            { value: ALL, label: t("settings:supportAccess.allStatuses") },
            ...IMPERSONATION_GRANT_STATUSES.map((value) => ({
              value,
              label: t(`settings:supportAccess.statuses.${value}`),
            })),
          ]}
        />
        <DataTableViewOptions table={table} className="ml-auto" />
      </Group>

      <Card withBorder padding={0}>
        {isError ? (
          <Text size="sm" c="red" ta="center" p="lg">
            {t("settings:supportAccess.loadError")}
          </Text>
        ) : (
          <DataTable table={table} isLoading={isLoading} emptyMessage={t("settings:supportAccess.empty")} />
        )}
      </Card>

      <ConfirmDialog
        open={pending !== null}
        onOpenChange={(open) => !open && setPending(null)}
        title={
          pending?.kind === "approve" ? t("settings:supportAccess.approveTitle") : t("settings:supportAccess.revokeTitle")
        }
        description={
          pending?.kind === "approve"
            ? t("settings:supportAccess.approveConfirm", { requester: pending.grant.platformUserEmail ?? t("settings:supportAccess.senseStaff") })
            : t("settings:supportAccess.revokeConfirm")
        }
        confirmLabel={pending?.kind === "approve" ? t("settings:supportAccess.approve") : t("settings:supportAccess.revoke")}
        confirmVariant={pending?.kind === "approve" ? "default" : "destructive"}
        onConfirm={confirm}
        isLoading={approveMutation.isPending || revokeMutation.isPending}
      />
    </div>
  );
}
