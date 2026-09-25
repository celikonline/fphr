import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Badge, Button, Card, Group, Skeleton, Table, Tabs, Text, Title } from "@mantine/core";
import { Plus, Stamp } from "lucide-react";
import ConfirmDialog from "@/components/confirm-dialog";
import NoAccess from "@/components/no-access";
import { useAnyPermission, usePermission } from "@/components/permission-guard";
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
      {tab === "mine" ? <RequestList scope="mine" /> : <RequestList scope="all" />}
      <PunchRequestForm opened={creating} onClose={() => setCreating(false)} />
    </div>
  );
}

function RequestList({ scope }: { scope: "mine" | "all" }) {
  const { t, i18n } = useTranslation(["attendance-daily", "common"]);
  const queryClient = useQueryClient();
  const [cancelling, setCancelling] = useState<PunchRequest | null>(null);
  const mine = scope === "mine";

  const query = useQuery({
    queryKey: mine ? MY_PUNCH_REQUESTS_KEY : ["attendance", "punch-requests", "all"],
    queryFn: () => (mine ? listMyPunchRequests() : listPunchRequests()),
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

  if (query.isLoading) return <Skeleton height={120} />;
  if (query.isError) return <Text c="red">{t("attendance-daily:punchRequests.loadError")}</Text>;
  const rows = query.data ?? [];
  if (rows.length === 0) return <Text c="dimmed">{t("attendance-daily:punchRequests.empty")}</Text>;

  return (
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
              {!mine && <Table.Td>{row.employeeName ?? row.employeeId}</Table.Td>}
              <Table.Td>{formatDateTime(row.occurredAtUtc, i18n.language)}</Table.Td>
              <Table.Td>{row.direction === "out" ? t("attendance-daily:punchRequests.out") : t("attendance-daily:punchRequests.in")}</Table.Td>
              <Table.Td>{row.reason}</Table.Td>
              <Table.Td>
                <Badge color={STATUS_COLOR[row.status] ?? "gray"} variant="light">
                  {t(`attendance-daily:punchRequests.statuses.${row.status}`)}
                </Badge>
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
      <ConfirmDialog
        open={cancelling !== null}
        onOpenChange={(open) => !open && setCancelling(null)}
        description={t("attendance-daily:punchRequests.cancelConfirm")}
        confirmLabel={t("attendance-daily:punchRequests.cancel")}
        isLoading={cancelMutation.isPending}
        onConfirm={() => cancelling && cancelMutation.mutate(cancelling.id)}
      />
    </Card>
  );
}
