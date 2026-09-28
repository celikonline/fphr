import React, { useEffect, useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { BottomSheet, PrimaryButton, SegmentedControl } from "@/components/ui";
import { toLocalDateOnly } from "@/lib/formatters";
import { palette, radius, text } from "@/theme/theme";
import { MonthCalendar } from "./MonthCalendar";
import {
  applyDayTap,
  daysInclusive,
  formatDayMonthYear,
  parseDateOnly,
  shiftMonth,
  type DateRangeMode,
} from "./dateRange";

type Props = {
  visible: boolean;
  mode: DateRangeMode;
  /** Shows the "Tek gün / Birden çok gün" segment (leave requests). */
  allowModeSwitch?: boolean;
  from?: string;
  to?: string;
  minDate?: string;
  /** Called with the picked range (`from === to` in single mode); the sheet then calls `onClose`. */
  onApply: (from: string, to: string) => void;
  onClose: () => void;
};

type MonthView = { year: number; month0: number };

/** The month to show first: the selected start, or the current month. */
function monthOf(value?: string): MonthView {
  const { year, month0 } = parseDateOnly(value ?? toLocalDateOnly(new Date()));
  return { year, month0 };
}

/** JS "Tarih seç" bottom sheet (spec §5): mode segment, Başlangıç/Bitiş boxes, month calendar. */
export function DateRangeSheet({
  visible,
  mode,
  allowModeSwitch = false,
  from,
  to,
  minDate,
  onApply,
  onClose,
}: Props) {
  const { t } = useTranslation();
  const modeOptions: { value: DateRangeMode; label: string }[] = [
    { value: "single", label: t("dateSheet.singleDay") },
    { value: "range", label: t("dateSheet.multipleDays") },
  ];
  const [activeMode, setActiveMode] = useState<DateRangeMode>(mode);
  const [draft, setDraft] = useState<{ from?: string; to?: string }>({ from, to });
  const [view, setView] = useState<MonthView>(() => monthOf(from));

  // Every opening starts from the caller's current value, not the last abandoned draft.
  useEffect(() => {
    if (!visible) return;
    setActiveMode(mode);
    setDraft({ from, to });
    setView(monthOf(from));
  }, [visible, mode, from, to]);

  function changeMode(next: DateRangeMode) {
    setActiveMode(next);
    if (next === "single" && draft.from) setDraft({ from: draft.from, to: draft.from });
  }

  function apply() {
    if (!draft.from || !draft.to) return;
    onApply(draft.from, draft.to);
    onClose();
  }

  const dayCount = draft.from && draft.to ? daysInclusive(draft.from, draft.to) : 0;
  const isRange = activeMode === "range";

  return (
    <BottomSheet visible={visible} onClose={onClose} title={t("dateSheet.title")}>
      <View style={styles.body}>
        {allowModeSwitch ? (
          <SegmentedControl options={modeOptions} value={activeMode} onChange={changeMode} />
        ) : null}

        <View style={styles.boxes}>
          <DateBox
            label={isRange ? t("dateSheet.start") : t("dateSheet.date")}
            value={draft.from}
          />
          {isRange ? <DateBox label={t("dateSheet.end")} value={draft.to} /> : null}
        </View>

        <MonthCalendar
          year={view.year}
          month0={view.month0}
          from={draft.from}
          to={draft.to}
          minDate={minDate}
          onPrevMonth={() => setView((current) => shiftMonth(current.year, current.month0, -1))}
          onNextMonth={() => setView((current) => shiftMonth(current.year, current.month0, 1))}
          onDayPress={(day) =>
            setDraft((current) => applyDayTap({ ...current, mode: activeMode }, day))
          }
        />

        <PrimaryButton
          label={
            dayCount > 0 ? t("dateSheet.applyWithDays", { count: dayCount }) : t("actions.apply")
          }
          onPress={apply}
          disabled={dayCount === 0}
        />
      </View>
    </BottomSheet>
  );
}

function DateBox({ label, value }: { label: string; value?: string }) {
  const { t } = useTranslation();
  return (
    <View style={styles.box}>
      <Text style={styles.boxLabel}>{label}</Text>
      <Text style={value ? styles.boxValue : styles.boxPlaceholder}>
        {value ? formatDayMonthYear(value) : t("actions.select")}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  body: {
    gap: 14,
    paddingTop: 8,
  },
  boxes: {
    flexDirection: "row",
    gap: 10,
  },
  box: {
    flex: 1,
    gap: 2,
    paddingHorizontal: 14,
    paddingVertical: 10,
    borderRadius: radius.button,
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
  },
  boxLabel: {
    ...text.caption,
  },
  boxValue: {
    ...text.body,
  },
  boxPlaceholder: {
    ...text.body,
    color: palette.textFaint,
  },
});
