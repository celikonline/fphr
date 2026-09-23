import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import type { LeaveRequest, LeaveRequestStatus } from "@/services/LeaveService";

/** Shared with `LeaveScreen`'s own request list so both screens render identical status badges. */
export const LEAVE_STATUS_LABELS: Record<LeaveRequestStatus, string> = {
  pending: "Onay bekliyor",
  approved: "Onaylandı",
  rejected: "Reddedildi",
  withdrawn: "Geri çekildi",
  cancelled: "İptal edildi",
};

export const LEAVE_STATUS_COLORS: Record<LeaveRequestStatus, string> = {
  pending: colors.warning,
  approved: colors.success,
  rejected: colors.error,
  withdrawn: colors.textTertiary,
  cancelled: colors.textTertiary,
};

export function formatLeaveDateOnly(value: string): string {
  const date = new Date(`${value}T00:00:00`);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleDateString("tr-TR", { day: "2-digit", month: "short", year: "numeric" });
}

type Props = {
  request: LeaveRequest;
  onPress?: () => void;
};

export function LeaveRequestListItem({ request, onPress }: Props) {
  const content = (
    <>
      <View style={styles.header}>
        <Text style={styles.label}>{request.leaveTypeName ?? request.leaveTypeCode}</Text>
        <View
          style={[styles.statusBadge, { backgroundColor: LEAVE_STATUS_COLORS[request.status] }]}
        >
          <Text style={styles.statusBadgeText}>{LEAVE_STATUS_LABELS[request.status]}</Text>
        </View>
      </View>
      <Text style={styles.subtext}>
        {formatLeaveDateOnly(request.startDate)} – {formatLeaveDateOnly(request.endDate)} ·{" "}
        {request.calculatedDays} gün
      </Text>
    </>
  );

  if (!onPress) {
    return <View style={styles.card}>{content}</View>;
  }

  return (
    <TouchableOpacity style={styles.card} onPress={onPress}>
      {content}
    </TouchableOpacity>
  );
}

const styles = StyleSheet.create({
  card: {
    backgroundColor: colors.white,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 12,
    padding: spacing.lg,
    marginBottom: spacing.sm,
  },
  header: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
  },
  label: {
    ...typography.bodySm,
    color: colors.textSecondary,
  },
  subtext: {
    ...typography.bodyXs,
    color: colors.textTertiary,
    marginTop: spacing.xs,
  },
  statusBadge: {
    borderRadius: 12,
    paddingHorizontal: spacing.sm,
    paddingVertical: 2,
  },
  statusBadgeText: {
    ...typography.caption,
    color: colors.white,
  },
});
