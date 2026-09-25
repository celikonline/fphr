import React from "react";
import { View, Text, StyleSheet } from "react-native";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";

// İskelet (zaman-devam W0-c): kayıt (punch) düzeltme talebi formu ilgili şeritte doldurulur.
export default function PunchRequestScreen() {
  return (
    <View style={styles.container}>
      <Text style={styles.title}>Kayıt talebi</Text>
      <Text style={styles.message}>Bu ekran yakında kullanıma açılacak.</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    padding: spacing.xl,
    justifyContent: "center",
    alignItems: "center",
    backgroundColor: colors.background
  },
  title: {
    ...typography.h3,
    color: colors.text
  },
  message: {
    ...typography.bodySm,
    color: colors.textSecondary,
    marginTop: spacing.md,
    textAlign: "center"
  }
});
