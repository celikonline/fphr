import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Badge, Button, Card, Group, Select, Stack, Text, Title } from "@mantine/core";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { assignEmployeeCostCenter, type EmployeeDetail } from "@/services/employees.service";
import { getCostCenters } from "@/services/organization.service";

const NONE = "__none__";

export function CostCenterSection({ employee }: { employee: EmployeeDetail }) {
  const { t } = useTranslation(["employees", "common", "settings"]);
  const canUpdate = usePermission("employee.employees.update");
  const canViewCatalog = usePermission("organization.cost_centers.view");
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState(false);
  const [selected, setSelected] = useState<string | null>(employee.costCenterId ?? null);
  const centers = useQuery({
    queryKey: ["organization", "cost-centers", { includeInactive: false }],
    queryFn: () => getCostCenters(),
    enabled: editing && canViewCatalog,
  });
  const mutation = useMutation({
    mutationFn: () => assignEmployeeCostCenter(employee.id, selected),
    onSuccess: (updated) => {
      queryClient.setQueryData(["employees", "detail", employee.id], updated);
      void queryClient.invalidateQueries({ queryKey: ["employees"] });
      void queryClient.invalidateQueries({ queryKey: ["organization", "employee-cost-center", employee.id] });
      void queryClient.invalidateQueries({ queryKey: ["payroll", "periods"] });
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("employees:costCenter.saved"),
      });
      setEditing(false);
    },
  });
  const label = employee.costCenterId
    ? [employee.costCenterCode, employee.costCenterName].filter(Boolean).join(" — ") ||
      employee.costCenterId
    : t("employees:costCenter.none");
  const options = [
    { value: NONE, label: t("employees:costCenter.none") },
    ...(centers.data ?? [])
      .filter((center) => center.isActive)
      .map((center) => ({ value: center.id, label: `${center.code} — ${center.name}` })),
  ];
  // Preserve and display an assigned center that has since been deactivated; it is never offered for a new assignment.
  if (employee.costCenterId && !options.some((option) => option.value === employee.costCenterId)) {
    options.push({
      value: employee.costCenterId,
      label:
        employee.costCenterIsActive === false
          ? `${label} (${t("settings:costCenters.inactive")})`
          : label,
    });
  }
  const busy = centers.isPending || centers.isError || mutation.isPending;

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" mb="md">
        <Title order={4}>{t("employees:costCenter.title")}</Title>
        {canUpdate && canViewCatalog && !editing && (
          <Button
            type="button"
            variant="subtle"
            size="sm"
            onClick={() => {
              setSelected(employee.costCenterId ?? null);
              mutation.reset();
              setEditing(true);
            }}
          >
            {t("common:edit")}
          </Button>
        )}
      </Group>
      {editing ? (
        <Stack gap="sm">
          <Select
            label={t("employees:costCenter.title")}
            description={t("employees:costCenter.hint")}
            data={options}
            value={selected ?? NONE}
            onChange={(value) => {
              setSelected(value === NONE ? null : value);
              mutation.reset();
            }}
            allowDeselect={false}
            searchable
            disabled={busy}
          />
          {centers.isError && (
            <Alert color="red">
              {t("employees:costCenter.loadError")}
              <Button
                type="button"
                variant="subtle"
                size="xs"
                onClick={() => void centers.refetch()}
              >
                {t("employees:costCenter.retry")}
              </Button>
            </Alert>
          )}
          {mutation.isError && (
            <Alert color="red">
              {getApiErrorMessage(mutation.error, t("employees:genericError"))}
            </Alert>
          )}
          <Group justify="flex-end">
            <Button
              type="button"
              variant="default"
              disabled={mutation.isPending}
              onClick={() => setEditing(false)}
            >
              {t("common:cancel")}
            </Button>
            <Button
              type="button"
              disabled={busy || selected === (employee.costCenterId ?? null)}
              loading={mutation.isPending}
              onClick={() => mutation.mutate()}
            >
              {t("common:save")}
            </Button>
          </Group>
        </Stack>
      ) : (
        <Group gap="xs">
          <Text size="sm">{label}</Text>
          {employee.costCenterId && employee.costCenterIsActive === false && (
            <Badge color="gray">{t("settings:costCenters.inactive")}</Badge>
          )}
        </Group>
      )}
    </Card>
  );
}
