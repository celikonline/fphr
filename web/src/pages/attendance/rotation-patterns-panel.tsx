import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button, Card, Group, NumberInput, SegmentedControl, Select, Stack, Table, Text, TextInput } from "@mantine/core";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { listShiftDefinitions } from "@/services/attendance-shift-definitions.service";
import {
  listRotations,
  saveRotation,
  type RotationDayType,
  type RotationPreset,
  type RotationStep,
} from "@/services/attendance-rotations.service";
import { PRESET_KEYS, presetSteps } from "./rotation-presets";

export const ROTATIONS_KEY = ["attendance", "rotations"] as const;

interface Props {
  canManage: boolean;
}

/** Pattern list plus create form; picking a preset prefills the step rows (cycle length follows the step count). */
export default function RotationPatternsPanel({ canManage }: Props) {
  const { t } = useTranslation(["rotations"]);
  const queryClient = useQueryClient();
  const patterns = useQuery({ queryKey: ROTATIONS_KEY, queryFn: listRotations });
  const shifts = useQuery({ queryKey: ["attendance", "shift-definitions"], queryFn: listShiftDefinitions });
  const [editing, setEditing] = useState(false);
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [preset, setPreset] = useState<RotationPreset>("custom");
  const [steps, setSteps] = useState<RotationStep[]>([]);

  const shiftOptions = (shifts.data ?? [])
    .filter((s) => s.id)
    .map((s) => ({ value: s.id as string, label: s.name }));

  function choosePreset(value: string) {
    const next = value as RotationPreset;
    setPreset(next);
    if (next !== "custom") setSteps(presetSteps(next));
  }

  function updateStep(index: number, patch: Partial<RotationStep>) {
    setSteps((current) => current.map((s, i) => (i === index ? { ...s, ...patch } : s)));
  }

  function reindex(list: RotationStep[]): RotationStep[] {
    return list.map((s, dayOffset) => ({ ...s, dayOffset }));
  }

  const save = useMutation({
    mutationFn: () => saveRotation(null, { code: code.trim(), name: name.trim(), preset, cycleLengthDays: steps.length, steps }),
    onSuccess: () => {
      toast({ variant: "success", description: t("rotations:saved") });
      setEditing(false);
      void queryClient.invalidateQueries({ queryKey: ROTATIONS_KEY });
    },
    onError: (error) => toast({ variant: "destructive", description: getApiErrorMessage(error, t("rotations:genericError")) }),
  });

  return (
    <Stack gap="md">
      {canManage && !editing && (
        <Group>
          <Button
            onClick={() => {
              setCode("");
              setName("");
              setPreset("custom");
              setSteps([]);
              setEditing(true);
            }}
          >
            {t("rotations:newPattern")}
          </Button>
        </Group>
      )}
      {editing && (
        <Card withBorder padding="lg" radius="md">
          <Stack gap="sm">
            <Group grow align="flex-start">
              <TextInput label={t("rotations:code")} value={code} onChange={(e) => setCode(e.currentTarget.value)} required />
              <TextInput label={t("rotations:name")} value={name} onChange={(e) => setName(e.currentTarget.value)} required />
            </Group>
            <div>
              <Text size="sm" fw={500} mb={4}>
                {t("rotations:preset")}
              </Text>
              <SegmentedControl
                value={preset}
                onChange={choosePreset}
                data={[
                  { value: "custom", label: t("rotations:presets.custom") },
                  ...PRESET_KEYS.map((key) => ({ value: key, label: t(`rotations:presets.${key}`) })),
                ]}
              />
            </div>
            <NumberInput label={t("rotations:cycleLength")} value={steps.length} readOnly />
            <Text size="sm" fw={500}>
              {t("rotations:steps")}
            </Text>
            {steps.map((step, index) => (
              <Group key={index} data-testid="rotation-step" align="flex-end">
                <Text size="sm" w={70}>
                  {t("rotations:stepDay", { n: index + 1 })}
                </Text>
                <Select
                  aria-label={t("rotations:dayType")}
                  data={(["work", "off"] as RotationDayType[]).map((v) => ({ value: v, label: t(`rotations:dayTypes.${v}`) }))}
                  value={step.dayType}
                  allowDeselect={false}
                  onChange={(v) =>
                    updateStep(index, { dayType: (v as RotationDayType) ?? "work", shiftDefinitionId: v === "off" ? undefined : step.shiftDefinitionId })
                  }
                />
                {step.dayType === "work" && (
                  <Select
                    aria-label={t("rotations:shift")}
                    placeholder={t("rotations:shift")}
                    data={shiftOptions}
                    value={step.shiftDefinitionId ?? null}
                    onChange={(v) => updateStep(index, { shiftDefinitionId: v ?? undefined })}
                  />
                )}
                <Button variant="subtle" color="red" size="xs" onClick={() => setSteps((c) => reindex(c.filter((_, i) => i !== index)))}>
                  {t("rotations:removeStep")}
                </Button>
              </Group>
            ))}
            <Group>
              <Button variant="light" onClick={() => setSteps((c) => reindex([...c, { dayOffset: c.length, dayType: "work" }]))}>
                {t("rotations:addStep")}
              </Button>
              <Button onClick={() => save.mutate()} loading={save.isPending} disabled={!code.trim() || !name.trim() || steps.length === 0}>
                {t("rotations:save")}
              </Button>
            </Group>
          </Stack>
        </Card>
      )}
      {(patterns.data ?? []).length === 0 && !patterns.isLoading ? (
        <Text c="dimmed" size="sm">
          {t("rotations:noPatterns")}
        </Text>
      ) : (
        <Table>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>{t("rotations:code")}</Table.Th>
              <Table.Th>{t("rotations:name")}</Table.Th>
              <Table.Th>{t("rotations:preset")}</Table.Th>
              <Table.Th>{t("rotations:cycleLength")}</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {(patterns.data ?? []).map((p) => (
              <Table.Tr key={p.id}>
                <Table.Td>{p.code}</Table.Td>
                <Table.Td>{p.name}</Table.Td>
                <Table.Td>{t(`rotations:presets.${p.preset}`)}</Table.Td>
                <Table.Td>{p.cycleLengthDays}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}
    </Stack>
  );
}
