import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Workflow } from "lucide-react";
import { Alert, Badge, Button, Card, Group, Input, Loader, Stack, Table, Text, Title } from "@mantine/core";
import NoAccess from "@/components/no-access";
import { useAnyPermission } from "@/components/permission-guard";
import EmployeePicker, { type PickedEmployee } from "@/components/employees/employee-picker";
import { DatePicker } from "@/components/ui/date-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly, toDateOnly } from "@/lib/dates";
import { useAuthStore } from "@/store/auth.store";
import { getEmployee } from "@/services/employees.service";
import {
  createDelegation,
  type CreateDelegationInput,
  listDelegations,
  revokeDelegation,
  type Delegation,
} from "@/services/workflow-delegations.service";

const QUERY_KEY = ["workflow", "delegations"];

type DelegationStatus = "active" | "scheduled" | "expired";
type FormError = "endBeforeStart" | "self" | "required" | "noAccount";

/** Status by calendar date ("yyyy-MM-dd" compares lexically): a delegation is active only while today is inside [from, to]. */
export function delegationStatus(d: Pick<Delegation, "from" | "to">, today: string): DelegationStatus {
  if (today < d.from) return "scheduled";
  if (today > d.to) return "expired";
  return "active";
}

const STATUS_COLOR: Record<DelegationStatus, string> = { active: "green", scheduled: "blue", expired: "gray" };

export default function DelegationsPage() {
  const { t, i18n } = useTranslation(["delegations", "common"]);
  const allowed = useAnyPermission(["workflow.delegation.manage", "workflow.my.delegation"]);
  const me = useAuthStore((s) => s.user);
  const queryClient = useQueryClient();

  const [delegate, setDelegate] = useState<PickedEmployee | null>(null);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [formError, setFormError] = useState<FormError | null>(null);

  const listQuery = useQuery({ queryKey: QUERY_KEY, queryFn: listDelegations, enabled: allowed });

  const onError = (error: unknown) =>
    toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("delegations:errors.generic")) });

  const createMutation = useMutation({
    mutationFn: (dto: CreateDelegationInput) => createDelegation(dto),
    onSuccess: () => {
      toast({ variant: "success", title: t("common:success"), description: t("delegations:toast.created") });
      setDelegate(null);
      setFrom("");
      setTo("");
      void queryClient.invalidateQueries({ queryKey: QUERY_KEY });
    },
    onError,
  });

  const revokeMutation = useMutation({
    mutationFn: (id: string) => revokeDelegation(id),
    onSuccess: () => {
      toast({ variant: "success", title: t("common:success"), description: t("delegations:toast.revoked") });
      void queryClient.invalidateQueries({ queryKey: QUERY_KEY });
    },
    onError,
  });

  if (!allowed) return <NoAccess />;

  async function submit() {
    setFormError(null);
    if (!delegate || !from || !to) return setFormError("required");
    if (to < from) return setFormError("endBeforeStart");
    if (delegate.id === me?.employeeId) return setFormError("self");
    let delegateUserId: string | null;
    try {
      delegateUserId = (await getEmployee(delegate.id)).userId;
    } catch (error) {
      return onError(error);
    }
    if (!delegateUserId) return setFormError("noAccount");
    if (delegateUserId === me?.id) return setFormError("self");
    createMutation.mutate({ delegateUserId, from, to });
  }

  const today = toDateOnly(new Date());
  const items = listQuery.data ?? [];
  const name = (userId: string, display?: string) =>
    display ?? (userId === me?.id ? me.name : t("delegations:list.unknown"));

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
      <Alert variant="light" color="blue">
        {t("delegations:info")}
      </Alert>

      <Card withBorder padding="lg" radius="md">
        <Stack gap="md">
          <Title order={2} size="h4">
            {t("delegations:form.title")}
          </Title>
          <EmployeePicker
            label={t("delegations:form.delegate")}
            placeholder={t("delegations:form.delegatePlaceholder")}
            value={delegate}
            onChange={setDelegate}
            excludeId={me?.employeeId ?? undefined}
            allowClear
            withAsterisk
          />
          <Group align="flex-start" grow>
            <Input.Wrapper label={t("delegations:form.from")} withAsterisk>
              <DatePicker id="delegation-from" value={from} onChange={setFrom} />
            </Input.Wrapper>
            <Input.Wrapper label={t("delegations:form.to")} withAsterisk>
              <DatePicker id="delegation-to" value={to} onChange={setTo} min={from || undefined} />
            </Input.Wrapper>
          </Group>
          {formError && (
            <Text c="red" size="sm" role="alert">
              {t(`delegations:errors.${formError}`)}
            </Text>
          )}
          <Group>
            <Button onClick={() => void submit()} loading={createMutation.isPending}>
              {t("delegations:form.submit")}
            </Button>
          </Group>
        </Stack>
      </Card>

      <Card withBorder padding="lg" radius="md">
        <Title order={2} size="h4" mb="md">
          {t("delegations:list.title")}
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
                      <Table.Td>{name(d.delegatorUserId, d.delegatorName)}</Table.Td>
                      <Table.Td>{name(d.delegateUserId, d.delegateName)}</Table.Td>
                      <Table.Td>{formatDateOnly(d.from, i18n.language)}</Table.Td>
                      <Table.Td>{formatDateOnly(d.to, i18n.language)}</Table.Td>
                      <Table.Td>
                        <Badge color={STATUS_COLOR[status]} variant="light">
                          {t(`delegations:status.${status}`)}
                        </Badge>
                      </Table.Td>
                      <Table.Td>
                        {status !== "expired" && (
                          <Button
                            size="compact-sm"
                            variant="light"
                            color="red"
                            loading={revokeMutation.isPending && revokeMutation.variables === d.id}
                            onClick={() => revokeMutation.mutate(d.id)}
                          >
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
    </div>
  );
}
