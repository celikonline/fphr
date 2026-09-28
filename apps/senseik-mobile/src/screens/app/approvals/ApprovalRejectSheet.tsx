import React from "react";
import { StyleSheet, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { BottomSheet, PrimaryButton, TextField } from "@/components/ui";
import { text } from "@/theme/theme";

type Props = {
  visible: boolean;
  reason: string;
  isDeciding: boolean;
  onChangeReason: (reason: string) => void;
  onCancel: () => void;
  onSubmit: () => void;
  onRequestClose: () => void;
};

/**
 * Görev 8: red gerekçesi mevcut akışla (`approvalsRejectFlow.ts`) aynı, yalnızca görünüm bir
 * `Modal` yerine ortak `BottomSheet`'e taşındı. B19: hem donanım geri tuşu hem de arka plan
 * (scrim) dokunuşu `onRequestClose`'a gider - "Vazgeç" butonu ayrı bir `CANCEL` eylemi kalır.
 */
export function ApprovalRejectSheet({
  visible,
  reason,
  isDeciding,
  onChangeReason,
  onCancel,
  onSubmit,
  onRequestClose,
}: Props) {
  const { t } = useTranslation("approvals");
  return (
    <BottomSheet visible={visible} onClose={onRequestClose} title={t("reject.title")}>
      <Text style={styles.message}>{t("reject.message")}</Text>
      <TextField
        label={t("reject.reason")}
        value={reason}
        onChangeText={onChangeReason}
        placeholder={t("reject.reasonPlaceholder")}
        multiline
        numberOfLines={3}
        editable={!isDeciding}
        style={styles.input}
      />
      <View style={styles.actionRow}>
        <View style={styles.actionButton}>
          <PrimaryButton
            label={t("common:actions.cancel")}
            variant="secondary"
            onPress={onCancel}
            disabled={isDeciding}
          />
        </View>
        <View style={styles.actionButton}>
          <PrimaryButton
            label={t("actions.reject")}
            variant="danger"
            onPress={onSubmit}
            disabled={isDeciding}
            loading={isDeciding}
          />
        </View>
      </View>
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  message: {
    ...text.bodyMuted,
    marginBottom: 16,
  },
  input: {
    minHeight: 72,
    textAlignVertical: "top",
  },
  actionRow: {
    flexDirection: "row",
    gap: 10,
    marginTop: 20,
  },
  actionButton: {
    flex: 1,
  },
});
