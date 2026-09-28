import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { palette, radius, text } from "@/theme/theme";

type Props = {
  label: string;
  value?: string;
  placeholder: string;
  onPress: () => void;
  required?: boolean;
  error?: string;
};

/** Glass "select" field - opens a picker/`BottomSheet` on tap (dates, leave type, ...). */
export function SelectField({ label, value, placeholder, onPress, required, error }: Props) {
  return (
    <View style={styles.container}>
      <Text style={styles.label}>
        {label}
        {required ? " *" : ""}
      </Text>
      <TouchableOpacity
        style={[styles.input, error ? styles.inputError : null]}
        onPress={onPress}
        accessibilityRole="button"
        accessibilityLabel={label}
      >
        <Text style={value ? styles.value : styles.placeholder}>{value ?? placeholder}</Text>
        <Ionicons name="chevron-down" size={18} color={palette.textFaint} />
      </TouchableOpacity>
      {error ? <Text style={styles.error}>{error}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: 6,
  },
  label: {
    ...text.caption,
  },
  input: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
    borderRadius: radius.button,
    paddingHorizontal: 14,
    paddingVertical: 12,
    minHeight: 48,
  },
  inputError: {
    borderColor: palette.coral,
  },
  value: {
    ...text.body,
  },
  placeholder: {
    ...text.body,
    color: palette.textFaint,
  },
  error: {
    ...text.caption,
    color: palette.coral,
  },
});
