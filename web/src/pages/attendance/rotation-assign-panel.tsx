import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Alert, Button, Card, Group, NumberInput, Select, Stack, Text, TextInput, Badge } from "@mantine/core";
import EmployeePicker, { type PickedEmployee } from "@/components/employees/employee-picker";
import { getApiErrorMessage, getApiErrorStatus } from "@/lib/api-error";
import {
  assignRotation,
  listRotations,
  type AssignRotationResult,
} from "@/services/attendance-rotations.service";
import { ROTATIONS_KEY } from "./rotation-patterns-panel";

interface Props {
  canManage: boolean;
}

/** Assigns a pattern to employees; shows locked-month skips from the result and overlap conflicts (409) inline. */
export default function RotationAssignPanel({ canManage }: Props) {
  const { t } = useTranslation(["rotations"]);
  const patterns = useQuery({ queryKey: ROTATIONS_KEY, queryFn: listRotations });
  const [patternId, setPatternId] = useState<string | null>(null);
  const [employees, setEmployees] = useState<PickedEmployee[]>([]);
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate] = useState("");
  const [offsetDays, setOffsetDays] = useState(0);
  const [result, setResult] = useState<AssignRotationResult | null>(null);
  const [conflict, setConflict] = useState<string | null>(null);

  const apply = useMutation({
    mutationFn: () =>
      assignRotation(patternId as string, {
        employeeIds: employees.map((e) => e.id),
        startDate,
        endDate: endDate || undefined,
        offsetDays,
      }),
    onMutate: () => {
      setResult(null);
      setConflict(null);
    },
    onSuccess: setResult,
    onError: (error) => setConflict(getApiErrorMessage(error, t("rotations:genericError"))),
  });
  const isOverlap = apply.isError && getApiErrorStatus(apply.error) === 409;

  return (
    <Card withBorder padding="lg" radius="md">
      <Stack gap="sm">
        <Select
          label={t("rotations:pattern")}
          data={(patterns.data ?? []).filter((p) => p.isActive).map((p) => ({ value: p.id, label: `${p.code} - ${p.name}` }))}
          value={patternId}
          onChange={setPatternId}
        />
        <div>
          <Text size="sm" fw={500} mb={4}>
            {t("rotations:employees")}
          </Text>
          <Group gap="xs" mb="xs">
            {employees.map((e) => (
              <Badge
                key={e.id}
                variant="light"
                style={{ cursor: "pointer" }}
                onClick={() => setEmployees((c) => c.filter((x) => x.id !== e.id))}
              >
                {e.displayName}
              </Badge>
            ))}
          </Group>
          <EmployeePicker
            value={null}
            placeholder={t("rotations:pickEmployee")}
            onChange={(v) => v && setEmployees((c) => (c.some((x) => x.id === v.id) ? c : [...c, v]))}
          />
        </div>
        <Group grow align="flex-start">
          <TextInput type="date" label={t("rotations:startDate")} value={startDate} onChange={(e) => setStartDate(e.currentTarget.value)} required />
          <TextInput type="date" label={t("rotations:endDate")} value={endDate} onChange={(e) => setEndDate(e.currentTarget.value)} />
          <NumberInput label={t("rotations:offsetDays")} value={offsetDays} min={0} allowDecimal={false} onChange={(v) => setOffsetDays(Number(v) || 0)} />
        </Group>
        {conflict && (
          <Alert color="red" title={isOverlap ? t("rotations:overlapTitle") : undefined} role="alert">
            {conflict}
          </Alert>
        )}
        {result && (
          <Stack gap="xs">
            <Text size="sm">{t("rotations:result", { count: result.createdCount })}</Text>
            {result.skippedLockedMonths.length > 0 && (
              <Alert color="yellow" title={t("rotations:skippedTitle")} data-testid="rotation-skipped">
                <ul>
                  {result.skippedLockedMonths.map((month) => (
                    <li key={month}>{t("rotations:skippedMonth", { month })}</li>
                  ))}
                </ul>
              </Alert>
            )}
          </Stack>
        )}
        <Group>
          <Button
            onClick={() => apply.mutate()}
            loading={apply.isPending}
            disabled={!canManage || !patternId || employees.length === 0 || !startDate}
          >
            {t("rotations:apply")}
          </Button>
        </Group>
      </Stack>
    </Card>
  );
}
