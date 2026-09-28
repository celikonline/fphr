import React from "react";
import {
  ActivityIndicator,
  StyleSheet,
  Text,
  TouchableOpacity,
  type ViewStyle,
} from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { palette, radius, text } from "@/theme/theme";

type IoniconName = React.ComponentProps<typeof Ionicons>["name"];
type Variant = "primary" | "danger" | "secondary";

type Props = {
  label: string;
  onPress: () => void;
  variant?: Variant;
  loading?: boolean;
  disabled?: boolean;
  icon?: IoniconName;
};

const VARIANT_STYLES: Record<Variant, { container: ViewStyle; textColor: string }> = {
  primary: { container: { backgroundColor: palette.brand }, textColor: palette.white },
  danger: { container: { backgroundColor: palette.badge }, textColor: palette.white },
  secondary: {
    container: {
      backgroundColor: palette.glass,
      borderWidth: 1,
      borderColor: palette.glassBorder,
    },
    textColor: palette.white,
  },
};

export function PrimaryButton({
  label,
  onPress,
  variant = "primary",
  loading = false,
  disabled = false,
  icon,
}: Props) {
  const isDisabled = disabled || loading;
  const { container, textColor } = VARIANT_STYLES[variant];

  return (
    <TouchableOpacity
      style={[styles.button, container, isDisabled && styles.disabled]}
      onPress={onPress}
      disabled={isDisabled}
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled: isDisabled, busy: loading }}
    >
      {loading ? (
        <ActivityIndicator color={textColor} />
      ) : (
        <>
          {icon ? <Ionicons name={icon} size={18} color={textColor} /> : null}
          <Text style={[styles.label, { color: textColor }]}>{label}</Text>
        </>
      )}
    </TouchableOpacity>
  );
}

const styles = StyleSheet.create({
  button: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 8,
    minHeight: 48,
    borderRadius: radius.button,
    paddingHorizontal: 20,
  },
  disabled: {
    opacity: 0.5,
  },
  label: {
    ...text.button,
  },
});
