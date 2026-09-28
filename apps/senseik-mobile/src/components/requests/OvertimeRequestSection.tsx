import React, { useState } from "react";
import { Alert, View } from "react-native";
import { useTranslation } from "react-i18next";
import { PrimaryButton, SelectField, TextField } from "@/components/ui";
import { OptionSheet } from "@/components/form/OptionSheet";
import { SheetDateField } from "@/components/form/SheetDateField";
import { SummaryRow } from "@/components/form/SummaryRow";
import { TimeField } from "@/components/form/TimeField";
import {
  leaveService,
  OVERTIME_DEFAULT_PAY_RATE_KIND,
  type OvertimePayRateKind,
} from "@/services/LeaveService";
import { formatDurationMinutes, toLocalDateOnly } from "@/lib/formatters";
import { isOvertimeDurationValid, parseAmountInput } from "@/lib/requestValidation";
import { overtimeWarningLabel } from "@/lib/overtimeWarnings";
import { getApiErrorMessage } from "@/lib/apiError";
import i18n from "@/lib/i18n";
import { requestFormStyles as styles } from "./requestFormStyles";

function todayDateOnly(): string {
  return toLocalDateOnly(new Date());
}

/**
 * TK-031 (web parity): labels mirror `web/public/locales/tr/leave.json`'s `overtime.payRate.*`
 * (4857 md.41 - `OvertimePayRateKind` doc comment in `LeaveService.ts` has the legal detail).
 */
const PAY_RATE_KINDS: readonly OvertimePayRateKind[] = ["rate50", "rate25"];

export function overtimePayRateLabel(kind: OvertimePayRateKind): string {
  return i18n.t(`requests:overtime.payRates.${kind}`);
}

/** `SubmitMyOvertimeRequestValidator`: duration must be `1..OvertimeLimits.MaxDurationMinutes`. */
export function canSubmitOvertimeRequest(params: { durationText: string }): boolean {
  const minutes = parseAmountInput(params.durationText);
  return minutes !== null && isOvertimeDurationValid(minutes);
}

type Props = {
  /** Called after a successful submit (NewRequest closes itself with it). */
  onSubmitted?: () => void;
};

/**
 * "Fazla mesai talebi" form (NewRequest body). The caller's own overtime history, with its "Geri
 * çek" action, lives on Taleplerim (`RequestsScreen`).
 */
export default function OvertimeRequestSection({ onSubmitted }: Props) {
  const { t } = useTranslation("requests");
  const [submitting, setSubmitting] = useState(false);
  const [workDate, setWorkDate] = useState(todayDateOnly());
  const [startTime, setStartTime] = useState("18:00");
  const [durationText, setDurationText] = useState("");
  const [description, setDescription] = useState("");
  const [payRateKind, setPayRateKind] = useState<OvertimePayRateKind>(
    OVERTIME_DEFAULT_PAY_RATE_KIND
  );
  const [payRateSheetVisible, setPayRateSheetVisible] = useState(false);

  async function submit() {
    const durationMinutes = parseAmountInput(durationText);
    if (durationMinutes === null || !isOvertimeDurationValid(durationMinutes)) {
      Alert.alert(t("common:alerts.error"), t("overtime.invalidDuration"));
      return;
    }

    setSubmitting(true);
    try {
      const result = await leaveService.submitOvertimeRequest({
        workDate,
        startTime,
        durationMinutes,
        description: description.trim().length > 0 ? description.trim() : null,
        payRateKind,
      });
      setDurationText("");
      setDescription("");
      setPayRateKind(OVERTIME_DEFAULT_PAY_RATE_KIND);
      onSubmitted?.();
      if (result.warnings.length > 0) {
        Alert.alert(
          t("overtime.sentWithWarnings"),
          result.warnings.map(overtimeWarningLabel).join("\n\n")
        );
      } else {
        Alert.alert(t("common:alerts.sent"), t("overtime.submitted"));
      }
    } catch (submitError) {
      Alert.alert(
        t("common:alerts.error"),
        getApiErrorMessage(submitError, t("overtime.submitError"))
      );
    } finally {
      setSubmitting(false);
    }
  }

  const durationPreview = parseAmountInput(durationText);
  const canSubmit = canSubmitOvertimeRequest({ durationText }) && !submitting;
  const payRateOptions = PAY_RATE_KINDS.map((kind) => ({
    value: kind,
    label: overtimePayRateLabel(kind),
  }));

  return (
    <View style={styles.form}>
      <SheetDateField label={t("form.date")} required value={workDate} onChange={setWorkDate} />
      <TimeField
        label={t("overtime.startTime")}
        required
        value={startTime}
        onChange={setStartTime}
      />
      <TextField
        label={t("overtime.durationMinutes")}
        required
        value={durationText}
        onChangeText={setDurationText}
        placeholder={t("overtime.durationPlaceholder")}
        keyboardType="number-pad"
      />
      {durationPreview !== null && isOvertimeDurationValid(durationPreview) ? (
        <SummaryRow label={t("overtime.duration")} value={formatDurationMinutes(durationPreview)} />
      ) : null}
      <SelectField
        label={t("overtime.payRate")}
        required
        value={overtimePayRateLabel(payRateKind)}
        placeholder={t("overtime.payRatePlaceholder")}
        onPress={() => setPayRateSheetVisible(true)}
      />
      <TextField
        label={t("form.descriptionOptional")}
        value={description}
        onChangeText={setDescription}
        placeholder={t("overtime.descriptionPlaceholder")}
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

      <OptionSheet
        visible={payRateSheetVisible}
        title={t("overtime.payRate")}
        options={payRateOptions}
        selected={payRateKind}
        onSelect={setPayRateKind}
        onClose={() => setPayRateSheetVisible(false)}
      />
    </View>
  );
}
