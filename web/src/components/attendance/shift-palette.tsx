import type { DragEvent, KeyboardEvent } from "react";
import { useTranslation } from "react-i18next";
import { Group, Paper, Stack, Text } from "@mantine/core";
import type { ShiftDefinition } from "@/services/attendance-shift-definitions.service";
import type { ShiftDayType } from "@/services/attendance-shift-calendar.service";

/** MIME used for native HTML5 drag & drop between the palette and the calendar cells. */
export const SHIFT_PALETTE_MIME = "application/x-senseik-shift-palette";

/** Day types that can be dropped without a shift definition (`work` needs a definition, so it is not a chip). */
export const PALETTE_DAY_TYPES: readonly Exclude<ShiftDayType, "work">[] = ["weeklyRest", "restDay", "publicHoliday"];

export type PalettePayload =
  | { kind: "shift"; shiftDefinitionId: string }
  | { kind: "dayType"; dayType: Exclude<ShiftDayType, "work"> };

export function decodePalettePayload(raw: string): PalettePayload | null {
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw) as Partial<PalettePayload>;
    if (parsed.kind === "shift" && typeof parsed.shiftDefinitionId === "string") return parsed as PalettePayload;
    if (parsed.kind === "dayType" && PALETTE_DAY_TYPES.includes(parsed.dayType as never)) return parsed as PalettePayload;
  } catch {
    // malformed payload from a foreign drag source
  }
  return null;
}

interface ShiftPaletteProps {
  definitions: ShiftDefinition[];
  /** Locked period / missing plan permission: chips are inert. */
  disabled: boolean;
  /** Chip selected for keyboard use: the next Enter on a cell applies it. */
  armed?: PalettePayload | null;
  onPick: (payload: PalettePayload) => void;
}

/** Minutes from day start → "HH:mm" (an overnight end wraps past 24h). */
const hhmm = (minute: number) => `${String(Math.floor(minute / 60) % 24).padStart(2, "0")}:${String(minute % 60).padStart(2, "0")}`;

export default function ShiftPalette({ definitions, disabled, armed, onPick }: ShiftPaletteProps) {
  const { t } = useTranslation(["attendance"]);
  const active = definitions.filter((d) => d.id && d.isActive);

  return (
    <Paper withBorder p="sm" radius="md">
      <Stack gap="xs">
        <Text fw={600} size="sm">
          {t("attendance:shiftPlan.paletteTitle")}
        </Text>
        <Text size="xs" c="dimmed">
          {t("attendance:shiftPlan.paletteHint")}
        </Text>
        <Group gap="xs" wrap="wrap">
          {active.length === 0 && (
            <Text size="xs" c="dimmed">
              {t("attendance:shiftPlan.paletteEmpty")}
            </Text>
          )}
          {active.map((definition) => (
            <PaletteChip
              key={definition.id}
              testId={`palette-shift-${definition.id}`}
              colorHex={definition.colorHex}
              label={`${definition.name} ${hhmm(definition.startMinute)}–${hhmm(definition.endMinute)}`}
              payload={{ kind: "shift", shiftDefinitionId: definition.id as string }}
              disabled={disabled}
              armed={isArmed(armed, { kind: "shift", shiftDefinitionId: definition.id as string })}
              onPick={onPick}
            />
          ))}
        </Group>
        <Text fw={600} size="sm">
          {t("attendance:shiftPlan.dayTypeTitle")}
        </Text>
        <Group gap="xs" wrap="wrap">
          {PALETTE_DAY_TYPES.map((dayType) => (
            <PaletteChip
              key={dayType}
              testId={`palette-daytype-${dayType}`}
              label={t(`attendance:shiftPlan.dayType.${dayType}`)}
              payload={{ kind: "dayType", dayType }}
              disabled={disabled}
              armed={isArmed(armed, { kind: "dayType", dayType })}
              onPick={onPick}
            />
          ))}
        </Group>
      </Stack>
    </Paper>
  );
}

function isArmed(armed: PalettePayload | null | undefined, payload: PalettePayload): boolean {
  return !!armed && JSON.stringify(armed) === JSON.stringify(payload);
}

interface PaletteChipProps {
  testId: string;
  label: string;
  payload: PalettePayload;
  colorHex?: string;
  disabled: boolean;
  armed: boolean;
  onPick: (payload: PalettePayload) => void;
}

function PaletteChip({ testId, label, payload, colorHex, disabled, armed, onPick }: PaletteChipProps) {
  const handleDragStart = (event: DragEvent<HTMLDivElement>) => {
    if (disabled) return event.preventDefault();
    event.dataTransfer.setData(SHIFT_PALETTE_MIME, JSON.stringify(payload));
    event.dataTransfer.effectAllowed = "copy";
  };
  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (disabled || (event.key !== "Enter" && event.key !== " ")) return;
    event.preventDefault();
    onPick(payload);
  };

  return (
    <Paper
      component="div"
      withBorder
      bd={armed ? "2px solid var(--mantine-primary-color-filled)" : undefined}
      px="sm"
      py={4}
      radius="xl"
      role="button"
      tabIndex={disabled ? -1 : 0}
      draggable={!disabled}
      aria-disabled={disabled}
      aria-pressed={armed}
      data-testid={testId}
      onDragStart={handleDragStart}
      onClick={() => !disabled && onPick(payload)}
      onKeyDown={handleKeyDown}
      style={{ cursor: disabled ? "not-allowed" : "grab", opacity: disabled ? 0.5 : 1, userSelect: "none" }}
    >
      <Group gap={6} wrap="nowrap">
        {colorHex && <span aria-hidden style={{ width: 8, height: 8, borderRadius: 4, background: colorHex }} />}
        <Text size="sm">{label}</Text>
      </Group>
    </Paper>
  );
}
