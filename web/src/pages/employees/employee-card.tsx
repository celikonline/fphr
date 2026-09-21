import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, Navigate, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ActionIcon, Button, Card, Group, Menu, Skeleton, Stack, Tabs, Text, Title, Tooltip } from "@mantine/core";
import { ArrowLeft, Lock, LogOut, Mail, MoreVertical, PauseCircle, PlayCircle, SearchX, UserPlus } from "lucide-react";
import ConfirmDialog from "@/components/confirm-dialog";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiErrorStatus } from "@/lib/api-error";
import { getEmployee, getMyEmployee, type EmployeeDetail } from "@/services/employees.service";
import { useAuthStore } from "@/store/auth.store";
import { inviteEmployee } from "@/services/invitations.service";
import { AccountStatusBadge, EmployeeStatusBadge } from "@/components/employees/employee-badges";
import { EmployeeAvatar } from "@/components/employees/employee-avatar";
import { useVisibleEmployeeCardTabs } from "@/components/employees/card-tabs";
import { useOrgUnits } from "@/components/employees/use-org-units";
import { EmployeeLifecycleDialogs, lifecycleActionsFor, type LifecycleAction } from "@/components/employees/lifecycle-actions";

function LifecycleIcon({ action }: { action: LifecycleAction }) {
  const size = 16;
  if (action === "suspend") return <PauseCircle size={size} />;
  if (action === "reactivate") return <PlayCircle size={size} />;
  if (action === "terminate") return <LogOut size={size} />;
  return <UserPlus size={size} />;
}

export default function EmployeeCardPage() {
  const { id = "", tab } = useParams();
  const { t } = useTranslation(["employees", "common"]);
  const navigate = useNavigate();
  const tabs = useVisibleEmployeeCardTabs();
  const active = tabs.find((candidate) => candidate.key === tab);

  const canReadEmployees = usePermission("employee.employees.read");
  const ownEmployeeId = useAuthStore((state) => state.user?.employeeId);
  // Employee-role users can't read arbitrary cards, but may open their own via /employees/me.
  const isOwnCard = !canReadEmployees && Boolean(ownEmployeeId) && id === ownEmployeeId;

  const { data: employee, isLoading, error } = useQuery({
    queryKey: ["employees", "detail", id],
    queryFn: () => (isOwnCard ? getMyEmployee() : getEmployee(id)),
    enabled: Boolean(id),
    retry: (count, err) => {
      const status = getApiErrorStatus(err);
      return status !== 404 && status !== 403 && count < 1;
    },
  });

  if (isLoading) {
    return (
      <div className="max-w-5xl space-y-4 p-6 md:p-10">
        <Skeleton h={80} />
        <Skeleton h={256} />
      </div>
    );
  }

  if (error || !employee) {
    const status = getApiErrorStatus(error);
    const forbidden = status === 403;
    const notFound = status === 404 || (!error && !employee);
    const iconColor = "var(--mantine-color-dimmed)";
    return (
      <div className="max-w-3xl p-6 md:p-10">
        <Card withBorder padding="xl">
          <Stack align="center" gap="sm" ta="center">
            {forbidden ? <Lock size={32} color={iconColor} /> : <SearchX size={32} color={iconColor} />}
            <Text fw={500}>{forbidden ? t("common:noAccessTitle") : notFound ? t("employees:card.notFoundTitle") : t("common:error")}</Text>
            <Text size="sm" c="dimmed">
              {forbidden
                ? t("common:noAccessDescription")
                : notFound
                  ? t("employees:card.notFoundDescription")
                  : getApiErrorMessage(error, t("employees:genericError"))}
            </Text>
            <Button component={Link} to="/app/employees" variant="default">
              {t("employees:card.backToList")}
            </Button>
          </Stack>
        </Card>
      </div>
    );
  }

  // Unknown or hidden tab (and the bare /employees/:id URL) lands on the first visible tab.
  if (!active) {
    return <Navigate to={`/app/employees/${id}/${tabs[0].key}`} replace />;
  }

  const ActiveTab = active.component;

  return (
    <div className="max-w-5xl space-y-6 p-6 md:p-10">
      <Button
        component={Link}
        to="/app/employees"
        variant="subtle"
        size="sm"
        className="-ml-3"
        leftSection={<ArrowLeft size={16} />}
      >
        {t("employees:card.backToList")}
      </Button>
      <CardHeaderBlock key={`header-${employee.id}`} employee={employee} />
      <Tabs value={active.key} onChange={(value) => value && navigate(`/app/employees/${id}/${value}`)}>
        <Tabs.List>
          {tabs.map((candidate) => (
            <Tabs.Tab key={candidate.key} value={candidate.key}>
              {t(candidate.labelKey)}
            </Tabs.Tab>
          ))}
        </Tabs.List>
      </Tabs>
      <ActiveTab key={`${active.key}-${employee.id}-${employee.accessType ?? ""}`} employee={employee} />
    </div>
  );
}

