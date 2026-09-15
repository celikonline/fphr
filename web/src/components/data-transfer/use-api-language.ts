import { useTranslation } from "react-i18next";

/** Two-letter language code sent as `?lang=` to localized template/result endpoints. */
export function useApiLanguage(): string {
  const { i18n } = useTranslation();
  return (i18n.resolvedLanguage ?? i18n.language ?? "tr").slice(0, 2);
}
