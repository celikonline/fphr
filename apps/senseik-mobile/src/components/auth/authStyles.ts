import { StyleSheet } from "react-native";
import { fontFor, palette, text } from "@/theme/theme";

/** Text/link styles shared by the secondary auth screens' glass panels (login's look). */
export const authStyles = StyleSheet.create({
  panelTitle: { ...text.title },
  panelSubtitle: { ...text.body, marginTop: 4, color: palette.ice },
  secondaryLink: {
    marginTop: 16,
    alignSelf: "center",
    minHeight: 44,
    justifyContent: "center",
    paddingHorizontal: 12,
  },
  secondaryLinkText: { fontSize: 14, color: palette.ice, ...fontFor(600) },
});
