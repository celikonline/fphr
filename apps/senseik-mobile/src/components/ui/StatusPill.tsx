import React from "react";
import { StyleSheet, Text, View } from "react-native";
import { radius, text, tones, type Tone } from "@/theme/theme";

type Props = {
  label: string;
  tone: Tone;
};

/** Rounded status badge (Onaylandı/Bekliyor/Reddedildi/...) using the shared `tones` palette. */
export function StatusPill({ label, tone }: Props) {
  const toneStyle = tones[tone];

  return (
    <View style={[styles.container, { backgroundColor: toneStyle.bg }]}>
      <Text style={[styles.label, { color: toneStyle.fg }]}>{label}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    alignSelf: "flex-start",
    borderRadius: radius.pill,
    paddingHorizontal: 10,
    paddingVertical: 4,
  },
  label: {
    ...text.caption,
  },
});
