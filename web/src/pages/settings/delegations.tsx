import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Workflow } from "lucide-react";
import { Alert, Badge, Button, Card, Group, Input, Loader, Stack, Table, Tabs, Text, Title } from "@mantine/core";
import NoAccess from "@/components/no-access";
import ConfirmDialog from "@/components/confirm-dialog";
import { usePermission } from "@/components/permission-guard";
import EmployeePicker, { type PickedEmployee } from "@/components/employees/employee-picker";
import { DatePicker } from "@/components/ui/date-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly, toDateOnly } from "@/lib/dates";
import { useAuthStore } from "@/store/auth.store";
import {
  createDelegation,
  createMyDelegation,
  listDelegations,
  listMyDelegations,
  revokeDelegation,
  revokeMyDelegation,
  type Delegation,
} from "@/services/workflow-delegations.service";

type Scope = "mine" | "admin";
type DelegationStatus = "active" | "scheduled" | "expired" | "revoked";
type FormError = "endBeforeStart" | "self" | "required";

/** Status by calendar date ("yyyy-MM-dd" compares lexically) and the revoked flag: active only while today is inside [start, end]. */
export function delegationStatus(d: Pick<Delegation, "startDate" | "endDate" | "isRevoked">, today: string): DelegationStatus {
  if (d.isRevoked) return "revoked";
  if (today < d.startDate) return "scheduled";
  if (today > d.endDate) return "expired";
  return "active";
}

const STATUS_COLOR: Record<DelegationStatus, string> = { active: "green", scheduled: "blue", expired: "gray", revoked: "red" };

const SCOPE_API = {
  mine: { queryKey: ["workflow", "delegations", "mine"], list: listMyDelegations, revoke: revokeMyDelegation },
  admin: { queryKey: ["workflow", "delegations", "admin"], list: listDelegations, revoke: revokeDelegation },
} as const;

export default function DelegationsPage() {
  const { t } = useTranslation(["delegations"]);
  const canMine = usePermission("workflow.my.delegation");
  const canAdmin = usePermission("workflow.delegation.manage");
  if (!canMine && !canAdmin) return <NoAccess />;

  return (
    <div className="max-w-5xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <Workflow size={24} /> {t("delegations:title")}
        </Title>
        <Text c="dimmed" size="sm" mt={4}>
          {t("delegations:subtitle")}
        </Text>
      </div>
      {canMine && canAdmin ? (
        <Tabs defaultValue="mine" keepMounted={false}>
          <Tabs.List>
            <Tabs.Tab value="mine">{t("delegations:tabs.mine")}</Tabs.Tab>
            <Tabs.Tab value="admin">{t("delegations:tabs.admin")}</Tabs.Tab>
          </Tabs.List>
          <Tabs.Panel value="mine" pt="md">
            <DelegationsPanel scope="mine" />
          </Tabs.Panel>
          <Tabs.Panel value="admin" pt="md">
            <DelegationsPanel scope="admin" />
          </Tabs.Panel>
        </Tabs>
      ) : (
        <DelegationsPanel scope={canAdmin ? "admin" : "mine"} />
      )}
    </div>
  );
}

