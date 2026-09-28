import { StyleSheet } from "react-native";
import { palette, text } from "@/theme/theme";

/** Shared layout for the NewRequest body forms (overtime/expense/advance/leave). */
export const requestFormStyles = StyleSheet.create({
  form: {
    gap: 16,
  },
  textArea: {
    minHeight: 88,
    textAlignVertical: "top",
  },
  centered: {
    paddingVertical: 40,
    alignItems: "center",
    justifyContent: "center",
  },
  hint: {
    ...text.bodyMuted,
  },
  warning: {
    ...text.caption,
    color: palette.coral,
  },
  submit: {
    marginTop: 8,
  },
});
