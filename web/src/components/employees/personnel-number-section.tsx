import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { Alert, Button, Card, Stack, Text, Title } from "@mantine/core";
import { usePermission } from "@/components/permission-guard";
import { getApiErrorMessage } from "@/lib/api-error";
import { toEmployeeInput, updateEmployee, type EmployeeDetail } from "@/services/employees.service";
import { acceptsPersonnelNumber } from "@/services/personnel-numbers.service";
import { PersonnelNumberFields, usePersonnelNumberAssignment } from "./personnel-number-fields";

export function PersonnelNumberSection({ employee }: { employee: EmployeeDetail }) {
  const { t } = useTranslation(["employees", "common"]);
  const canUpdate = usePermission("employee.employees.update");
  const [value, setValue] = useState("");
  const client = useQueryClient();
  const assignment = usePersonnelNumberAssignment(
    employee.orgUnitId,
    !employee.sicilNumarasi && canUpdate
  );
  const mutation = useMutation({
    mutationFn: () =>
      updateEmployee(
        employee.id,
        toEmployeeInput(employee, {
          sicilNumarasi: assignment.data?.assignmentMode === "internal" ? null : value.trim(),
        })
      ),
    onSuccess: (updated) => {
      client.setQueryData(["employees", "detail", employee.id], updated);
      void client.invalidateQueries({ queryKey: ["employees"] });
      void client.invalidateQueries({ queryKey: ["personnel-numbers"] });
    },
  });
  const valid =
    assignment.data &&
    !assignment.isFetching &&
    !assignment.isError &&
    (assignment.data.assignmentMode === "internal"
      ? !!assignment.data.nextNumber
      : !!value.trim() && acceptsPersonnelNumber(value.trim(), assignment.data));
  return (
    <Card withBorder padding="lg">
      <Stack>
        <Title order={4}>{t("employees:personnelNumbers.title")}</Title>
        {employee.sicilNumarasi ? (
          <>
            <Text ff="monospace">{employee.sicilNumarasi}</Text>
            <Text size="sm" c="dimmed">
              {t("employees:personnelNumbers.immutable")}
            </Text>
          </>
        ) : canUpdate ? (
          <>
            <PersonnelNumberFields
              assignment={assignment.data}
              loading={assignment.isPending || assignment.isFetching}
              error={assignment.error}
              retry={() => void assignment.refetch()}
              value={value}
              onChange={setValue}
              disabled={mutation.isPending}
            />
            {mutation.error && (
              <Alert color="red">
                {getApiErrorMessage(mutation.error, t("employees:genericError"))}
              </Alert>
            )}
            <Button
              onClick={() => mutation.mutate()}
              disabled={!valid || mutation.isPending}
              loading={mutation.isPending}
            >
              {t("employees:personnelNumbers.assign")}
            </Button>
          </>
        ) : (
          <Text c="dimmed">—</Text>
        )}
        {!!employee.legacyPersonnelNumbers?.length && (
          <>
            <Title order={5}>{t("employees:personnelNumbers.references")}</Title>
            {employee.legacyPersonnelNumbers.map((reference, index) => (
              <Text key={`${reference.sourceSystem}-${index}`}>
                {reference.sourceSystem}: {reference.personnelNumber}
              </Text>
            ))}
          </>
        )}
      </Stack>
    </Card>
  );
}
