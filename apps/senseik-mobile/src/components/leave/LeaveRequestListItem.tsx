import React, { useState } from "react";
import { ActivityIndicator, Alert, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import {
  leaveService,
  type LeaveRequest,
  type LeaveRequestAttachment,
  type LeaveRequestStatus,
} from "@/services/LeaveService";
import { formatDays } from "@/lib/formatters";
import {
  ATTACHMENT_OPEN_ERROR_MESSAGE,
  openLeaveAttachment,
  shouldIgnoreAttachmentTap,
} from "@/lib/attachmentDownload";

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

/**
 * senseik-mobile-011: the employee's own request-list attachments - lazy-loaded (not fetched until
 * the user expands the card), so viewing a long "Taleplerim" history never fires one request per
 * row. Loaded state is cached in this component's state once fetched: collapsing and re-expanding
 * does not refetch. `openingDocumentId` blocks a second tap while one download-url request/open is
 * in flight (senseik-mobile-011's double-tap guard).
 */
function LeaveRequestAttachmentsSection({ requestId }: { requestId: string }) {
  const [expanded, setExpanded] = useState(false);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState(false);
  const [attachments, setAttachments] = useState<LeaveRequestAttachment[] | null>(null);
  const [openingDocumentId, setOpeningDocumentId] = useState<string | null>(null);

  function toggle() {
    if (expanded) {
      setExpanded(false);
      return;
    }
    setExpanded(true);
    if (attachments !== null || loading) {
      return;
    }
    setLoading(true);
    setLoadError(false);
    leaveService
      .getMyLeaveRequest(requestId)
      .then((detail) => setAttachments(detail.attachments ?? []))
      .catch(() => setLoadError(true))
      .finally(() => setLoading(false));
  }

  async function openAttachment(documentId: string) {
    if (shouldIgnoreAttachmentTap(openingDocumentId)) {
      return;
    }
    setOpeningDocumentId(documentId);
    try {
      await openLeaveAttachment(() =>
        leaveService.getMyAttachmentDownloadUrl(requestId, documentId)
      );
    } catch {
      Alert.alert("Hata", ATTACHMENT_OPEN_ERROR_MESSAGE);
    } finally {
      setOpeningDocumentId(null);
    }
  }

  return (
    <View>
      <TouchableOpacity style={styles.attachmentsToggle} onPress={toggle}>
        <Text style={styles.attachmentsToggleText}>{expanded ? "Ekleri gizle" : "Ekler"}</Text>
      </TouchableOpacity>
      {expanded &&
        (loading ? (
          <ActivityIndicator size="small" color={colors.primary} />
        ) : loadError ? (
          <Text style={styles.attachmentsError}>Ekler yüklenemedi.</Text>
        ) : attachments && attachments.length > 0 ? (
          attachments.map((attachment) => {
            const isOpening = openingDocumentId === attachment.documentId;
            return (
              <TouchableOpacity
                key={attachment.documentId}
                style={styles.attachmentRow}
                onPress={() => void openAttachment(attachment.documentId)}
                disabled={openingDocumentId !== null}
              >
                {isOpening ? (
                  <ActivityIndicator size="small" color={colors.primary} />
                ) : (
                  <Text style={styles.attachmentText}>{attachment.fileName}</Text>
                )}
              </TouchableOpacity>
            );
          })
        ) : (
          <Text style={styles.attachmentsEmpty}>Ek yok.</Text>
        ))}
    </View>
  );
}

type Props = {
  request: LeaveRequest;
  onPress?: () => void;
  /** H1: renders a "Geri çek" action, shown only when the request is still `pending`. */
  onWithdraw?: () => void;
  isWithdrawing?: boolean;
  /** senseik-mobile-011: renders the lazy "Ekler" expand/collapse section - off by default so the
   * lightweight `ProfileScreen` "recent requests" summary usage is unaffected. */
  showAttachments?: boolean;
};

export function LeaveRequestListItem({
  request,
  onPress,
  onWithdraw,
  isWithdrawing = false,
  showAttachments = false,
}: Props) {
  const canWithdraw = Boolean(onWithdraw) && request.status === "pending";

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
        {formatDays(request.calculatedDays)} gün
      </Text>
      {canWithdraw && (
        <TouchableOpacity
          style={styles.withdrawButton}
          onPress={onWithdraw}
          disabled={isWithdrawing}
        >
          {isWithdrawing ? (
            <ActivityIndicator size="small" color={colors.error} />
          ) : (
            <Text style={styles.withdrawButtonText}>Geri çek</Text>
          )}
        </TouchableOpacity>
      )}
      {showAttachments && <LeaveRequestAttachmentsSection requestId={request.id} />}
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
  withdrawButton: {
    alignSelf: "flex-start",
    marginTop: spacing.sm,
    paddingVertical: spacing.xs,
  },
  withdrawButtonText: {
    ...typography.bodyXs,
    color: colors.error,
    fontWeight: "600",
  },
  attachmentsToggle: {
    alignSelf: "flex-start",
    marginTop: spacing.sm,
    paddingVertical: spacing.xs,
  },
  attachmentsToggleText: {
    ...typography.bodyXs,
    color: colors.primary,
    fontWeight: "600",
  },
  attachmentsError: {
    ...typography.bodyXs,
    color: colors.error,
  },
  attachmentsEmpty: {
    ...typography.bodyXs,
    color: colors.textTertiary,
  },
  attachmentRow: {
    paddingVertical: spacing.xs,
  },
  attachmentText: {
    ...typography.bodyXs,
    color: colors.primary,
    textDecorationLine: "underline",
  },
});
