import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Badge, Button, Card, Group, NativeSelect, Skeleton, Table, Tabs, Text, Title } from "@mantine/core";
import { FileUp, LifeBuoy, Plus, Stamp } from "lucide-react";
import { Link } from "react-router";
import ConfirmDialog from "@/components/confirm-dialog";
import NoAccess from "@/components/no-access";
import { useAnyPermission, usePermission } from "@/components/permission-guard";
import AttendanceEmptyState from "@/components/attendance/attendance-empty-state";
import { MY_PUNCH_REQUESTS_KEY, PunchRequestForm } from "@/components/attendance/punch-request-form";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateTime } from "@/lib/dates";
import {
  cancelPunchRequest,
  listMyPunchRequests,
  listPunchRequests,
  type PunchRequest,
  type PunchRequestStatus,
} from "@/services/attendance-punch-requests.service";

const STATUS_COLOR: Record<PunchRequestStatus, string> = {
  pending: "yellow",
  approved: "green",
  rejected: "red",
  cancelled: "gray",
};

/** Kayıt talepleri (Z2.1): çalışanın kendi talepleri + oluşturma/iptal; yöneticiye tüm talepler (salt okunur). */
export default function PunchRequestsPage() {
  const canUse = useAnyPermission(["attendance.punchrequest.manage", "attendance.my.punchrequest"]);
  if (!canUse) return <NoAccess />;
  return <PunchRequests />;
}

function PunchRequests() {
  const { t } = useTranslation(["attendance-daily", "common"]);
  const canMine = usePermission("attendance.my.punchrequest");
  const canManage = usePermission("attendance.punchrequest.manage");
  const [tab, setTab] = useState<"mine" | "all">(canMine ? "mine" : "all");
  const [creating, setCreating] = useState(false);

  return (
    <div className="max-w-5xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Stamp size={24} /> {t("attendance-daily:punchRequests.title")}
          </Title>
          <Text c="dimmed" size="sm" mt={4}>
            {t("attendance-daily:punchRequests.subtitle")}
          </Text>
        </div>
        {canMine && (
          <Button leftSection={<Plus size={16} />} onClick={() => setCreating(true)}>
            {t("attendance-daily:punchRequests.new")}
          </Button>
        )}
      </Group>
      <Tabs value={tab} onChange={(value) => setTab(value === "all" ? "all" : "mine")}>
        <Tabs.List>
          {canMine && <Tabs.Tab value="mine">{t("attendance-daily:punchRequests.tabMine")}</Tabs.Tab>}
          {canManage && <Tabs.Tab value="all">{t("attendance-daily:punchRequests.tabAll")}</Tabs.Tab>}
        </Tabs.List>
      </Tabs>
      {tab === "mine" ? <RequestList scope="mine" onCreate={() => setCreating(true)} /> : <RequestList scope="all" onCreate={() => setCreating(true)} />}
      <PunchRequestForm opened={creating} onClose={() => setCreating(false)} />
    </div>
  );
}

