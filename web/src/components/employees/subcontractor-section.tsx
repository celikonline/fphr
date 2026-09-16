import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button, Card, Group, Select, Text, Title } from "@mantine/core";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { getSubcontractors, linkEmployeeSubcontractor, type EmployeeDetail } from "@/services/employees.service";

const NONE = "__none__";

/**
 * "Alt işveren" (E5b): which subcontractor the employee belongs to. Only rendered for callers who can read the
 * catalog; editing needs employee.employees.update like the rest of the card.
 */
export function SubcontractorSection({ employee }: { employee: EmployeeDetail }) {
  const { t } = useTranslation(["employees", "common"]);
  const canRead = usePermission("employee.employees.read");
  const canUpdate = usePermission("employee.employees.update");
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState(false);
  const [selected, setSelected] = useState<string | null>(employee.subcontractorId ?? null);

  const { data: subcontractors = [] } = useQuery({
    queryKey: ["subcontractors"],
    queryFn: () => getSubcontractors(),
    enabled: canRead && editing,
  });

  const mutation = useMutation({
    mutationFn: () => linkEmployeeSubcontractor(employee.id, selected),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["employees"] });
      toast({ variant: "success", title: t("common:success"), description: t("employees:subcontractor.saved") });
      setEditing(false);
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("employees:genericError")) }),
  });

  if (!canRead) return null;

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" wrap="nowrap" mb="md">
        <Title order={4}>{t("employees:subcontractor.title")}</Title>
        {canUpdate && !editing && (
          <Button
            type="button"
            size="sm"
            variant="subtle"
            onClick={() => {
              setSelected(employee.subcontractorId ?? null);
              setEditing(true);
            }}
          >
            {t("common:edit")}
          </Button>
        )}
      </Group>

      {editing ? (
        <Group align="flex-end" gap="sm">
          <Select
            id="employee-subcontractor"
            label={t("employees:subcontractor.company")}
            description={t("employees:subcontractor.hint")}
            className="flex-1"
            data={[
              { value: NONE, label: t("employees:subcontractor.none") },
              ...subcontractors.map((option) => ({ value: option.id, label: option.name })),
            ]}
            value={selected ?? NONE}
            onChange={(value) => setSelected(value === null || value === NONE ? null : value)}
            allowDeselect={false}
            disabled={mutation.isPending}
            inputWrapperOrder={["label", "input", "description"]}
          />
          <Button type="button" variant="default" onClick={() => setEditing(false)} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button type="button" onClick={() => mutation.mutate()} loading={mutation.isPending}>
            {t("common:save")}
          </Button>
        </Group>
      ) : (
        <Text size="sm">{employee.subcontractorName ?? t("employees:subcontractor.none")}</Text>
      )}
    </Card>
  );
}
