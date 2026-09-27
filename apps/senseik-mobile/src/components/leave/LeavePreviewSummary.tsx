import React from "react";
import { ActivityIndicator, StyleSheet, Text, View } from "react-native";
import { SummaryRow } from "@/components/form/SummaryRow";
import { formatDays } from "@/lib/formatters";
import type { LeaveRequestPreview } from "@/services/LeaveService";
import { palette, text, tones } from "@/theme/theme";
import { filterRedundantWarnings, previewSummaryText } from "./leaveRequestRules";

type Props = {
  loading: boolean;
  preview: LeaveRequestPreview | null;
  error: string | null;
  requiresDocument: boolean;
};

/** B-mobil-006 preview as the form's "Süre: N gün" line, plus the deduction note and warnings. */
export function LeavePreviewSummary({ loading, preview, error, requiresDocument }: Props) {
  if (loading) {
    return <ActivityIndicator size="small" color={palette.white} style={styles.loading} />;
  }
  if (error) {
    return <Text style={styles.invalid}>{error}</Text>;
  }
  if (!preview) return null;

  const hasDays = preview.days > 0;
  const warnings = filterRedundantWarnings(preview.warnings, {
    previewDaysZero: !hasDays,
    requiresDocument,
  });

  return (
    <View style={styles.container}>
      <SummaryRow label="Süre" value={`${formatDays(preview.days)} gün`} invalid={!hasDays} />
      <Text style={hasDays ? styles.note : styles.invalid}>{previewSummaryText(preview)}</Text>
      {warnings.map((warning) => (
        <Text key={warning.code} style={styles.warning}>
          {warning.message}
        </Text>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: 6,
  },
  loading: {
    paddingVertical: 12,
  },
  note: {
    ...text.caption,
  },
  invalid: {
    ...text.caption,
    color: tones.danger.fg,
  },
  warning: {
    ...text.caption,
    color: tones.warning.fg,
  },
});
