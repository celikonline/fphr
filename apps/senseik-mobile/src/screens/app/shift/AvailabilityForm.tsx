import React, { useState } from "react";
import { StyleSheet } from "react-native";
import { useTranslation } from "react-i18next";
import i18n from "@/lib/i18n";
import { GlassCard, PrimaryButton, SelectField, TextField } from "@/components/ui";
import { DateField } from "@/components/form/DateField";
import { OptionSheet } from "@/components/form/OptionSheet";
import type { AvailabilityKind } from "@/services/ShiftService";

const AVAILABILITY_KINDS: readonly AvailabilityKind[] = ["unavailable", "weeklyRestRequest"];

/** Availability kind label in the active language. */
export function availabilityKindLabel(kind: AvailabilityKind): string {
  return i18n.t(`shift:availability.kinds.${kind}`);
}

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
  const { t } = useTranslation("shift");
  const [kindSheetVisible, setKindSheetVisible] = useState(false);
  const kindOptions = AVAILABILITY_KINDS.map((value) => ({
    value,
    label: availabilityKindLabel(value),
  }));
  return (
    <GlassCard style={styles.card}>
      <DateField label={t("availability.date")} required value={date} onChange={onDateChange} />
      <SelectField
        label={t("availability.kind")}
        required
        value={availabilityKindLabel(kind)}
        placeholder={t("availability.kindPlaceholder")}
        onPress={() => setKindSheetVisible(true)}
      />
      <TextField
        label={t("availability.note")}
        value={note}
        onChangeText={onNoteChange}
        placeholder={t("availability.notePlaceholder")}
        multiline
        numberOfLines={3}
        style={styles.textArea}
      />
      <PrimaryButton label={t("common:actions.save")} onPress={onSubmit} loading={submitting} />
      <OptionSheet
        visible={kindSheetVisible}
        title={t("availability.kind")}
        options={kindOptions}
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
