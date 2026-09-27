import React from "react";
import { ActivityIndicator, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";

export interface StagedDocument {
  id: string;
  name: string;
}

type Props = {
  documents: StagedDocument[];
  uploading: boolean;
  missingRequired: boolean;
  onCapturePhoto: () => void;
  onPickFile: () => void;
  onRemove: (documentId: string) => void;
};

/** "Belge ekle" bölümü (B-mobil-004): requiresDocument türlerinde kamera/dosya seçici + yüklenen liste. */
export function LeaveDocumentsSection({
  documents,
  uploading,
  missingRequired,
  onCapturePhoto,
  onPickFile,
  onRemove,
}: Props) {
  return (
    <>
      <Text style={styles.fieldLabel}>Belge ekle (zorunlu)</Text>
      <View style={styles.actionRow}>
        <TouchableOpacity
          style={styles.secondaryButton}
          onPress={onCapturePhoto}
          disabled={uploading}
        >
          <Text style={styles.secondaryButtonText}>Fotoğraf çek</Text>
        </TouchableOpacity>
        <TouchableOpacity style={styles.secondaryButton} onPress={onPickFile} disabled={uploading}>
          <Text style={styles.secondaryButtonText}>Dosya seç</Text>
        </TouchableOpacity>
      </View>
      {uploading && (
        <View style={styles.uploadingRow}>
          <ActivityIndicator size="small" color={colors.primary} />
          <Text style={styles.uploadingText}>Belge yükleniyor…</Text>
        </View>
      )}
      {documents.map((doc) => (
        <View key={doc.id} style={styles.documentRow}>
          <Text style={styles.documentName} numberOfLines={1}>
            {doc.name}
          </Text>
          <TouchableOpacity onPress={() => onRemove(doc.id)}>
            <Text style={styles.documentRemove}>Kaldır</Text>
          </TouchableOpacity>
        </View>
      ))}
      {missingRequired && (
        <Text style={styles.documentHint}>
          Bu izin türü için en az bir belge eklemeniz gerekir.
        </Text>
      )}
    </>
  );
}

const styles = StyleSheet.create({
  fieldLabel: {
    ...typography.caption,
    color: colors.textSecondary,
    marginTop: spacing.md,
    marginBottom: spacing.xs,
  },
  actionRow: {
    flexDirection: "row",
    gap: spacing.sm,
  },
  secondaryButton: {
    flex: 1,
    backgroundColor: colors.gray50,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 8,
    paddingVertical: spacing.sm,
    alignItems: "center",
  },
  secondaryButtonText: {
    ...typography.button,
    color: colors.text,
  },
  uploadingRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing.xs,
    marginTop: spacing.sm,
  },
  uploadingText: {
    ...typography.bodyXs,
    color: colors.textSecondary,
  },
  documentRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    marginTop: spacing.sm,
    paddingVertical: spacing.xs,
    borderBottomWidth: 1,
    borderBottomColor: colors.border,
  },
  documentName: {
    ...typography.bodySm,
    color: colors.text,
    flex: 1,
    marginRight: spacing.sm,
  },
  documentRemove: {
    ...typography.bodyXs,
    color: colors.error,
  },
  documentHint: {
    ...typography.bodyXs,
    color: colors.error,
    marginTop: spacing.xs,
  },
});
