import React from "react";
import { StyleSheet, Text, TextInput, View, type TextInputProps } from "react-native";
import { fontFor, palette, radius, text } from "@/theme/theme";

type Props = TextInputProps & {
  label: string;
  required?: boolean;
  error?: string;
};

/** Glass text input with a label above and an optional inline error below. */
export function TextField({ label, required, error, style, ...inputProps }: Props) {
  return (
    <View style={styles.container}>
      <Text style={styles.label}>
        {label}
        {required ? " *" : ""}
      </Text>
      <TextInput
        style={[styles.input, error ? styles.inputError : null, style]}
        placeholderTextColor={palette.textFaint}
        accessibilityLabel={label}
        {...inputProps}
      />
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
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
    borderRadius: radius.button,
    paddingHorizontal: 14,
    paddingVertical: 12,
    fontSize: 15,
    color: palette.white,
    ...fontFor(500),
  },
  inputError: {
    borderColor: palette.coral,
  },
  error: {
    ...text.caption,
    color: palette.coral,
  },
});
