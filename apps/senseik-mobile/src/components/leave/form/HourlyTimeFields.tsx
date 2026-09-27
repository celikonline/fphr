import React from "react";
import { StyleSheet, Text, View } from "react-native";
import { SummaryRow } from "@/components/form/SummaryRow";
import { TimeField } from "@/components/form/TimeField";
import { requestFormStyles as formStyles } from "@/components/requests/requestFormStyles";

type Props = {
  startTime: string;
  endTime: string;
  onStartTimeChange: (value: string) => void;
  onEndTimeChange: (value: string) => void;
  /** "2 sa 30 dk" when the range is valid, otherwise null (end not after start). */
  duration: string | null;
};

/** Start/end time pickers of an hourly leave request, with the duration or an ordering warning. */
export function HourlyTimeFields({
  startTime,
  endTime,
  onStartTimeChange,
  onEndTimeChange,
  duration,
}: Props) {
  return (
    <>
      <View style={styles.timeRow}>
        <View style={styles.timeField}>
          <TimeField
            label="Başlangıç saati"
            required
            value={startTime}
            onChange={onStartTimeChange}
          />
        </View>
        <View style={styles.timeField}>
          <TimeField label="Bitiş saati" required value={endTime} onChange={onEndTimeChange} />
        </View>
      </View>
      {duration ? (
        <SummaryRow label="Saat" value={duration} />
      ) : (
        <Text style={formStyles.warning}>Bitiş saati başlangıçtan sonra olmalı</Text>
      )}
    </>
  );
}

const styles = StyleSheet.create({
  timeRow: {
    flexDirection: "row",
    gap: 12,
  },
  timeField: {
    flex: 1,
  },
});
