import type { RotationPreset, RotationStep } from "@/services/attendance-rotations.service";

/** Step in the editor: `slot` is a UI-only label key for the shift the user maps to it (not sent to the API). */
export interface EditableRotationStep extends RotationStep {
  slot?: string;
}

type PresetKey = Exclude<RotationPreset, "custom">;

const OFF = "off";

/** Ready-made cycles: work slots (shift to be picked by the user) and rest days. */
const PRESET_SLOTS: Record<PresetKey, readonly string[]> = {
  twoByTwelve: ["day", "night", OFF, OFF],
  threeByEight: ["morning", "evening", "night", OFF],
  fourBySix: ["s1", "s2", "s3", "s4", OFF],
};

export const PRESET_KEYS = Object.keys(PRESET_SLOTS) as PresetKey[];

export function presetSteps(preset: PresetKey): EditableRotationStep[] {
  return PRESET_SLOTS[preset].map((slot, dayOffset) =>
    slot === OFF ? { dayOffset, dayType: "off" } : { dayOffset, dayType: "work", slot }
  );
}

/** Every work step must reference a shift definition before the pattern can be saved. */
export function allWorkStepsHaveShift(steps: readonly RotationStep[]): boolean {
  return steps.every((s) => s.dayType === "off" || Boolean(s.shiftDefinitionId));
}

export function toRequestSteps(steps: readonly EditableRotationStep[]): RotationStep[] {
  return steps.map(({ dayOffset, dayType, shiftDefinitionId }) => ({
    dayOffset,
    dayType,
    ...(dayType === "work" && shiftDefinitionId ? { shiftDefinitionId } : {}),
  }));
}
