import React, { useState } from "react";
import { ActivityIndicator, Alert, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { GlassCard, StatusPill } from "@/components/ui";
import { formatShortRange } from "@/components/form/dateRange";
import { REQUEST_OPTIONS } from "@/lib/requestOptions";
import { accents, fontFor, palette, text, tones, type Tone } from "@/theme/theme";
import {
  leaveService,
  type LeaveRequest,
  type LeaveRequestAttachment,
  type LeaveRequestStatus,
} from "@/services/LeaveService";
import { formatDate, formatDays } from "@/lib/formatters";
import i18n from "@/lib/i18n";
import {
  attachmentOpenErrorMessage,
  openLeaveAttachment,
  shouldIgnoreAttachmentTap,
} from "@/lib/attachmentDownload";

/** Shared with Taleplerim's leave/overtime cards so both screens render identical status badges. */
export function leaveStatusLabel(status: LeaveRequestStatus): string {
  return i18n.t(`leave:status.${status}`);
}

/** Status pill tones, shared with Taleplerim's leave/overtime cards (same status set). */
export const LEAVE_STATUS_TONES: Record<LeaveRequestStatus, Tone> = {
  pending: "warning",
  approved: "success",
  rejected: "danger",
  withdrawn: "neutral",
  cancelled: "neutral",
};

export function formatLeaveDateOnly(value: string): string {
  const date = new Date(`${value}T00:00:00`);
  if (Number.isNaN(date.getTime())) return value;
  return formatDate(date, { day: "2-digit", month: "short", year: "numeric" });
}

/**
 * senseik-mobile-011: the employee's own request-list attachments - lazy-loaded (not fetched until
 * the user expands the card), so viewing a long "Taleplerim" history never fires one request per
 * row. Loaded state is cached in this component's state once fetched: collapsing and re-expanding
 * does not refetch. `openingDocumentId` blocks a second tap while one download-url request/open is
 * in flight (senseik-mobile-011's double-tap guard).
 */
function LeaveRequestAttachmentsSection({ requestId }: { requestId: string }) {
  const { t } = useTranslation("leave");
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
      Alert.alert(t("common:alerts.error"), attachmentOpenErrorMessage());
    } finally {
      setOpeningDocumentId(null);
    }
  }

  return (
    <View>
      <TouchableOpacity
        style={styles.action}
        onPress={toggle}
        accessibilityRole="button"
        accessibilityLabel={expanded ? t("attachments.hide") : t("attachments.show")}
        hitSlop={ACTION_HIT_SLOP}
      >
        <Text style={styles.attachmentsToggleText}>
          {expanded ? t("attachments.hide") : t("attachments.show")}
        </Text>
      </TouchableOpacity>
      {expanded &&
        (loading ? (
          <ActivityIndicator size="small" color={palette.white} />
        ) : loadError ? (
          <Text style={styles.attachmentsError}>{t("attachments.loadError")}</Text>
        ) : attachments && attachments.length > 0 ? (
          attachments.map((attachment) => {
            const isOpening = openingDocumentId === attachment.documentId;
            return (
              <TouchableOpacity
                key={attachment.documentId}
                style={styles.attachmentRow}
                onPress={() => void openAttachment(attachment.documentId)}
                disabled={openingDocumentId !== null}
                accessibilityRole="button"
                accessibilityLabel={attachment.fileName}
              >
                {isOpening ? (
                  <ActivityIndicator size="small" color={palette.white} />
                ) : (
                  <Text style={styles.attachmentText}>{attachment.fileName}</Text>
                )}
              </TouchableOpacity>
            );
          })
        ) : (
          <Text style={styles.attachmentsEmpty}>{t("attachments.empty")}</Text>
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
  /** senseik-mobile-011: renders the lazy "Ekler" expand/collapse section - off by default so a
   * lightweight summary usage stays unaffected. */
  showAttachments?: boolean;
};

const LEAVE_ICON = REQUEST_OPTIONS.find((option) => option.kind === "leave");

/** İzinlerim card: type icon, type name, "29 Eyl → 3 Eki · 5 gün", status pill, actions. */
export function LeaveRequestListItem({
  request,
  onPress,
  onWithdraw,
  isWithdrawing = false,
  showAttachments = false,
}: Props) {
  const { t } = useTranslation("leave");
  const canWithdraw = Boolean(onWithdraw) && request.status === "pending";
  const typeName = request.leaveTypeName ?? request.leaveTypeCode;
  const accent = accents[LEAVE_ICON?.accent ?? "green"];

  const content = (
    <>
      <View style={styles.header}>
        <View style={[styles.icon, { backgroundColor: accent.bg }]}>
          <Ionicons name={LEAVE_ICON?.icon ?? "sunny-outline"} size={20} color={accent.fg} />
        </View>
        <View style={styles.texts}>
          <Text style={styles.title} numberOfLines={1}>
            {typeName}
          </Text>
          <Text style={styles.subtext}>
            {formatShortRange(request.startDate, request.endDate)} ·{" "}
            {t("common:units.days", {
              count: request.calculatedDays,
              value: formatDays(request.calculatedDays),
            })}
          </Text>
        </View>
        <StatusPill
          label={leaveStatusLabel(request.status)}
          tone={LEAVE_STATUS_TONES[request.status]}
        />
      </View>
      {canWithdraw && (
        <TouchableOpacity
          style={styles.action}
          onPress={onWithdraw}
          disabled={isWithdrawing}
          accessibilityRole="button"
          accessibilityLabel={t("withdraw.action")}
          hitSlop={ACTION_HIT_SLOP}
        >
          {isWithdrawing ? (
            <ActivityIndicator size="small" color={palette.coral} />
          ) : (
            <Text style={styles.withdrawText}>{t("withdraw.action")}</Text>
          )}
        </TouchableOpacity>
      )}
      {showAttachments && <LeaveRequestAttachmentsSection requestId={request.id} />}
    </>
  );

  if (!onPress) {
    return <GlassCard style={styles.card}>{content}</GlassCard>;
  }

  return (
    <GlassCard style={styles.card} onPress={onPress} accessibilityLabel={`${typeName} talebi`}>
      {content}
    </GlassCard>
  );
}

/** The text actions' visual height is ~16-20px; hitSlop brings the tappable area to >=44. */
const ACTION_HIT_SLOP = { top: 12, bottom: 12, left: 8, right: 8 };

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 20,
    marginBottom: 10,
  },
  header: {
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
  },
  icon: {
    width: 40,
    height: 40,
    borderRadius: 14,
    alignItems: "center",
    justifyContent: "center",
  },
  texts: {
    flex: 1,
    minWidth: 0,
  },
  title: {
    ...text.heading,
  },
  subtext: {
    ...text.caption,
    marginTop: 2,
    fontVariant: ["tabular-nums"],
  },
  action: {
    alignSelf: "flex-start",
    marginTop: 12,
    paddingVertical: 4,
  },
  withdrawText: {
    ...text.caption,
    color: palette.coral,
    ...fontFor(700),
  },
  attachmentsToggleText: {
    ...text.caption,
    color: palette.brandText,
    ...fontFor(700),
  },
  attachmentsError: {
    ...text.caption,
    color: tones.danger.fg,
  },
  attachmentsEmpty: {
    ...text.caption,
  },
  attachmentRow: {
    minHeight: 44,
    justifyContent: "center",
  },
  attachmentText: {
    ...text.caption,
    color: palette.brandText,
    textDecorationLine: "underline",
  },
});
