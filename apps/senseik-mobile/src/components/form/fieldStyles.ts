import { StyleSheet } from "react-native";
import { palette, radius, text } from "@/theme/theme";

/** Glass box shared by the native-picker fields (`DateField`, `TimeField`) - same look as `SelectField`. */
export const pickerFieldStyles = StyleSheet.create({
  container: {
    gap: 6,
  },
  label: {
    ...text.caption,
  },
  field: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    minHeight: 48,
    paddingHorizontal: 14,
    paddingVertical: 12,
    borderRadius: radius.button,
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
  },
  fieldDisabled: {
    opacity: 0.6,
  },
  fieldText: {
    ...text.body,
    fontVariant: ["tabular-nums"],
  },
});
