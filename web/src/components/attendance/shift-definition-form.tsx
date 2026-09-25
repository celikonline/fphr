import { useState } from "react";
import { useTranslation } from "react-i18next";
import {
  ActionIcon,
  Button,
  ColorInput,
  Group,
  NumberInput,
  SegmentedControl,
  Stack,
  Switch,
  Text,
  TextInput,
} from "@mantine/core";
import { Plus, Trash2 } from "lucide-react";
import ShiftTimeline, { formatMinute } from "./shift-timeline";
import type {
  ShiftBreak,
  ShiftDefinition,
  ShiftType,
} from "@/services/attendance-shift-definitions.service";

export function emptyShiftDefinition(): ShiftDefinition {
  return {
    code: "",
    name: "",
    colorHex: "#228be6",
    type: "fixed",
    startMinute: 540,
    endMinute: 1080,
    startsPreviousDay: false,
    breaks: [],
    lateToleranceMinutes: 0,
    earlyLeaveToleranceMinutes: 0,
    bonusMinutes: 0,
    isActive: true,
  };
}

const parseTime = (v: string): number | null => {
  const m = /^(\d{2}):(\d{2})/.exec(v);
  return m ? Number(m[1]) * 60 + Number(m[2]) : null;
};
const num = (v: string | number): number | null => (v === "" ? null : Number(v));

interface Props {
  initial: ShiftDefinition;
  saving: boolean;
  readOnly?: boolean;
  onSubmit: (dto: ShiftDefinition, applyToPast: boolean) => void;
  onCancel: () => void;
}