function DelegationsPanel({ scope }: { scope: Scope }) {
  const { t, i18n } = useTranslation(["delegations", "common"]);
  const me = useAuthStore((s) => s.user);
  const queryClient = useQueryClient();
  const api = SCOPE_API[scope];

  const [delegator, setDelegator] = useState<PickedEmployee | null>(null);
  const [delegate, setDelegate] = useState<PickedEmployee | null>(null);
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate] = useState("");
  const [formError, setFormError] = useState<FormError | null>(null);
  const [pendingRevoke, setPendingRevoke] = useState<string | null>(null);

  const listQuery = useQuery({ queryKey: api.queryKey, queryFn: api.list });

  const onError = (error: unknown) =>
    toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("delegations:errors.generic")) });

  const createMutation = useMutation({
    mutationFn: (delegateEmployeeId: string) =>
      scope === "admin" && delegator
        ? createDelegation({ delegatorEmployeeId: delegator.id, delegateEmployeeId, startDate, endDate })
        : createMyDelegation({ delegateEmployeeId, startDate, endDate }),
    onSuccess: () => {
      toast({ variant: "success", title: t("common:success"), description: t("delegations:toast.created") });
      setDelegator(null);
      setDelegate(null);
      setStartDate("");
      setEndDate("");
      void queryClient.invalidateQueries({ queryKey: api.queryKey });
    },
    onError,
  });

  const revokeMutation = useMutation({
    mutationFn: (id: string) => api.revoke(id),
    onSuccess: () => {
      toast({ variant: "success", title: t("common:success"), description: t("delegations:toast.revoked") });
      void queryClient.invalidateQueries({ queryKey: api.queryKey });
    },
    onError,
    onSettled: () => setPendingRevoke(null),
  });

  function submit() {
    setFormError(null);
    if (!delegate || !startDate || !endDate || (scope === "admin" && !delegator)) return setFormError("required");
    if (endDate < startDate) return setFormError("endBeforeStart");
    const principalId = scope === "admin" ? delegator?.id : me?.employeeId;
    if (delegate.id === principalId) return setFormError("self");
    createMutation.mutate(delegate.id);
  }

  const today = toDateOnly(new Date());
  const items = listQuery.data ?? [];
  const name = (employeeId: string, display?: string) =>
    display ?? (employeeId === me?.employeeId ? me.name : t("delegations:list.unknown"));
  /** Revoke is offered only for a live delegation the caller may revoke (admin: any; mine: the ones they gave). */
  const canRevoke = (d: Delegation) => {
    const status = delegationStatus(d, today);
    if (status === "expired" || status === "revoked") return false;
    return scope === "admin" || d.delegatorEmployeeId === me?.employeeId;
  };

  return (
    <Stack gap="lg">
      <Alert variant="light" color="blue">
        {scope === "admin" ? t("delegations:adminInfo") : t("delegations:info")}
      </Alert>

      <Card withBorder padding="lg" radius="md">
        <Stack gap="md">
          <Title order={2} size="h4">
            {t("delegations:form.title")}
          </Title>
          {scope === "admin" && (
            <EmployeePicker
              label={t("delegations:form.delegator")}
              placeholder={t("delegations:form.delegatePlaceholder")}
              value={delegator}
              onChange={setDelegator}
              allowClear
              withAsterisk
            />
          )}
          <EmployeePicker
            label={t("delegations:form.delegate")}
            placeholder={t("delegations:form.delegatePlaceholder")}
            value={delegate}
            onChange={setDelegate}
            excludeId={scope === "mine" ? (me?.employeeId ?? undefined) : undefined}
            allowClear
            withAsterisk
          />
          <Group align="flex-start" grow>
            <Input.Wrapper label={t("delegations:form.from")} withAsterisk>
              <DatePicker id="delegation-from" value={startDate} onChange={setStartDate} />
            </Input.Wrapper>
            <Input.Wrapper label={t("delegations:form.to")} withAsterisk>
              <DatePicker id="delegation-to" value={endDate} onChange={setEndDate} min={startDate || undefined} />
            </Input.Wrapper>
          </Group>
          {formError && (
            <Text c="red" size="sm" role="alert">
              {t(`delegations:errors.${formError}`)}
            </Text>
          )}
          <Group>
            <Button onClick={submit} loading={createMutation.isPending}>
              {t("delegations:form.submit")}
            </Button>
          </Group>
        </Stack>
      </Card>

      <Card withBorder padding="lg" radius="md">
        <Title order={2} size="h4" mb="md">
          {scope === "admin" ? t("delegations:tabs.admin") : t("delegations:list.title")}
        </Title>
        {listQuery.isLoading ? (
          <Loader size="sm" />
        ) : items.length === 0 ? (
          <Text c="dimmed" size="sm">
            {t("delegations:list.empty")}
          </Text>
        ) : (
          <Table.ScrollContainer minWidth={560}>
            <Table verticalSpacing="xs">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t("delegations:list.delegator")}</Table.Th>
                  <Table.Th>{t("delegations:list.delegate")}</Table.Th>
                  <Table.Th>{t("delegations:list.from")}</Table.Th>
                  <Table.Th>{t("delegations:list.to")}</Table.Th>
                  <Table.Th>{t("delegations:list.status")}</Table.Th>
                  <Table.Th />
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {items.map((d) => {
                  const status = delegationStatus(d, today);
                  return (
                    <Table.Tr key={d.id}>
                      <Table.Td>{name(d.delegatorEmployeeId, d.delegatorName)}</Table.Td>
                      <Table.Td>{name(d.delegateEmployeeId, d.delegateName)}</Table.Td>
                      <Table.Td>{formatDateOnly(d.startDate, i18n.language)}</Table.Td>
                      <Table.Td>{formatDateOnly(d.endDate, i18n.language)}</Table.Td>
                      <Table.Td>
                        <Badge color={STATUS_COLOR[status]} variant="light">
                          {t(`delegations:status.${status}`)}
                        </Badge>
                      </Table.Td>
                      <Table.Td>
                        {canRevoke(d) && (
                          <Button size="compact-sm" variant="light" color="red" onClick={() => setPendingRevoke(d.id)}>
                            {t("delegations:list.revoke")}
                          </Button>
                        )}
                      </Table.Td>
                    </Table.Tr>
                  );
                })}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        )}
      </Card>

      <ConfirmDialog
        open={pendingRevoke !== null}
        onOpenChange={(open) => !open && setPendingRevoke(null)}
        title={t("delegations:confirm.title")}
        description={t("delegations:confirm.description")}
        confirmLabel={t("delegations:confirm.action")}
        isLoading={revokeMutation.isPending}
        onConfirm={() => pendingRevoke && revokeMutation.mutate(pendingRevoke)}
      />
    </Stack>
  );
}
