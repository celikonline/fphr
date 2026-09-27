import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { BottomSheet } from "@/components/ui";
import { accents, fontFor, palette, text, type AccentKey } from "@/theme/theme";
import type { RequestKind } from "@/types/navigation";

type IoniconName = React.ComponentProps<typeof Ionicons>["name"];

type RequestOption = {
  kind: RequestKind;
  label: string;
  description: string;
  icon: IoniconName;
  accent: AccentKey;
};

/** The request types the FAB can start, in sheet order (mockup `.sheet .req`). */
export const REQUEST_OPTIONS: readonly RequestOption[] = [
  {
    kind: "leave",
    label: "İzin",
    description: "Yıllık, mazeret, raporlu…",
    icon: "sunny-outline",
    accent: "green",
  },
  {
    kind: "overtime",
    label: "Fazla mesai",
    description: "Planlanan veya yapılan mesai",
    icon: "time-outline",
    accent: "blue",
  },
  {
    kind: "expense",
    label: "Masraf",
    description: "Fiş veya fatura ile",
    icon: "receipt-outline",
    accent: "amber",
  },
  {
    kind: "advance",
    label: "Avans",
    description: "Maaştan mahsup edilir",
    icon: "wallet-outline",
    accent: "coral",
  },
];

type Props = {
  visible: boolean;
  onClose: () => void;
  onSelect: (kind: RequestKind) => void;
};

/** "Yeni talep" bottom sheet opened by the tab bar's FAB. */
export function RequestSheet({ visible, onClose, onSelect }: Props) {
  return (
    <BottomSheet visible={visible} onClose={onClose} title="Yeni talep">
      <Text style={styles.subtitle}>Ne için talep oluşturmak istiyorsun?</Text>
      {REQUEST_OPTIONS.map((option, index) => {
        const accent = accents[option.accent];
        const isLast = index === REQUEST_OPTIONS.length - 1;
        return (
          <TouchableOpacity
            key={option.kind}
            style={[styles.row, !isLast && styles.rowDivider]}
            onPress={() => onSelect(option.kind)}
            accessibilityRole="button"
            accessibilityLabel={option.label}
            accessibilityHint={option.description}
          >
            <View style={[styles.icon, { backgroundColor: accent.bg }]}>
              <Ionicons name={option.icon} size={22} color={accent.fg} />
            </View>
            <View style={styles.texts}>
              <Text style={styles.label}>{option.label}</Text>
              <Text style={styles.description}>{option.description}</Text>
            </View>
            <Ionicons name="chevron-forward" size={20} color={palette.textMuted} />
          </TouchableOpacity>
        );
      })}
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  subtitle: {
    ...text.bodyMuted,
    fontSize: 13,
    marginBottom: 10,
  },
  row: {
    flexDirection: "row",
    alignItems: "center",
    gap: 14,
    paddingVertical: 12,
    paddingHorizontal: 4,
  },
  rowDivider: {
    borderBottomWidth: 1,
    borderBottomColor: palette.line,
  },
  icon: {
    width: 44,
    height: 44,
    borderRadius: 14,
    alignItems: "center",
    justifyContent: "center",
  },
  texts: {
    flex: 1,
  },
  label: {
    ...text.body,
    ...fontFor(800),
  },
  description: {
    ...text.caption,
    fontSize: 12.5,
  },
});