export default function ShiftDefinitionForm({
  initial,
  saving,
  readOnly,
  onSubmit,
  onCancel,
}: Props) {
  const { t } = useTranslation(["shifts"]);
  const [v, setV] = useState<ShiftDefinition>(initial);
  const [touched, setTouched] = useState(false);
  const patch = (p: Partial<ShiftDefinition>) => setV((cur) => ({ ...cur, ...p }));
  const flexible = v.type === "flexible";
  const invalid = v.code.trim() === "" || v.name.trim() === "";

  const setBreak = (i: number, p: Partial<ShiftBreak>) =>
    patch({ breaks: v.breaks.map((b, idx) => (idx === i ? { ...b, ...p } : b)) });

  const submit = (applyToPast: boolean) => {
    setTouched(true);
    if (invalid) return;
    onSubmit({ ...v, code: v.code.trim(), name: v.name.trim() }, applyToPast);
  };

  const optional = (key: keyof ShiftDefinition, label: string) => (
    <NumberInput
      label={label}
      min={0}
      disabled={readOnly}
      value={(v[key] as number | null | undefined) ?? ""}
      onChange={(x) => patch({ [key]: num(x) } as Partial<ShiftDefinition>)}
    />
  );

  return (
    <form
      className="space-y-4"
      onSubmit={(e) => {
        e.preventDefault();
        submit(false);
      }}
    >
      <Group grow align="flex-start">
        <TextInput
          label={t("shifts:form.code")}
          value={v.code}
          disabled={readOnly}
          error={touched && v.code.trim() === "" ? t("shifts:form.required") : undefined}
          onChange={(e) => patch({ code: e.currentTarget.value })}
        />
        <TextInput
          label={t("shifts:form.name")}
          value={v.name}
          disabled={readOnly}
          error={touched && v.name.trim() === "" ? t("shifts:form.required") : undefined}
          onChange={(e) => patch({ name: e.currentTarget.value })}
        />
        <ColorInput
          label={t("shifts:form.color")}
          value={v.colorHex}
          disabled={readOnly}
          onChange={(c) => patch({ colorHex: c })}
        />
      </Group>

      <div>
        <Text size="sm" fw={500} mb={4}>
          {t("shifts:form.type")}
        </Text>
        <SegmentedControl
          value={v.type}
          disabled={readOnly}
          onChange={(x) => patch({ type: x as ShiftType })}
          data={(["fixed", "flexible", "dynamic"] as const).map((k) => ({
            value: k,
            label: t(`shifts:type.${k}`),
          }))}
        />
        <Text size="xs" c="dimmed" mt={4}>
          {t(`shifts:typeHint.${v.type}`)}
        </Text>
      </div>

      <ShiftTimeline
        startMinute={v.startMinute}
        endMinute={v.endMinute}
        breaks={v.breaks}
        color={v.colorHex}
        hideEnd={flexible}
        disabled={readOnly}
        onChange={patch}
      />
      {!flexible && v.endMinute <= v.startMinute && (
        <Text size="xs" c="orange">
          {t("shifts:form.overnight", {
            start: formatMinute(v.startMinute),
            end: formatMinute(v.endMinute),
          })}
        </Text>
      )}
      <Switch
        label={t("shifts:form.startsPreviousDay")}
        checked={v.startsPreviousDay}
        disabled={readOnly}
        onChange={(e) => patch({ startsPreviousDay: e.currentTarget.checked })}
      />

      {flexible && (
        <Group grow align="flex-start">
          <NumberInput
            label={t("shifts:form.duration")}
            min={1}
            disabled={readOnly}
            value={v.durationMinutes ?? ""}
            onChange={(x) => patch({ durationMinutes: num(x) })}
          />
          <TextInput
            type="time"
            step={900}
            label={t("shifts:form.flexEarliest")}
            disabled={readOnly}
            value={v.flexStartEarliestMinute == null ? "" : formatMinute(v.flexStartEarliestMinute)}
            onChange={(e) => patch({ flexStartEarliestMinute: parseTime(e.currentTarget.value) })}
          />
          <TextInput
            type="time"
            step={900}
            label={t("shifts:form.flexLatest")}
            disabled={readOnly}
            value={v.flexStartLatestMinute == null ? "" : formatMinute(v.flexStartLatestMinute)}
            onChange={(e) => patch({ flexStartLatestMinute: parseTime(e.currentTarget.value) })}
          />
        </Group>
      )}

      <Stack gap="xs">
        <Group justify="space-between">
          <Text fw={500}>{t("shifts:form.breaks")}</Text>
          {!readOnly && (
            <Button
              variant="light"
              size="xs"
              leftSection={<Plus size={14} />}
              onClick={() =>
                patch({
                  breaks: [
                    ...v.breaks,
                    { startMinute: null, durationMinutes: 30, isFlexible: true },
                  ],
                })
              }
            >
              {t("shifts:form.addBreak")}
            </Button>
          )}
        </Group>
        {v.breaks.length === 0 && (
          <Text size="sm" c="dimmed">
            {t("shifts:form.noBreaks")}
          </Text>
        )}
        {v.breaks.map((b, i) => (
          <Group key={i} data-testid="break-row" align="flex-end">
            <TextInput
              type="time"
              step={900}
              label={t("shifts:form.breakStart")}
              disabled={readOnly || b.isFlexible}
              value={b.startMinute == null ? "" : formatMinute(b.startMinute)}
              onChange={(e) => setBreak(i, { startMinute: parseTime(e.currentTarget.value) })}
            />
            <NumberInput
              label={t("shifts:form.breakDuration")}
              min={1}
              disabled={readOnly}
              value={b.durationMinutes}
              onChange={(x) => setBreak(i, { durationMinutes: Number(x) || 0 })}
            />
            <Switch
              label={t("shifts:form.breakFlexible")}
              checked={b.isFlexible}
              disabled={readOnly}
              onChange={(e) =>
                setBreak(i, {
                  isFlexible: e.currentTarget.checked,
                  startMinute: e.currentTarget.checked ? null : (b.startMinute ?? v.startMinute),
                })
              }
            />
            {!readOnly && (
              <ActionIcon
                color="red"
                variant="subtle"
                aria-label={t("shifts:form.removeBreak")}
                onClick={() => patch({ breaks: v.breaks.filter((_, idx) => idx !== i) })}
              >
                <Trash2 size={16} />
              </ActionIcon>
            )}
          </Group>
        ))}
      </Stack>

      <Group grow align="flex-start">
        <NumberInput
          label={t("shifts:form.lateTolerance")}
          min={0}
          disabled={readOnly}
          value={v.lateToleranceMinutes}
          onChange={(x) => patch({ lateToleranceMinutes: Number(x) || 0 })}
        />
        <NumberInput
          label={t("shifts:form.earlyLeaveTolerance")}
          min={0}
          disabled={readOnly}
          value={v.earlyLeaveToleranceMinutes}
          onChange={(x) => patch({ earlyLeaveToleranceMinutes: Number(x) || 0 })}
        />
        <NumberInput
          label={t("shifts:form.bonus")}
          min={0}
          disabled={readOnly}
          value={v.bonusMinutes}
          onChange={(x) => patch({ bonusMinutes: Number(x) || 0 })}
        />
      </Group>
      <Group grow align="flex-start">
        {optional("earlyArrivalOvertimeMinMinutes", t("shifts:form.earlyOtMin"))}
        {optional("earlyArrivalOvertimeMaxMinutes", t("shifts:form.earlyOtMax"))}
        {optional("lateDepartureOvertimeMinMinutes", t("shifts:form.lateOtMin"))}
        {optional("lateDepartureOvertimeMaxMinutes", t("shifts:form.lateOtMax"))}
      </Group>
      <Switch
        label={t("shifts:form.active")}
        checked={v.isActive}
        disabled={readOnly}
        onChange={(e) => patch({ isActive: e.currentTarget.checked })}
      />

      <Group justify="flex-end">
        <Button variant="default" onClick={onCancel}>
          {t("shifts:form.cancel")}
        </Button>
        {!readOnly && (
          <>
            <Button type="button" variant="light" loading={saving} onClick={() => submit(true)}>
              {t("shifts:form.saveApplyPast")}
            </Button>
            <Button type="submit" loading={saving}>
              {t("shifts:form.save")}
            </Button>
          </>
        )}
      </Group>
    </form>
  );
}
