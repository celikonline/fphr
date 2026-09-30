import React, { useEffect, useState } from "react";
import { Alert, StyleSheet, Switch, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { PrimaryButton, TextField } from "@/components/ui";
import { SheetDateField } from "@/components/form/SheetDateField";
import { TimeField } from "@/components/form/TimeField";
import { attendanceService, type AttendanceDay } from "@/services/AttendanceService";
import { toLocalDateOnly } from "@/lib/formatters";
import {
  isPunchCorrectionDateAllowed,
  isPunchCorrectionTimeRangeValid,
} from "@/lib/requestValidation";
import { getApiErrorMessage } from "@/lib/apiError";
import { palette, text } from "@/theme/theme";
import { requestFormStyles as styles } from "./requestFormStyles";

function todayDateOnly(): string {
  return toLocalDateOnly(new Date());
}

const DEFAULT_CHECK_IN = "09:00";
const DEFAULT_CHECK_OUT = "18:00";

function timeOnly5(value?: string | null): string | null {
  return value ? value.slice(0, 5) : null;
}

/**
 * Prefill for the picked day (spec: "gün ve planlanan saat önceden dolu"): the day's planned shift
 * time if there is one, else its own recorded punch, else a sensible default. A punch missing on the
 * day (the "Eksik" case this form exists for) starts pre-checked so the field is ready to fill in;
 * a punch already on file starts unchecked (the employee is only fixing what's actually wrong).
 */
export function punchCorrectionDefaults(
  day?: Pick<AttendanceDay, "checkIn" | "checkOut" | "plannedStart" | "plannedEnd">
): {
  includeCheckIn: boolean;
  includeCheckOut: boolean;
  checkInTime: string;
  checkOutTime: string;
} {
  return {
    includeCheckIn: !day?.checkIn,
    includeCheckOut: !day?.checkOut,
    checkInTime: timeOnly5(day?.plannedStart) ?? timeOnly5(day?.checkIn) ?? DEFAULT_CHECK_IN,
    checkOutTime: timeOnly5(day?.plannedEnd) ?? timeOnly5(day?.checkOut) ?? DEFAULT_CHECK_OUT,
  };
}

/** `PunchCorrectionRequest.Submit`: at least one time, an in-order range when both are given, a
 * non-empty gerekçe and no gelecek tarih. */
export function canSubmitPunchCorrection(params: {
  date: string;
  includeCheckIn: boolean;
  includeCheckOut: boolean;
  checkInTime: string;
  checkOutTime: string;
  reason: string;
  today?: Date;
}): boolean {
  if (!isPunchCorrectionDateAllowed(params.date, params.today)) return false;
  if (!params.includeCheckIn && !params.includeCheckOut) return false;
  if (!isPunchCorrectionTimeRangeValid(params)) return false;
  return params.reason.trim().length > 0;
}

type Props = {
  /** Preselected day from the Puantaj day card's "Düzeltme iste" action ("yyyy-MM-dd"); defaults to today. */
  initialDate?: string;
  onSubmitted?: () => void;
};

/**
 * "Eksik giriş/çıkış talebi" form (NewRequest body / FG-004): the picked day's own record (if any)
 * seeds the planned/current times, the employee ticks which punch(es) to correct and why. The
 * caller's own request history (with geri çek) lives on Taleplerim (`RequestsScreen`).
 */
export default function PunchCorrectionSection({ initialDate, onSubmitted }: Props) {
  const { t } = useTranslation("requests");
  const [date, setDate] = useState(initialDate ?? todayDateOnly());
  const [includeCheckIn, setIncludeCheckIn] = useState(false);
  const [includeCheckOut, setIncludeCheckOut] = useState(false);
  const [checkInTime, setCheckInTime] = useState(DEFAULT_CHECK_IN);
  const [checkOutTime, setCheckOutTime] = useState(DEFAULT_CHECK_OUT);
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);

  // Re-seeds every time the date changes, whether it arrived via `initialDate` or the sheet below.
  useEffect(() => {
    let active = true;
    attendanceService
      .getMyAttendanceDays({ from: date, to: date })
      .then((days) => {
        if (!active) return;
        const defaults = punchCorrectionDefaults(days[0]);
        setIncludeCheckIn(defaults.includeCheckIn);
        setIncludeCheckOut(defaults.includeCheckOut);
        setCheckInTime(defaults.checkInTime);
        setCheckOutTime(defaults.checkOutTime);
      })
      .catch(() => {
        // Best-effort prefill only - the form still works with whatever is already in state.
      });
    return () => {
      active = false;
    };
  }, [date]);

  async function submit() {
    setSubmitting(true);
    try {
      await attendanceService.submitPunchCorrection({
        date,
        checkIn: includeCheckIn ? `${checkInTime}:00` : undefined,
        checkOut: includeCheckOut ? `${checkOutTime}:00` : undefined,
        reason: reason.trim(),
      });
      setReason("");
      onSubmitted?.();
      Alert.alert(t("common:alerts.sent"), t("punchCorrection.submitted"));
    } catch (submitError) {
      Alert.alert(
        t("common:alerts.error"),
        getApiErrorMessage(submitError, t("punchCorrection.submitError"))
      );
    } finally {
      setSubmitting(false);
    }
  }

  const canSubmit =
    canSubmitPunchCorrection({
      date,
      includeCheckIn,
      includeCheckOut,
      checkInTime,
      checkOutTime,
      reason,
    }) && !submitting;
  const showRangeWarning = !isPunchCorrectionTimeRangeValid({
    includeCheckIn,
    checkInTime,
    includeCheckOut,
    checkOutTime,
  });
  const showFutureDateWarning = !isPunchCorrectionDateAllowed(date);

  return (
    <View style={styles.form}>
      <SheetDateField label={t("form.date")} required value={date} onChange={setDate} />
      {showFutureDateWarning ? (
        <Text style={styles.warning}>{t("punchCorrection.futureDateNotAllowed")}</Text>
      ) : null}

      <TimeToggleRow
        label={t("punchCorrection.includeCheckIn")}
        enabled={includeCheckIn}
        time={checkInTime}
        onToggle={setIncludeCheckIn}
        onTimeChange={setCheckInTime}
      />
      <TimeToggleRow
        label={t("punchCorrection.includeCheckOut")}
        enabled={includeCheckOut}
        time={checkOutTime}
        onToggle={setIncludeCheckOut}
        onTimeChange={setCheckOutTime}
      />
      {showRangeWarning ? (
        <Text style={styles.warning}>{t("punchCorrection.invalidTimeRange")}</Text>
      ) : null}

      <TextField
        label={t("punchCorrection.reason")}
        required
        value={reason}
        onChangeText={setReason}
        placeholder={t("punchCorrection.reasonPlaceholder")}
        multiline
        numberOfLines={3}
        style={styles.textArea}
      />

      <View style={styles.submit}>
        <PrimaryButton
          label={t("form.submit")}
          onPress={() => void submit()}
          loading={submitting}
          disabled={!canSubmit}
        />
      </View>
    </View>
  );
}

/** One "Giriş/Çıkış saatini düzelt" toggle: a Switch that reveals its `TimeField` once enabled. */
function TimeToggleRow({
  label,
  enabled,
  time,
  onToggle,
  onTimeChange,
}: {
  label: string;
  enabled: boolean;
  time: string;
  onToggle: (value: boolean) => void;
  onTimeChange: (value: string) => void;
}) {
  return (
    <View style={rowStyles.row}>
      <View style={rowStyles.header}>
        <Text style={rowStyles.label}>{label}</Text>
        <Switch
          value={enabled}
          trackColor={{ false: palette.glassBorder, true: palette.brand }}
          thumbColor={palette.white}
          onValueChange={onToggle}
        />
      </View>
      {enabled ? <TimeField value={time} onChange={onTimeChange} /> : null}
    </View>
  );
}

const rowStyles = StyleSheet.create({
  row: {
    gap: 8,
  },
  header: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
  },
  label: {
    ...text.body,
  },
});
