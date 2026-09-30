import React from "react";
import { ScrollView, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { BottomSheet } from "@/components/ui";
import { fontFor, palette, text } from "@/theme/theme";

export type SheetOption<T extends string> = {
  value: T;
  label: string;
  description?: string;
};

type Props<T extends string> = {
  visible: boolean;
  title: string;
  options: SheetOption<T>[];
  selected: T | null;
  onSelect: (value: T) => void;
  onClose: () => void;
};

/** Bottom-sheet single-choice list (leave type, expense category, availability kind, filters). */
export function OptionSheet<T extends string>({
  visible,
  title,
  options,
  selected,
  onSelect,
  onClose,
}: Props<T>) {
  return (
    <BottomSheet visible={visible} onClose={onClose} title={title}>
      <ScrollView style={styles.list}>
        {options.map((option, index) => {
          const isSelected = option.value === selected;
          return (
            <TouchableOpacity
              key={option.value}
              style={[styles.row, index < options.length - 1 && styles.rowDivider]}
              onPress={() => {
                onSelect(option.value);
                onClose();
              }}
              accessibilityRole="button"
              accessibilityLabel={option.label}
              accessibilityState={{ selected: isSelected }}
            >
              <View style={styles.texts}>
                <Text style={[styles.label, isSelected && styles.labelSelected]}>
                  {option.label}
                </Text>
                {option.description ? (
                  <Text style={styles.description}>{option.description}</Text>
                ) : null}
              </View>
              {isSelected ? (
                <Ionicons name="checkmark-circle" size={22} color={palette.brand} />
              ) : null}
            </TouchableOpacity>
          );
        })}
      </ScrollView>
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  list: {
    maxHeight: 420,
  },
  row: {
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
    minHeight: 52,
    paddingVertical: 10,
  },
  rowDivider: {
    borderBottomWidth: 1,
    borderBottomColor: palette.line,
  },
  texts: {
    flex: 1,
  },
  label: {
    ...text.body,
  },
  labelSelected: {
    ...fontFor(800),
  },
  description: {
    ...text.caption,
    marginTop: 2,
  },
});