function RequestList({ scope, onCreate }: { scope: "mine" | "all"; onCreate: () => void }) {
  const { t, i18n } = useTranslation(["attendance-daily", "attendance", "common"]);
  const queryClient = useQueryClient();
  const [cancelling, setCancelling] = useState<PunchRequest | null>(null);
  const mine = scope === "mine";
  const canCreate = usePermission("attendance.my.punchrequest");
  const canImport = usePermission("attendance.timesheet.import");
  const [statusFilter, setStatusFilter] = useState<PunchRequestStatus | "">("");

  const query = useQuery({
    queryKey: mine ? MY_PUNCH_REQUESTS_KEY : ["attendance", "punch-requests", "all", statusFilter],
    queryFn: () => (mine ? listMyPunchRequests() : listPunchRequests({ status: statusFilter || undefined })),
  });

  const cancelMutation = useMutation({
    mutationFn: (id: string) => cancelPunchRequest(id),
    onSuccess: async () => {
      toast({ variant: "success", title: t("common:success"), description: t("attendance-daily:punchRequests.cancelled") });
      setCancelling(null);
      await queryClient.invalidateQueries({ queryKey: ["attendance", "punch-requests"] });
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("common:error")) }),
  });

  const rows = query.data ?? [];
  const filter = mine ? null : (
    <NativeSelect
      label={t("attendance-daily:punchRequests.status")}
      value={statusFilter}
      onChange={(e) => setStatusFilter(e.currentTarget.value as PunchRequestStatus | "")}
      data={[
        { value: "", label: t("attendance-daily:punchRequests.allStatuses") },
        ...(Object.keys(STATUS_COLOR) as PunchRequestStatus[]).map((value) => ({
          value,
          label: t(`attendance-daily:punchRequests.statuses.${value}`),
        })),
      ]}
      w={220}
    />
  );

  let body;
  if (query.isLoading) body = <Skeleton height={120} />;
  else if (query.isError) body = <Text c="red">{t("attendance-daily:punchRequests.loadError")}</Text>;
  else if (rows.length === 0) body = (
    <AttendanceEmptyState
      title={t("attendance-daily:punchRequests.empty")}
      description={t(mine ? "attendance-daily:punchRequests.emptyMineGuide" : "attendance-daily:punchRequests.emptyAllGuide")}
      action={mine && canCreate ? (
        <Button leftSection={<Plus size={16} />} onClick={onCreate}>{t("attendance-daily:punchRequests.new")}</Button>
      ) : canImport ? (
        <Button component={Link} to="/app/attendance/device-logs/import" leftSection={<FileUp size={16} />}>
          {t("attendance:openDeviceImport")}
        </Button>
      ) : (
        <Button component={Link} to="/app/support" variant="light" leftSection={<LifeBuoy size={16} />}>
          {t("attendance:contactSupport")}
        </Button>
      )}
    />
  );
  else body = (
    <Card withBorder padding="sm" radius="md">
      <Table>
        <Table.Thead>
          <Table.Tr>
            {!mine && <Table.Th>{t("attendance-daily:punchRequests.employee")}</Table.Th>}
            <Table.Th>{t("attendance-daily:punchRequests.occurredAt")}</Table.Th>
            <Table.Th>{t("attendance-daily:punchRequests.direction")}</Table.Th>
            <Table.Th>{t("attendance-daily:punchRequests.reason")}</Table.Th>
            <Table.Th>{t("attendance-daily:punchRequests.status")}</Table.Th>
            {mine && <Table.Th />}
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {rows.map((row) => (
            <Table.Tr key={row.id}>
              {!mine && <Table.Td>{row.employeeName || t("attendance-daily:punchRequests.unknownEmployee")}</Table.Td>}
              <Table.Td>{formatDateTime(row.occurredAtUtc, i18n.language)}</Table.Td>
              <Table.Td>{row.direction === "out" ? t("attendance-daily:punchRequests.out") : t("attendance-daily:punchRequests.in")}</Table.Td>
              <Table.Td>{row.reason}</Table.Td>
              <Table.Td>
                <Badge color={STATUS_COLOR[row.status] ?? "gray"} variant="light">
                  {t(`attendance-daily:punchRequests.statuses.${row.status}`)}
                </Badge>
                {row.decidedAtUtc && (
                  <Text size="xs" c="dimmed">
                    {t("attendance-daily:punchRequests.decidedAt", { date: formatDateTime(row.decidedAtUtc, i18n.language) })}
                  </Text>
                )}
              </Table.Td>
              {mine && (
                <Table.Td>
                  {row.status === "pending" && (
                    <Button size="xs" variant="subtle" color="red" onClick={() => setCancelling(row)}>
                      {t("attendance-daily:punchRequests.cancel")}
                    </Button>
                  )}
                </Table.Td>
              )}
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Card>
  );

  return (
    <div className="space-y-4">
      {filter}
      {body}
      <ConfirmDialog
        open={cancelling !== null}
        onOpenChange={(open) => !open && setCancelling(null)}
        description={t("attendance-daily:punchRequests.cancelConfirm")}
        confirmLabel={t("attendance-daily:punchRequests.cancel")}
        isLoading={cancelMutation.isPending}
        onConfirm={() => cancelling && cancelMutation.mutate(cancelling.id)}
      />
    </div>
  );
}
