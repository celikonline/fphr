import React from "react";
import { View, Text, StyleSheet } from "react-native";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";

export default function PayrollScreen() {
  return (
    <View style={styles.container}>
      <Text style={styles.title}>Bordro</Text>
      <Text style={styles.message}>Bordro bilgilerinizi web uygulamasından görüntüleyebilirsiniz.</Text>
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
