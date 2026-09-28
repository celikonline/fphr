import React from "react";
import { useTranslation } from "react-i18next";
import { OptionSheet } from "@/components/form/OptionSheet";
import { SUPPORTED_LANGUAGES, type AppLanguage } from "@/lib/i18n";

type Props = {
  visible: boolean;
  selected: AppLanguage;
  onSelect: (language: AppLanguage) => void;
  onClose: () => void;
};

/**
 * FG-001: Profil > Ayarlar > Dil picker. Every language is listed in its own language ("Türkçe",
 * "English" - `common:languageNames`, identical in every locale file), no flags; the selected row
 * gets the brand check icon (`OptionSheet`).
 */
export function LanguageSheet({ visible, selected, onSelect, onClose }: Props) {
  const { t } = useTranslation("profile");
  const options = SUPPORTED_LANGUAGES.map((language) => ({
    value: language,
    label: t(`common:languageNames.${language}`),
  }));

  return (
    <OptionSheet
      visible={visible}
      title={t("settings.language")}
      options={options}
      selected={selected}
      onSelect={onSelect}
      onClose={onClose}
    />
  );
}
