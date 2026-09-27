import React from "react";
import { StyleSheet, Text, View } from "react-native";
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
  return (
    <BottomSheet visible={visible} onClose={onRequestClose} title="Talebi reddet">
      <Text style={styles.message}>
        Bu talebi reddetmek istediğinize emin misiniz? Bu işlem geri alınamaz.
      </Text>
      <TextField
        label="Gerekçe (opsiyonel)"
        value={reason}
        onChangeText={onChangeReason}
        placeholder="Gerekçenizi yazın"
        multiline
        numberOfLines={3}
        editable={!isDeciding}
        style={styles.input}
      />
      <View style={styles.actionRow}>
        <View style={styles.actionButton}>
          <PrimaryButton
            label="Vazgeç"
            variant="secondary"
            onPress={onCancel}
            disabled={isDeciding}
          />
        </View>
        <View style={styles.actionButton}>
          <PrimaryButton
            label="Reddet"
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
