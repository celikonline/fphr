import { useTranslation } from "react-i18next";
import { Slider, Text } from "@mantine/core";
import type { ShiftBreak } from "@/services/attendance-shift-definitions.service";

export const STEP_MINUTES = 15;
export const DAY_MINUTES = 1440;

/** En yakın 15 dakikaya yuvarlar; 0..1440 aralığında tutar. */
export function snapToStep(minute: number): number {
  const snapped = Math.round(minute / STEP_MINUTES) * STEP_MINUTES;
  return Math.min(DAY_MINUTES, Math.max(0, snapped));
}

export function formatMinute(minute: number): string {
  const m = ((minute % DAY_MINUTES) + DAY_MINUTES) % DAY_MINUTES;
  return `${String(Math.floor(m / 60)).padStart(2, "0")}:${String(m % 60).padStart(2, "0")}`;
}

/** end <= start ise vardiya gece yarısını aşar: iki parçaya bölünür (akşam + ertesi sabah). */
export function shiftSegments(start: number, end: number): { from: number; to: number }[] {
  if (end > start) return [{ from: start, to: end }];
  return [
    { from: start, to: DAY_MINUTES },
    { from: 0, to: end },
  ].filter((s) => s.to > s.from);
}

interface ShiftTimelineProps {
  startMinute: number;
  endMinute: number;
  breaks: ShiftBreak[];
  /** Esnek vardiyada bitiş sabit değildir; bitiş kaydırıcısı gizlenir. */
  hideEnd?: boolean;
  color?: string;
  disabled?: boolean;
  onChange: (next: { startMinute: number; endMinute: number }) => void;
}

const pct = (minute: number) => `${(minute / DAY_MINUTES) * 100}%`;

/** 24 saatlik (bitişte 00:00 = gece yarısı; kaydırıcı 23:45'te biter),, 15 dakika adımlı zaman çizelgesi; gece vardiyası sarmalı gösterilir. */
export default function ShiftTimeline({
  startMinute,
  endMinute,
  breaks,
  hideEnd,
  color = "#228be6",
  disabled,
  onChange,
}: ShiftTimelineProps) {
  const { t } = useTranslation(["shifts"]);
  const segments = hideEnd ? [] : shiftSegments(startMinute, endMinute);
  return (
    <div className="space-y-3">
      <div className="relative h-8 overflow-hidden rounded bg-gray-100" aria-hidden>
        {segments.map((s) => (
          <div
            key={s.from}
            data-testid="shift-segment"
            className="absolute inset-y-0 opacity-80"
            style={{ left: pct(s.from), width: pct(s.to - s.from), backgroundColor: color }}
          />
        ))}
        {hideEnd && (
          <div
            data-testid="shift-start-marker"
            className="absolute inset-y-0 w-1"
            style={{ left: pct(startMinute), backgroundColor: color }}
          />
        )}
        {breaks
          .filter((b) => b.startMinute != null)
          .map((b, i) => (
            <div
              key={i}
              data-testid="shift-break"
              className="absolute inset-y-0 bg-white/80"
              style={{ left: pct(b.startMinute ?? 0), width: pct(b.durationMinutes) }}
            />
          ))}
      </div>
      <div className="flex justify-between text-xs text-gray-500" aria-hidden>
        {[0, 6, 12, 18, 24].map((h) => (
          <span key={h}>{String(h).padStart(2, "0")}:00</span>
        ))}
      </div>
      <Text size="xs" c="dimmed">
        {formatMinute(startMinute)}
        {hideEnd ? "" : ` – ${formatMinute(endMinute)}`}
      </Text>
      <Slider
        aria-label={t("shifts:timeline.start")}
        thumbLabel={t("shifts:timeline.start")}
        min={0}
        max={DAY_MINUTES - STEP_MINUTES}
        step={STEP_MINUTES}
        value={startMinute}
        disabled={disabled}
        label={formatMinute}
        onChange={(v) => onChange({ startMinute: snapToStep(v), endMinute })}
      />
      {!hideEnd && (
        <Slider
          aria-label={t("shifts:timeline.end")}
          thumbLabel={t("shifts:timeline.end")}
          min={0}
          max={DAY_MINUTES - STEP_MINUTES}
          step={STEP_MINUTES}
          value={endMinute}
          disabled={disabled}
          label={formatMinute}
          onChange={(v) => onChange({ startMinute, endMinute: snapToStep(v) })}
        />
      )}
    </div>
  );
}
