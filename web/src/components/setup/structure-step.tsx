import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Badge, Group, Stack, Text, Textarea, Title } from "@mantine/core";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  COMPANY_TYPE_CODE,
  createUnit,
  listUnitTypes,
  listUnits,
  type OrgUnit,
  type OrgUnitType,
} from "@/services/structure.service";
import { StepActions, type SetupStepProps } from "./step-actions";

const DEPARTMENT_TYPE_CODE = "Department";

/** Step 3 - the company's departments, created under its root Company unit. */
export default function StructureStep(props: SetupStepProps) {
  const { t } = useTranslation(["setup", "common"]);
  const loaded = useQuery({
    queryKey: ["setup", "structure"],
    queryFn: async () => {
      const [types, units] = await Promise.all([listUnitTypes(true), listUnits(true)]);
      const companyType = types.find((type) => type.code === COMPANY_TYPE_CODE);
      const departmentType = types.find((type) => type.code === DEPARTMENT_TYPE_CODE);
      const root = units.find((unit) => unit.parentId === null && unit.typeId === companyType?.id);
      const departments = units.filter((unit) => unit.typeId === departmentType?.id);
      return { departmentType, root, departments };
    },
  });

  if (loaded.isError) {
    return (
      <Text c="red" role="alert">
        {t("setup:errors.load")}
      </Text>
    );
  }
  if (!loaded.data) {
    return <Text c="dimmed">{t("common:loading")}</Text>;
  }
  return <StructureForm {...loaded.data} {...props} />;
}

function StructureForm({
  departmentType,
  root,
  departments,
  onContinue,
  onBack,
}: SetupStepProps & {
  departmentType: OrgUnitType | undefined;
  root: OrgUnit | undefined;
  departments: OrgUnit[];
}) {
  const { t } = useTranslation(["setup", "common"]);
  const queryClient = useQueryClient();
  const [lines, setLines] = useState("");
  const [error, setError] = useState<string | null>(null);

  const canCreate = Boolean(departmentType && root);

  const mutation = useMutation({
    mutationFn: async () => {
      if (!departmentType || !root) return;
      const existing = new Set(
        departments.map((department) => department.name.toLocaleLowerCase())
      );
      const names = lines
        .split("\n")
        .map((line) => line.trim())
        .filter((name) => name.length > 0 && !existing.has(name.toLocaleLowerCase()));

      // Sequential on purpose: keeps creation order (sortOrder) and stops at the first failure.
      for (const name of new Set(names)) {
        await createUnit({
          typeId: departmentType.id,
          parentId: root.id,
          name,
          managerEmployeeId: null,
          locationId: null,
        });
      }
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["setup", "structure"] });
      void queryClient.invalidateQueries({ queryKey: ["organization", "units"] });
      onContinue();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("setup:errors.generic"))),
  });

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    mutation.mutate();
  }

  return (
    <form onSubmit={handleSubmit}>
      <Stack gap="md">
        <div>
          <Title order={3} size="h4">
            {t("setup:structure.heading")}
          </Title>
          <Text size="sm" c="dimmed">
            {t("setup:structure.description")}
          </Text>
        </div>

        {departments.length > 0 && (
          <div>
            <Text size="sm" fw={500} mb={4}>
              {t("setup:structure.existing")}
            </Text>
            <Group gap="xs">
              {departments.map((department) => (
                <Badge key={department.id} variant="light">
                  {department.name}
                </Badge>
              ))}
            </Group>
          </div>
        )}

        {canCreate ? (
          <Textarea
            label={t("setup:structure.departmentsLabel")}
            description={t("setup:structure.departmentsHint")}
            placeholder={t("setup:structure.departmentsPlaceholder")}
            rows={5}
            value={lines}
            onChange={(event) => setLines(event.currentTarget.value)}
            disabled={mutation.isPending}
            inputWrapperOrder={["label", "input", "description"]}
          />
        ) : (
          <Text size="sm" c="dimmed">
            {t("setup:structure.noDepartmentType")}
          </Text>
        )}

        <StepActions
          onBack={onBack}
          onSkip={onContinue}
          isSubmitting={mutation.isPending}
          error={error}
        />
      </Stack>
    </form>
  );
}
