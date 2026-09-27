import React, { useState } from "react";
import { StyleSheet } from "react-native";
import { GlassCard, PrimaryButton, SelectField, TextField } from "@/components/ui";
import { DateField } from "@/components/form/DateField";
import { OptionSheet } from "@/components/form/OptionSheet";
import type { AvailabilityKind } from "@/services/ShiftService";

export const AVAILABILITY_KIND_LABELS: Record<AvailabilityKind, string> = {
  unavailable: "Bugün çalışmak istemiyorum",
  weeklyRestRequest: "Haftalık iznimi kullanmak istiyorum",
};

const KIND_OPTIONS = (Object.keys(AVAILABILITY_KIND_LABELS) as AvailabilityKind[]).map((kind) => ({
  value: kind,
  label: AVAILABILITY_KIND_LABELS[kind],
}));

type Props = {
  date: string;
  onDateChange: (value: string) => void;
  kind: AvailabilityKind;
  onKindChange: (value: AvailabilityKind) => void;
  note: string;
  onNoteChange: (value: string) => void;
  submitting: boolean;
  onSubmit: () => void;
};

/** "Müsait değilim" card: date, kind (bottom-sheet list), optional note, "Kaydet". */
export function AvailabilityForm({
  date,
  onDateChange,
  kind,
  onKindChange,
  note,
  onNoteChange,
  submitting,
  onSubmit,
}: Props) {
  const [kindSheetVisible, setKindSheetVisible] = useState(false);

  return (
    <GlassCard style={styles.card}>
      <DateField label="Tarih" required value={date} onChange={onDateChange} />
      <SelectField
        label="Tür"
        required
        value={AVAILABILITY_KIND_LABELS[kind]}
        placeholder="Tür seçin"
        onPress={() => setKindSheetVisible(true)}
      />
      <TextField
        label="Not (opsiyonel)"
        value={note}
        onChangeText={onNoteChange}
        placeholder="Ek bilgi ekleyin"
        multiline
        numberOfLines={3}
        style={styles.textArea}
      />
      <PrimaryButton label="Kaydet" onPress={onSubmit} loading={submitting} />
      <OptionSheet
        visible={kindSheetVisible}
        title="Tür"
        options={KIND_OPTIONS}
        selected={kind}
        onSelect={onKindChange}
        onClose={() => setKindSheetVisible(false)}
      />
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 20,
    gap: 14,
  },
  textArea: {
    minHeight: 88,
    textAlignVertical: "top",
  },
});
