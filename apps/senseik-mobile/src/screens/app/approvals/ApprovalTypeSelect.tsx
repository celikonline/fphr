import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { palette, text } from "@/theme/theme";
import { approvalFilterSelectedText, type ApprovalFilter } from "./approvalFilters";

type Props = {
  filter: ApprovalFilter;
  counts: Record<ApprovalFilter, number>;
  onPress: () => void;
};

/** Görev 8: "Tür" etiketi + brandText renkli seçili değer, alt sayfayı açar (spec §5). */
export function ApprovalTypeSelect({ filter, counts, onPress }: Props) {
  const { t } = useTranslation("approvals");
  const selectedText = approvalFilterSelectedText(filter, counts);

  return (
    <View style={styles.container}>
      <Text style={styles.label}>{t("typeLabel")}</Text>
      <TouchableOpacity
        style={styles.row}
        onPress={onPress}
        accessibilityRole="button"
        accessibilityLabel={t("typeA11y", { value: selectedText })}
      >
        <Text style={styles.value}>{selectedText}</Text>
        <Ionicons name="chevron-down" size={18} color={palette.brandText} />
      </TouchableOpacity>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    marginHorizontal: 20,
    marginBottom: 12,
    gap: 4,
  },
  label: {
    ...text.caption,
  },
  row: {
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
    minHeight: 44,
  },
  value: {
    ...text.body,
    color: palette.brandText,
  },
});
