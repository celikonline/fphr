import React from "react";
import { StyleSheet, Text, View } from "react-native";
import { Avatar, GlassCard, PrimaryButton, StatusPill } from "@/components/ui";
import type { InboxTask } from "@/services/ApprovalsService";
import { fontFor, palette, text } from "@/theme/theme";
import { ApprovalAttachmentsLine } from "./ApprovalAttachmentsLine";
import { approvalCardHeading, approvalDetailRows } from "./approvalPresentation";

/** Web `home.tsx`'teki `SUBMITTED_DATE` biçimiyle aynı: `gg.aa.yyyy`. */
const SUBMITTED_DATE_FORMAT: Intl.DateTimeFormatOptions = {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
};

function formatSubmittedAt(submittedAt: string): string {
  return new Intl.DateTimeFormat("tr-TR", SUBMITTED_DATE_FORMAT).format(new Date(submittedAt));
}

/** Avatar satırının caption'ı - gerçek ünvan/departman alanı `InboxTask`'ta yok (bkz.
 * `ApprovalsService.ts`), bunun yerine adım no + gönderim tarihi + (varsa) yedek onaylayan notu. */
function approverCaption(task: InboxTask): string {
  const parts = [`Adım ${task.stepNumber}`];
  if (task.submittedAt) {
    parts.push(formatSubmittedAt(task.submittedAt));
  }
  if (task.isFallbackRoleTask) {
    parts.push("Yedek onaylayan");
  }
  return parts.join(" · ");
}

type Props = {
  task: InboxTask;
  isDeciding: boolean;
  isApproving: boolean;
  onApprove: () => void;
  onReject: () => void;
};

/** Görev 8: "Onaylarım" kart görünümü (spec §5 / ZenHR "My Approvals" referansı). */
export function ApprovalTaskCard({ task, isDeciding, isApproving, onApprove, onReject }: Props) {
  const { heading, subtitle } = approvalCardHeading(task);
  const detailRows = approvalDetailRows(task);
  const requesterName = task.requesterName ?? "Bilinmiyor";

  return (
    <GlassCard style={styles.card}>
      <View style={styles.headerRow}>
        <Avatar name={requesterName} size={44} radius={14} />
        <View style={styles.headerTexts}>
          <Text style={styles.name} numberOfLines={1}>
            {requesterName}
          </Text>
          <Text style={styles.caption} numberOfLines={1}>
            {approverCaption(task)}
          </Text>
        </View>
        <Text style={styles.chevron} accessibilityElementsHidden importantForAccessibility="no">
          ›
        </Text>
      </View>

      <View style={styles.amountRow}>
        <View style={styles.amountTexts}>
          <Text style={styles.amount}>{heading}</Text>
          {subtitle ? <Text style={styles.typeCaption}>{subtitle}</Text> : null}
        </View>
        <StatusPill label="Bekliyor" tone="warning" />
      </View>

      {detailRows.length > 0 && (
        <>
          <View style={styles.divider} />
          {detailRows.map((row) => (
            <View key={row.label} style={styles.detailRow}>
              <Text style={styles.detailLabel}>{row.label}</Text>
              <Text style={styles.detailValue}>{row.value}</Text>
            </View>
          ))}
        </>
      )}

      {task.summary?.leave?.description ? (
        <Text style={styles.note}>{task.summary.leave.description}</Text>
      ) : null}
      {task.summary?.overtime?.reason ? (
        <Text style={styles.note}>{task.summary.overtime.reason}</Text>
      ) : null}
      <ApprovalAttachmentsLine task={task} />

      <View style={styles.actionRow}>
        <View style={styles.actionButton}>
          <PrimaryButton
            label="Reddet"
            variant="danger"
            onPress={onReject}
            disabled={isDeciding}
            icon="close"
          />
        </View>
        <View style={styles.actionButton}>
          <PrimaryButton
            label="Onayla"
            onPress={onApprove}
            disabled={isDeciding}
            loading={isApproving}
            icon="checkmark"
          />
        </View>
      </View>
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 20,
    marginBottom: 12,
    gap: 12,
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
  },
  headerTexts: {
    flex: 1,
    minWidth: 0,
  },
  name: {
    ...text.heading,
  },
  caption: {
    ...text.caption,
    color: palette.brandText,
    marginTop: 2,
  },
  chevron: {
    fontSize: 20,
    color: palette.textFaint,
  },
  amountRow: {
    flexDirection: "row",
    alignItems: "flex-start",
    justifyContent: "space-between",
    gap: 12,
  },
  amountTexts: {
    flex: 1,
    minWidth: 0,
  },
  amount: {
    ...text.heading,
  },
  typeCaption: {
    ...text.caption,
    color: palette.brandText,
    marginTop: 2,
  },
  divider: {
    height: 1,
    backgroundColor: palette.line,
  },
  detailRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: 12,
  },
  detailLabel: {
    ...text.caption,
    ...fontFor(700),
  },
  detailValue: {
    ...text.body,
    ...fontFor(500),
  },
  note: {
    ...text.bodyMuted,
  },
  actionRow: {
    flexDirection: "row",
    gap: 10,
  },
  actionButton: {
    flex: 1,
  },
});
