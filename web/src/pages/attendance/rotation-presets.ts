import type { RotationPreset, RotationStep } from "@/services/attendance-rotations.service";

type PresetDayTypes = readonly ("work" | "off")[];

/** Day pattern per ready-made preset; shift definitions are mapped by the user afterwards. */
const PRESET_DAY_TYPES: Record<Exclude<RotationPreset, "custom">, PresetDayTypes> = {
  // 2x12: day shift, night shift, two days off.
  twoByTwelve: ["work", "work", "off", "off"],
  // 3x8: morning, evening, night.
  threeByEight: ["work", "work", "work"],
  // 4x6: four consecutive 6-hour shifts.
  fourBySix: ["work", "work", "work", "work"],
};

export const PRESET_KEYS = Object.keys(PRESET_DAY_TYPES) as Exclude<RotationPreset, "custom">[];

export function presetSteps(preset: Exclude<RotationPreset, "custom">): RotationStep[] {
  return PRESET_DAY_TYPES[preset].map((dayType, dayOffset) => ({ dayOffset, dayType }));
}
