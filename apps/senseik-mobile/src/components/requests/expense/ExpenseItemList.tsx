import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { SummaryRow } from "@/components/form/SummaryRow";
import { formatDayMonthYear } from "@/components/form/dateRange";
import { formatCurrencyTRY } from "@/lib/formatters";
import { palette, radius, text, tones } from "@/theme/theme";

export type ExpenseItemRow = {
  localId: string;
  categoryLabel: string;
  receiptDate: string;
  amount: number;
};

type Props = {
  items: ExpenseItemRow[];
  total: number;
  onRemove: (localId: string) => void;
  disabled?: boolean;
};

/** "Eklenen kalemler" list of a multi-item expense claim being drafted, with its total (TK-031). */
export function ExpenseItemList({ items, total, onRemove, disabled = false }: Props) {
  const { t } = useTranslation("requests");
  return (
    <View style={styles.container}>
      <Text style={styles.title}>{t("expense.itemsTitle")}</Text>
      {items.map((item) => {
        const summary = `${formatDayMonthYear(item.receiptDate)} · ${formatCurrencyTRY(item.amount)}`;
        return (
          <View key={item.localId} style={styles.row}>
            <View
              style={styles.texts}
              accessible
              accessibilityLabel={`${item.categoryLabel}, ${summary}`}
            >
              <Text style={styles.category}>{item.categoryLabel}</Text>
              <Text style={styles.detail}>{summary}</Text>
            </View>
            <TouchableOpacity
              style={styles.remove}
              onPress={() => onRemove(item.localId)}
              disabled={disabled}
              accessibilityRole="button"
              accessibilityLabel={t("expense.removeItemA11y", {
                item: `${item.categoryLabel}, ${summary}`,
              })}
              accessibilityState={{ disabled }}
            >
              <Ionicons name="trash-outline" size={20} color={tones.danger.fg} />
            </TouchableOpacity>
          </View>
        );
      })}
      <SummaryRow label={t("expense.total")} value={formatCurrencyTRY(total)} />
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: 8,
  },
  title: {
    ...text.caption,
  },
  row: {
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
    minHeight: 56,
    paddingLeft: 14,
    paddingRight: 4,
    paddingVertical: 8,
    borderRadius: radius.button,
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
  },
  texts: {
    flex: 1,
  },
  category: {
    ...text.body,
  },
  detail: {
    ...text.caption,
  },
  remove: {
    width: 44,
    height: 44,
    alignItems: "center",
    justifyContent: "center",
  },
});
