import React, { useState } from "react";
import { Alert, View } from "react-native";
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
import { requestFormStyles as styles } from "./requestFormStyles";

function todayDateOnly(): string {
  return toLocalDateOnly(new Date());
}

/**
 * TK-031 (web parity): labels mirror `web/public/locales/tr/leave.json`'s `overtime.payRate.*`
 * (4857 md.41 - `OvertimePayRateKind` doc comment in `LeaveService.ts` has the legal detail).
 */
export const OVERTIME_PAY_RATE_LABELS: Record<OvertimePayRateKind, string> = {
  rate50: "%50 fazlası (fazla mesai)",
  rate25: "%25 fazlası (fazla sürelerle çalışma)",
};

const PAY_RATE_OPTIONS = (Object.keys(OVERTIME_PAY_RATE_LABELS) as OvertimePayRateKind[]).map(
  (kind) => ({ value: kind, label: OVERTIME_PAY_RATE_LABELS[kind] })
);

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
      Alert.alert("Hata", "Lütfen 1 dakika ile 24 saat arasında geçerli bir süre girin.");
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
        Alert.alert("Talep gönderildi", result.warnings.map(overtimeWarningLabel).join("\n\n"));
      } else {
        Alert.alert("Gönderildi", "Fazla mesai talebiniz onaya gönderildi.");
      }
    } catch (submitError) {
      Alert.alert(
        "Hata",
        getApiErrorMessage(submitError, "Fazla mesai talebi gönderilemedi. Lütfen tekrar deneyin.")
      );
    } finally {
      setSubmitting(false);
    }
  }

  const durationPreview = parseAmountInput(durationText);
  const canSubmit = canSubmitOvertimeRequest({ durationText }) && !submitting;

  return (
    <View style={styles.form}>
      <SheetDateField label="Tarih" required value={workDate} onChange={setWorkDate} />
      <TimeField label="Başlangıç saati" required value={startTime} onChange={setStartTime} />
      <TextField
        label="Süre (dakika)"
        required
        value={durationText}
        onChangeText={setDurationText}
        placeholder="Örn. 120"
        keyboardType="number-pad"
      />
      {durationPreview !== null && isOvertimeDurationValid(durationPreview) ? (
        <SummaryRow label="Süre" value={formatDurationMinutes(durationPreview)} />
      ) : null}
      <SelectField
        label="Ücret oranı"
        required
        value={OVERTIME_PAY_RATE_LABELS[payRateKind]}
        placeholder="Oran seçin"
        onPress={() => setPayRateSheetVisible(true)}
      />
      <TextField
        label="Açıklama (opsiyonel)"
        value={description}
        onChangeText={setDescription}
        placeholder="Örn. ay sonu kapanış çalışması"
        multiline
        numberOfLines={3}
        style={styles.textArea}
      />
      <View style={styles.submit}>
        <PrimaryButton
          label="Talebi gönder"
          onPress={() => void submit()}
          loading={submitting}
          disabled={!canSubmit}
        />
      </View>

      <OptionSheet
        visible={payRateSheetVisible}
        title="Ücret oranı"
        options={PAY_RATE_OPTIONS}
        selected={payRateKind}
        onSelect={setPayRateKind}
        onClose={() => setPayRateSheetVisible(false)}
      />
    </View>
  );
}
