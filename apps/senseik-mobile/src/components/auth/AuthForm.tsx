import React from "react";
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from "react-native";
import { sectionTitleText } from "@/components/ui/SectionHeader";
import { fontFor, palette, radius, text, tones } from "@/theme/theme";

/** Shared TextInput color props of the auth forms (placeholder + caret/selection). */
export const authInputColors = {
  placeholderTextColor: palette.textFaint,
  selectionColor: palette.brand,
} as const;

/** "Algosense güvencesiyle · © <year>" line under the auth panels. */
export function AuthFooter() {
  return <Text style={styles.footer}>Algosense güvencesiyle · © {new Date().getFullYear()}</Text>;
}

/** Inline form error of the auth panels (wrong credentials, empty field, failed biometric...). */
export function AuthFormError({ message }: { message: string }) {
  return (
    <View style={styles.errorBanner} accessibilityLiveRegion="polite" accessibilityRole="alert">
      <Text style={styles.errorText}>{message}</Text>
    </View>
  );
}

type AuthFieldProps = { label: string; focused: boolean; children: React.ReactNode };

/**
 * Labelled glass input wrapper with a brand focus ring. The label is uppercased with Turkish casing
 * (`sectionTitleText`, "tr-TR") - never `textTransform`, which Android applies with the device
 * locale and mis-cases "i"/"ı".
 */
export function AuthField({ label, focused, children }: AuthFieldProps) {
  return (
    <View style={styles.fieldGroup}>
      <Text style={styles.fieldLabel}>{sectionTitleText(label)}</Text>
      <View style={[styles.inputWrapper, focused && styles.inputWrapperFocused]}>{children}</View>
    </View>
  );
}

type AuthSubmitButtonProps = {
  label: string;
  onPress: () => void;
  disabled: boolean;
  loading: boolean;
};

/** The auth panels' brand submit button; shows a spinner instead of the label while loading. */
export function AuthSubmitButton({ label, onPress, disabled, loading }: AuthSubmitButtonProps) {
  return (
    <Pressable
      style={({ pressed }) => [styles.primaryButton, pressed && styles.primaryButtonPressed]}
      onPress={onPress}
      disabled={disabled}
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled, busy: loading }}
    >
      {loading ? (
        <ActivityIndicator color={palette.white} />
      ) : (
        <Text style={styles.primaryButtonText}>{label}</Text>
      )}
    </Pressable>
  );
}

/** Input text style of the auth fields (fills the `AuthField` wrapper). */
export const authFormStyles = StyleSheet.create({
  input: { flex: 1, height: "100%", fontSize: 15, color: palette.white, ...fontFor(500) },
});

const styles = StyleSheet.create({
  footer: { ...text.caption, marginTop: 16, textAlign: "center" },
  errorBanner: {
    marginTop: 16,
    padding: 12,
    borderRadius: radius.button,
    backgroundColor: tones.danger.bg,
    borderWidth: 1,
    borderColor: palette.dangerBorder,
  },
  errorText: { ...text.bodyMuted, color: tones.danger.fg },
  fieldGroup: { marginTop: 18 },
  fieldLabel: { ...text.caption, letterSpacing: 0.6, color: palette.ice, marginBottom: 6 },
  inputWrapper: {
    height: 52,
    borderRadius: radius.button,
    backgroundColor: palette.glassInner,
    borderWidth: 1,
    borderColor: palette.glassBorder,
    flexDirection: "row",
    alignItems: "center",
    paddingHorizontal: 16,
  },
  inputWrapperFocused: {
    borderColor: palette.brand,
    boxShadow: `0 0 0 3px ${palette.focusRing}`,
  },
  primaryButton: {
    marginTop: 24,
    height: 54,
    borderRadius: radius.button,
    backgroundColor: palette.brand,
    alignItems: "center",
    justifyContent: "center",
    boxShadow: `0 10px 24px ${palette.brandGlow}`,
  },
  primaryButtonPressed: { transform: [{ scale: 0.98 }] },
  primaryButtonText: { ...text.button },
});
