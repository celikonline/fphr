import { Alert, Button, Stack, Text, TextInput } from "@mantine/core";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  getPersonnelNumberAssignment,
  type PersonnelNumberAssignment,
} from "@/services/personnel-numbers.service";

export function usePersonnelNumberAssignment(orgUnitId: string | null, enabled = true) {
  return useQuery({
    queryKey: ["personnel-numbers", "assignment", orgUnitId],
    queryFn: () => getPersonnelNumberAssignment(orgUnitId),
    enabled,
    retry: false,
  });
}

export function PersonnelNumberFields({
  assignment,
  loading,
  error,
  retry,
  value,
  onChange,
  disabled,
  fieldError,
}: {
  assignment?: PersonnelNumberAssignment;
  loading: boolean;
  error: unknown;
  retry: () => void;
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  fieldError?: string;
}) {
  const { t } = useTranslation(["employees", "common"]);
  if (loading) return <Text role="status">{t("common:loading")}</Text>;
  if (error)
    return (
      <Alert color="red">
        <Stack gap="xs">
          <Text>{getApiErrorMessage(error, t("employees:personnelNumbers.loadError"))}</Text>
          <Button variant="light" onClick={retry}>
            {t("employees:personnelNumbers.retry")}
          </Button>
        </Stack>
      </Alert>
    );
  if (!assignment) return null;
  const internal = assignment.configured && assignment.assignmentMode === "internal";
  return (
    <Stack gap="xs">
      {!assignment.configured && (
        <Alert color="yellow">{t("employees:personnelNumbers.unconfigured")}</Alert>
      )}
      {assignment.configured && (
        <Text size="sm">
          {assignment.companyName}{" "}
          {assignment.personnelAreaName ? ` / ${assignment.personnelAreaName}` : ""} ·{" "}
          {assignment.rangeCode} · {assignment.startNumber}–{assignment.endNumber}
        </Text>
      )}
      {internal ? (
        <Alert color={assignment.nextNumber ? "blue" : "red"}>
          {t(
            assignment.nextNumber
              ? "employees:personnelNumbers.automatic"
              : "employees:personnelNumbers.exhausted",
            { next: assignment.nextNumber }
          )}
        </Alert>
      ) : (
        <TextInput
          id="employee-personnel-number"
          label={t("employees:personnelNumbers.number")}
          value={value}
          onChange={(e) => onChange(e.currentTarget.value)}
          disabled={disabled}
          maxLength={assignment.configured ? 8 : 30}
          inputMode={assignment.configured ? "numeric" : "text"}
          withAsterisk={assignment.configured}
          error={fieldError}
        />
      )}
    </Stack>
  );
}
