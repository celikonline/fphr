import React from "react";
import { StyleSheet, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { BottomSheet, PrimaryButton, TextField } from "@/components/ui";
import { palette, text } from "@/theme/theme";

type Props = {
  visible: boolean;
  reason: string;
  isDeciding: boolean;
  onChangeReason: (reason: string) => void;
  onCancel: () => void;
  onSubmit: () => void;
  onRequestClose: () => void;
  /** FG-008: overrides for the toplu red instance (default: single-task copy, kept unchanged). */
  title?: string;
  message?: string;
};

/**
 * Kontrolcü düzeltmesi (P07 tur 1): backend `ValidateDecisionComment` (denetim A10) reddi HER
 * zaman - tekli de, toplu da - boş/boşluk gerekçeyle 400'ler. Tek yerden karar: "Reddet" boş
 * gerekçeyle asla tıklanabilir olmasın, iki çağıran da bunu ayrıca yönetmez.
 */
export function isRejectSubmitDisabled(reason: string, isDeciding: boolean): boolean {
  return isDeciding || reason.trim().length === 0;
}

/**
 * Görev 8: red gerekçesi mevcut akışla (`approvalsRejectFlow.ts`) aynı, yalnızca görünüm bir
 * `Modal` yerine ortak `BottomSheet`'e taşındı. B19: hem donanım geri tuşu hem de arka plan
 * (scrim) dokunuşu `onRequestClose`'a gider - "Vazgeç" butonu ayrı bir `CANCEL` eylemi kalır.
 * FG-008: aynı kalıp `ApprovalsScreen`'in toplu red akışında da kullanılır (`title`/`message`
 * prop'larıyla) - gerekçe zorunluluğu ikisinde de burada, `isRejectSubmitDisabled`'la uygulanır.
 */
export function ApprovalRejectSheet({
  visible,
  reason,
  isDeciding,
  onChangeReason,
  onCancel,
  onSubmit,
  onRequestClose,
  title,
  message,
}: Props) {
  const { t } = useTranslation("approvals");
  return (
    <BottomSheet visible={visible} onClose={onRequestClose} title={title ?? t("reject.title")}>
      <Text style={styles.message}>{message ?? t("reject.message")}</Text>
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
      <Text style={styles.hint}>{t("reject.reasonHint")}</Text>
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
            disabled={isRejectSubmitDisabled(reason, isDeciding)}
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
  hint: {
    ...text.caption,
    color: palette.textFaint,
    marginTop: 6,
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
