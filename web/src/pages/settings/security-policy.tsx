import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Info, Lock } from "lucide-react";
import { Alert, Button, Card, Group, Skeleton, Stack, Switch, Text, Title } from "@mantine/core";
import { useAnyPermission, usePermission } from "@/components/permission-guard";
import NoAccess from "@/components/no-access";
import { toast } from "@/hooks/use-toast";
import { useAuthStore } from "@/store/auth.store";
import { getApiErrorMessage } from "@/lib/api-error";
import { getSecurityPolicy, updateSecurityPolicy } from "@/services/security-policy.service";
import { listRoles } from "@/services/users.service";
import { SETTINGS_PERMISSIONS } from "@/config/navigation";
import type { SecurityPolicy } from "@/types";

const POLICY_QUERY_KEY = ["identity", "security-policy"];
const ROLES_QUERY_KEY = ["identity", "roles"];

function isDirty(draft: SecurityPolicy | null, saved: SecurityPolicy | undefined): boolean {
  if (!draft || !saved) return false;
  if (draft.requireMfaForRoles.length !== saved.requireMfaForRoles.length) return true;
  const savedSet = new Set(saved.requireMfaForRoles);
  return draft.requireMfaForRoles.some((code) => !savedSet.has(code));
}

export default function SecurityPolicyPage() {
  const canView = useAnyPermission([...SETTINGS_PERMISSIONS.securityPolicy]);
  if (!canView) return <NoAccess />;
  return <SecurityPolicyForm />;
}

function SecurityPolicyForm() {
  const { t } = useTranslation(["settings", "account", "common"]);
  const queryClient = useQueryClient();
  const canManage = usePermission("identity.security.manage");
  const refreshMe = useAuthStore((state) => state.refreshMe);

  const policyQuery = useQuery({ queryKey: POLICY_QUERY_KEY, queryFn: getSecurityPolicy });
  const rolesQuery = useQuery({ queryKey: ROLES_QUERY_KEY, queryFn: listRoles });

  // Local edits only; null = untouched, so the form always reflects the latest server values until edited.
  const [edits, setEdits] = useState<SecurityPolicy | null>(null);
  const draft = edits ?? policyQuery.data ?? null;
  const dirty = isDirty(draft, policyQuery.data);
  const requiredSet = new Set(draft?.requireMfaForRoles ?? []);

  const saveMutation = useMutation({
    mutationFn: updateSecurityPolicy,
    onSuccess: async (_, saved) => {
      queryClient.setQueryData(POLICY_QUERY_KEY, saved);
      setEdits(null);
      await refreshMe();
      toast({ variant: "success", title: t("common:success"), description: t("settings:securityPolicy.saveSuccess") });
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("settings:securityPolicy.saveError")),
      }),
  });

  function toggleRole(code: string, required: boolean) {
    if (!draft) return;
    const next = new Set(draft.requireMfaForRoles);
    if (required) {
      next.add(code);
    } else {
      next.delete(code);
    }
    setEdits({ requireMfaForRoles: Array.from(next) });
  }

  const isLoading = policyQuery.isLoading || rolesQuery.isLoading;
  const isError = policyQuery.isError || rolesQuery.isError;

  return (
    <div className="max-w-4xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Lock className="h-6 w-6" /> {t("settings:securityPolicy.title")}
          </Title>
          <Text c="dimmed">{t("settings:securityPolicy.subtitle")}</Text>
        </div>
        {canManage && (
          <Group gap="xs">
            {dirty && (
              <Button type="button" variant="default" onClick={() => setEdits(null)} disabled={saveMutation.isPending}>
                {t("common:cancel")}
              </Button>
            )}
            <Button
              type="button"
              onClick={() => draft && saveMutation.mutate(draft)}
              disabled={!dirty || saveMutation.isPending}
            >
              {saveMutation.isPending ? t("common:saving") : t("common:save")}
            </Button>
          </Group>
        )}
      </Group>

      <Alert variant="light" color="gray" icon={<Info className="h-4 w-4" />}>
        {t("settings:securityPolicy.notice")}
      </Alert>

      {!canManage && (
        <Alert variant="light" color="yellow">
          {t("settings:securityPolicy.readOnly")}
        </Alert>
      )}

      {isLoading ? (
        <Skeleton h={280} />
      ) : isError || !draft || !rolesQuery.data ? (
        <Alert variant="light" color="red">
          {t("settings:securityPolicy.loadError")}
        </Alert>
      ) : (
        <Card withBorder padding="lg">
          <Title order={2} size="h5" mb="md">
            {t("settings:securityPolicy.rolesTitle")}
          </Title>
          <Stack gap="md">
            {rolesQuery.data.map((role) => (
              <Group key={role.id} justify="space-between" align="center" gap="xl" wrap="nowrap">
                <Text component="label" htmlFor={`mfa-role-${role.code}`} size="sm" fw={500}>
                  {t(`account:roleNames.${role.code}`, role.name ?? role.code)}
                </Text>
                <Switch
                  id={`mfa-role-${role.code}`}
                  checked={requiredSet.has(role.code)}
                  onChange={(e) => toggleRole(role.code, e.currentTarget.checked)}
                  disabled={!canManage || saveMutation.isPending}
                />
              </Group>
            ))}
          </Stack>
        </Card>
      )}
    </div>
  );
}
