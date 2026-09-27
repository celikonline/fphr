import React, { useState } from "react";
import { ScrollView, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import ExpenseClaimSection from "@/components/requests/ExpenseClaimSection";
import AdvanceRequestSection from "@/components/requests/AdvanceRequestSection";
import OvertimeRequestSection from "@/components/requests/OvertimeRequestSection";

/** TK-031: the three request types this screen can create, in tab order. */
export type RequestSegment = "expense" | "advance" | "overtime";

export const REQUEST_SEGMENT_LABELS: Record<RequestSegment, string> = {
  expense: "Harcama",
  advance: "Avans",
  overtime: "Fazla mesai",
};

const REQUEST_SEGMENTS: RequestSegment[] = ["expense", "advance", "overtime"];

export default function RequestsScreen() {
  const [segment, setSegment] = useState<RequestSegment>("expense");

  return (
    <ScrollView style={styles.container} contentContainerStyle={styles.content}>
      <Text style={styles.title}>Taleplerim</Text>

      <View style={styles.segmentRow}>
        {REQUEST_SEGMENTS.map((option) => {
          const selected = option === segment;
          return (
            <TouchableOpacity
              key={option}
              style={[styles.segmentButton, selected && styles.segmentButtonSelected]}
              onPress={() => setSegment(option)}
            >
              <Text
                style={[styles.segmentButtonText, selected && styles.segmentButtonTextSelected]}
              >
                {REQUEST_SEGMENT_LABELS[option]}
              </Text>
            </TouchableOpacity>
          );
        })}
      </View>

      {segment === "expense" && <ExpenseClaimSection />}
      {segment === "advance" && <AdvanceRequestSection />}
      {segment === "overtime" && <OvertimeRequestSection />}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.background,
  },
  content: {
    padding: spacing.xl,
    paddingBottom: spacing.xxxl,
  },
  title: {
    ...typography.h3,
    color: colors.text,
    marginBottom: spacing.lg,
  },
  segmentRow: {
    flexDirection: "row",
    backgroundColor: colors.gray50,
    borderRadius: 10,
    padding: 4,
    marginBottom: spacing.lg,
  },
  segmentButton: {
    flex: 1,
    paddingVertical: spacing.sm,
    borderRadius: 8,
    alignItems: "center",
  },
  segmentButtonSelected: {
    backgroundColor: colors.white,
  },
  segmentButtonText: {
    ...typography.bodySm,
    color: colors.textSecondary,
  },
  segmentButtonTextSelected: {
    color: colors.text,
    fontWeight: "600",
  },
});
