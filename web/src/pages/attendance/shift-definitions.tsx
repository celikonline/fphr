import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Badge, Button, Card, ColorSwatch, Group, Table, Text, Title } from "@mantine/core";
import { Clock, Plus } from "lucide-react";
import NoAccess from "@/components/no-access";
import { useAnyPermission, usePermission } from "@/components/permission-guard";
import ShiftDefinitionForm, {
  emptyShiftDefinition,
} from "@/components/attendance/shift-definition-form";
import { formatMinute } from "@/components/attendance/shift-timeline";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  listShiftDefinitions,
  saveShiftDefinition,
  setShiftDefinitionActive,
  type ShiftDefinition,
  type ShiftDefinitionSaveResult,
} from "@/services/attendance-shift-definitions.service";

const QUERY_KEY = ["attendance", "shift-definitions"];

/** Vardiya tanımları kataloğu: liste + düzenleyici (Sabit / Esnek / Dinamik). */
export default function ShiftDefinitionsPage() {
  const { t } = useTranslation(["shifts"]);
  const canView = useAnyPermission([
    "attendance.shiftdefinition.view",
    "attendance.shiftdefinition.manage",
  ]);
  const canManage = usePermission("attendance.shiftdefinition.manage");
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<ShiftDefinition | null>(null);

  const query = useQuery({
    queryKey: QUERY_KEY,
    queryFn: () => listShiftDefinitions({ includeInactive: true }),
    enabled: canView,
  });
  const [lastResult, setLastResult] = useState<ShiftDefinitionSaveResult | null>(null);
  const save = useMutation({
    mutationFn: async (args: { dto: ShiftDefinition; applyToPast: boolean }) => {
      const result = await saveShiftDefinition(args.dto, { applyToPast: args.applyToPast });
      const wasActive = args.dto.id
        ? (query.data?.find((d) => d.id === args.dto.id)?.isActive ?? true)
        : true;
      if (args.dto.isActive !== wasActive)
        await setShiftDefinitionActive(result.id, args.dto.isActive);
      return result;
    },
    onSuccess: async (result, args) => {
      toast({ variant: "success", description: t("shifts:saved") });
      // recalculationJobId is kept for the B3 status endpoint (GET v1/attendance/recalculation/{jobId}).
      setLastResult(args.applyToPast ? result : null);
      setEditing(null);
      await queryClient.invalidateQueries({ queryKey: QUERY_KEY });
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        description: getApiErrorMessage(error, t("shifts:saveFailed")),
      }),
  });

  if (!canView) return <NoAccess />;

  return (
    <div className="max-w-5xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Clock size={24} /> {t("shifts:title")}
          </Title>
          <Text c="dimmed" size="sm" mt={4}>
            {t("shifts:subtitle")}
          </Text>
        </div>
        {canManage && !editing && (
          <Button
            leftSection={<Plus size={16} />}
            onClick={() => setEditing(emptyShiftDefinition())}
          >
            {t("shifts:new")}
          </Button>
        )}
      </Group>

      {lastResult && !editing && (
        <Alert color="blue" withCloseButton onClose={() => setLastResult(null)}>
          {t("shifts:recalculating", { count: lastResult.affectedAssignmentCount })}
        </Alert>
      )}

      {editing ? (
        <Card withBorder padding="lg" radius="md">
          <ShiftDefinitionForm
            key={editing.id ?? "new"}
            initial={editing}
            saving={save.isPending}
            readOnly={!canManage}
            onSubmit={(dto, applyToPast) => save.mutate({ dto, applyToPast })}
            onCancel={() => setEditing(null)}
          />
        </Card>
      ) : (
        <Card withBorder padding={0} radius="md">
          <Table highlightOnHover={canManage}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("shifts:form.code")}</Table.Th>
                <Table.Th>{t("shifts:form.name")}</Table.Th>
                <Table.Th>{t("shifts:form.type")}</Table.Th>
                <Table.Th>{t("shifts:list.hours")}</Table.Th>
                <Table.Th>{t("shifts:list.status")}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {(query.data ?? []).map((d) => (
                <Table.Tr
                  key={d.id}
                  className={canManage ? "cursor-pointer" : undefined}
                  onClick={() => canManage && setEditing(d)}
                >
                  <Table.Td>
                    <Group gap="xs">
                      <ColorSwatch color={d.colorHex} size={14} />
                      {d.code}
                    </Group>
                  </Table.Td>
                  <Table.Td>{d.name}</Table.Td>
                  <Table.Td>{t(`shifts:type.${d.type}`)}</Table.Td>
                  <Table.Td>
                    {d.type === "flexible"
                      ? t("shifts:list.flexHours", { minutes: d.durationMinutes ?? 0 })
                      : `${formatMinute(d.startMinute)} – ${formatMinute(d.endMinute)}`}
                  </Table.Td>
                  <Table.Td>
                    <Badge color={d.isActive ? "green" : "gray"} variant="light">
                      {t(d.isActive ? "shifts:list.active" : "shifts:list.inactive")}
                    </Badge>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
          {query.isSuccess && query.data.length === 0 && (
            <Text c="dimmed" size="sm" ta="center" p="lg">
              {t("shifts:list.empty")}
            </Text>
          )}
        </Card>
      )}
    </div>
  );
}