function CardHeaderBlock({ employee }: { employee: EmployeeDetail }) {
  const { t } = useTranslation(["employees", "common"]);
  const queryClient = useQueryClient();
  const { nameById } = useOrgUnits();
  const canInvite = usePermission("identity.users.invite");
  const canUpdate = usePermission("employee.employees.update");
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [lifecycleAction, setLifecycleAction] = useState<LifecycleAction | null>(null);
  const lifecycleActions = canUpdate ? lifecycleActionsFor(employee.status) : [];

  const targetEmail = employee.workEmail ?? employee.personalEmail;
  const targetSourceKey = employee.workEmail ? "work" : "personal";
  const showInvite = canInvite && employee.accountStatus !== "active";
  const inviteLabel = employee.accountStatus === "invited" ? t("employees:invite.resend") : t("employees:invite.send");

  const mutation = useMutation({
    mutationFn: () => inviteEmployee(employee.id),
    onSuccess: (result) => {
      setConfirmOpen(false);
      queryClient.invalidateQueries({ queryKey: ["employees"] });
      // Tolerates both "AlreadyActive" and camelCase enum serialization.
      const outcome = String(result.outcome).toLowerCase();
      toast({
        variant: outcome === "alreadyactive" ? "default" : "success",
        title: t(`employees:invite.outcome.${outcome}`, { defaultValue: String(result.outcome) }),
        description: result.sentToEmail ? t("employees:invite.sentTo", { email: result.sentToEmail }) : undefined,
      });
    },
    onError: (error) => {
      setConfirmOpen(false);
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("employees:genericError")) });
    },
  });

  const unitName = employee.orgUnitId ? nameById.get(employee.orgUnitId) : undefined;

  return (
    <Card withBorder padding="lg">
      <Group gap="md" wrap="nowrap">
        <EmployeeAvatar employeeId={employee.id} firstName={employee.firstName} lastName={employee.lastName} size={64} radius="xl" />
        <div className="min-w-0 flex-1">
          <Title order={1} size="h2" lineClamp={1}>
            {employee.displayName}
          </Title>
          <Group gap="xs" mt={4}>
            {unitName && (
              <Text size="sm" c="dimmed">
                {unitName}
              </Text>
            )}
            <EmployeeStatusBadge status={employee.status} label={t("employees:card.statusLabel")} />
            <AccountStatusBadge status={employee.accountStatus} label={t("employees:card.accountStatusLabel")} />
          </Group>
        </div>
        {(showInvite || lifecycleActions.length > 0) && (
          <Menu position="bottom-end" width={256}>
            <Tooltip label={t("common:actions")}>
              <Menu.Target>
                <ActionIcon type="button" variant="subtle" color="gray" size="lg" aria-label={t("common:actions")}>
                  <MoreVertical size={20} />
                </ActionIcon>
              </Menu.Target>
            </Tooltip>
            <Menu.Dropdown>
              <Menu.Label>{t("common:actions")}</Menu.Label>
              {showInvite && (
                <Menu.Item disabled={!targetEmail} onClick={() => setConfirmOpen(true)} leftSection={<Mail size={16} />}>
                  {inviteLabel}
                </Menu.Item>
              )}
              {showInvite && !targetEmail && (
                <Text size="xs" c="dimmed" px="sm" pb="xs">
                  {t("employees:invite.emailRequiredHint")}
                </Text>
              )}
              {lifecycleActions.map((action) => (
                <Menu.Item
                  key={action}
                  color={action === "terminate" ? "red" : undefined}
                  leftSection={<LifecycleIcon action={action} />}
                  onClick={() => setLifecycleAction(action)}
                >
                  {t(`employees:lifecycle.action.${action}`)}
                </Menu.Item>
              ))}
            </Menu.Dropdown>
          </Menu>
        )}
      </Group>

      <EmployeeLifecycleDialogs employee={employee} action={lifecycleAction} onClose={() => setLifecycleAction(null)} />

      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        title={inviteLabel}
        description={t(`employees:invite.confirm.${targetSourceKey}`, { email: targetEmail ?? "", name: employee.displayName })}
        confirmLabel={t("common:yes")}
        confirmVariant="default"
        isLoading={mutation.isPending}
        onConfirm={() => mutation.mutate()}
      />
    </Card>
  );
}
