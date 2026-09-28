import React, { useState } from "react";
import { StyleSheet, Text, TouchableOpacity } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { GlassCard, SectionHeader } from "@/components/ui";
import { changeAppLanguage, currentLanguage, type AppLanguage } from "@/lib/i18n";
import { preferencesService } from "@/services/PreferencesService";
import { useAuthStore } from "@/store/authStore";
import { palette, text } from "@/theme/theme";
import { LanguageSheet } from "./LanguageSheet";

type IoniconName = React.ComponentProps<typeof Ionicons>["name"];

type SettingsRowProps = {
  icon: IoniconName;
  label: string;
  value: string;
  onPress: () => void;
};

/** One "Ayarlar" row: icon, label, current value, chevron. */
function SettingsRow({ icon, label, value, onPress }: SettingsRowProps) {
  return (
    <TouchableOpacity
      style={styles.row}
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={`${label}: ${value}`}
    >
      <Ionicons name={icon} size={20} color={palette.ice} />
      <Text style={styles.label}>{label}</Text>
      <Text style={styles.value}>{value}</Text>
      <Ionicons name="chevron-forward" size={18} color={palette.textFaint} />
    </TouchableOpacity>
  );
}

/**
 * FG-001: Profil'in "Ayarlar" kartı - the settings rows (today only "Dil"; later settings append
 * their own row here). Picking a language switches every mounted screen at once
 * (`i18n.changeLanguage`) and persists the choice, no restart needed. Then, only while signed in,
 * the choice is sent to the server (`PUT identity/me/language`, plan §2 Karar 18) so push/e-mail
 * follow it too - best effort: a failure is swallowed and the local choice stays.
 */
export function SettingsCard() {
  // `useTranslation` re-renders this card on a language switch, so `currentLanguage()` stays current.
  const { t } = useTranslation("profile");
  const [languageSheetVisible, setLanguageSheetVisible] = useState(false);
  const language = currentLanguage();
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated);

  async function selectLanguage(next: AppLanguage) {
    await changeAppLanguage(next);
    if (isAuthenticated) {
      await preferencesService.syncMyLanguage(next);
    }
  }

  return (
    <>
      <SectionHeader title={t("settings.title")} />
      <GlassCard style={styles.card} padding={0}>
        <SettingsRow
          icon="language-outline"
          label={t("settings.language")}
          value={t(`common:languageNames.${language}`)}
          onPress={() => setLanguageSheetVisible(true)}
        />
      </GlassCard>
      <LanguageSheet
        visible={languageSheetVisible}
        selected={language}
        onSelect={(next) => void selectLanguage(next)}
        onClose={() => setLanguageSheetVisible(false)}
      />
    </>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 20,
    marginBottom: 16,
  },
  row: {
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
    minHeight: 52,
    paddingHorizontal: 16,
  },
  label: {
    ...text.body,
    flex: 1,
  },
  value: {
    ...text.bodyMuted,
  },
});
