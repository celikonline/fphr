import React from "react";
import { View, Text, StyleSheet } from "react-native";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";

// İskelet (zaman-devam W0-c): bordro pusulası detayı, onay ve itiraz ilgili şeritte doldurulur.
export default function PayslipDetailScreen() {
  return (
    <View style={styles.container}>
      <Text style={styles.title}>Bordro pusulası</Text>
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
