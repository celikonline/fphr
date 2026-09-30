import React from "react";
import { ActivityIndicator, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { accents, fontFor, palette, radius, text, tones, type AccentKey } from "@/theme/theme";

type IoniconName = React.ComponentProps<typeof Ionicons>["name"];

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
  const { t } = useTranslation("leave");
  return (
    <View style={styles.container}>
      <Text style={styles.label}>{t("documents.add")} *</Text>
      <View style={styles.actionRow}>
        <PickerTile
          icon="camera-outline"
          accent="blue"
          label={t("documents.takePhoto")}
          onPress={onCapturePhoto}
          disabled={uploading}
        />
        <PickerTile
          icon="document-attach-outline"
          accent="purple"
          label={t("documents.pickFile")}
          onPress={onPickFile}
          disabled={uploading}
        />
      </View>
      {uploading ? (
        <View style={styles.uploadingRow}>
          <ActivityIndicator size="small" color={palette.white} />
          <Text style={styles.caption}>{t("documents.uploading")}</Text>
        </View>
      ) : null}
      {documents.map((doc) => (
        <View key={doc.id} style={styles.documentRow}>
          <Ionicons name="document-outline" size={18} color={palette.ice} />
          <Text style={styles.documentName} numberOfLines={1}>
            {doc.name}
          </Text>
          <TouchableOpacity
            onPress={() => onRemove(doc.id)}
            accessibilityRole="button"
            accessibilityLabel={t("documents.removeA11y", { name: doc.name })}
            hitSlop={{ top: 14, bottom: 14, left: 12, right: 12 }}
          >
            <Text style={styles.remove}>{t("common:actions.remove")}</Text>
          </TouchableOpacity>
        </View>
      ))}
      {missingRequired ? <Text style={styles.hint}>{t("blockers.documentRequired")}</Text> : null}
    </View>
  );
}

function PickerTile({
  icon,
  accent,
  label,
  onPress,
  disabled,
}: {
  icon: IoniconName;
  accent: AccentKey;
  label: string;
  onPress: () => void;
  disabled: boolean;
}) {
  const accentStyle = accents[accent];
  return (
    <TouchableOpacity
      style={[styles.tile, disabled && styles.tileDisabled]}
      onPress={onPress}
      disabled={disabled}
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled }}
    >
      <View style={[styles.tileIcon, { backgroundColor: accentStyle.bg }]}>
        <Ionicons name={icon} size={24} color={accentStyle.fg} />
      </View>
      <Text style={styles.tileLabel}>{label}</Text>
    </TouchableOpacity>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: 8,
  },
  label: {
    ...text.caption,
  },
  actionRow: {
    flexDirection: "row",
    gap: 12,
  },
  tile: {
    flex: 1,
    alignItems: "center",
    gap: 8,
    paddingVertical: 16,
    borderRadius: radius.tile,
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
  },
  tileDisabled: {
    opacity: 0.5,
  },
  tileIcon: {
    width: 48,
    height: 48,
    borderRadius: 16,
    alignItems: "center",
    justifyContent: "center",
  },
  tileLabel: {
    ...text.caption,
    color: palette.white,
    ...fontFor(700),
  },
  uploadingRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
  },
  caption: {
    ...text.caption,
  },
  documentRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
    paddingVertical: 10,
    borderBottomWidth: 1,
    borderBottomColor: palette.line,
  },
  documentName: {
    ...text.body,
    flex: 1,
  },
  remove: {
    ...text.caption,
    color: palette.coral,
    ...fontFor(700),
  },
  hint: {
    ...text.caption,
    color: tones.danger.fg,
  },
});
