import React, { useEffect, useState } from "react";
import { ActivityIndicator, Alert, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { useTranslation } from "react-i18next";
import {
  attachmentOpenErrorMessage,
  openLeaveAttachment,
  shouldIgnoreAttachmentTap,
} from "@/lib/attachmentDownload";
import { leaveService, type LeaveRequestAttachment } from "@/services/LeaveService";
import type { InboxTask } from "@/services/ApprovalsService";
import { palette, text } from "@/theme/theme";
import { attachmentsCountLabel, loadLeaveTaskAttachments } from "./approvalAttachments";

/**
 * A1: one card's attachment line - a dedicated component (not inline state in `ApprovalsScreen`) so
 * React's list reconciliation (`key={task.taskId}` on the card) gives "fetch once per card, cached"
 * for free: this only remounts (and re-fetches) when the task itself is a different task, not on
 * every inbox refresh of the same still-pending list.
 *
 * senseik-mobile-011: each attachment name is now its own tappable row - tapping fetches a fresh
 * 15-minute presigned download URL (manager-scoped, since this is the approver's inbox) and opens
 * it; `openingDocumentId` blocks a second tap while one download-url request/open is in flight.
 */
export function ApprovalAttachmentsLine({ task }: { task: InboxTask }) {
  const { t } = useTranslation();
  const [attachments, setAttachments] = useState<LeaveRequestAttachment[]>([]);
  const [openingDocumentId, setOpeningDocumentId] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void loadLeaveTaskAttachments(task).then((result) => {
      if (!cancelled) {
        setAttachments(result);
      }
    });
    return () => {
      cancelled = true;
    };
    // `task.taskId` identifies the card; the other fields it reads (requestType/requesterEmployeeId/
    // subjectId) never change for the same task, so re-running only when the id changes is correct.
  }, [task.taskId]);

  async function openAttachment(documentId: string) {
    const requesterEmployeeId = task.requesterEmployeeId;
    if (shouldIgnoreAttachmentTap(openingDocumentId) || !requesterEmployeeId) {
      return;
    }
    setOpeningDocumentId(documentId);
    try {
      await openLeaveAttachment(() =>
        leaveService.getEmployeeAttachmentDownloadUrl(
          requesterEmployeeId,
          task.subjectId,
          documentId
        )
      );
    } catch {
      Alert.alert(t("alerts.error"), attachmentOpenErrorMessage());
    } finally {
      setOpeningDocumentId(null);
    }
  }

  if (attachments.length === 0) {
    return null;
  }

  return (
    <View>
      <Text style={styles.summaryText}>{attachmentsCountLabel(attachments.length)}</Text>
      {attachments.map((attachment) => {
        const isOpening = openingDocumentId === attachment.documentId;
        return (
          <TouchableOpacity
            key={attachment.documentId}
            onPress={() => void openAttachment(attachment.documentId)}
            disabled={openingDocumentId !== null}
            style={styles.attachmentRow}
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
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  summaryText: {
    ...text.bodyMuted,
    marginTop: 6,
  },
  attachmentRow: {
    paddingVertical: 8,
    minHeight: 44,
    justifyContent: "center",
  },
  attachmentText: {
    ...text.bodyMuted,
    color: palette.brandText,
    textDecorationLine: "underline",
  },
});
