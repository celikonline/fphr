import { useTranslation } from "react-i18next";
import { Badge } from "@mantine/core";
import { daysUntil, formatDateOnly } from "@/lib/dates";

interface ExpiryBadgeProps {
  /** Backend `DateOnly` ("yyyy-MM-dd") or `null`/`undefined` (field omitted when unset, or not sent by this endpoint
   * yet - S-6 sözleşmesi). Renders nothing in either case, per TALIMAT Faz 4 tur 1 FE md.1. */
  expiryDate: string | null | undefined;
}

/**
 * Small validity badge for a single document (ehliyet, oturma izni, İSG sertifikası, sağlık raporu, ...): red once
 * expired, orange within 30 days, otherwise nothing. Shared by `documents-section.tsx` (used for both the HR
 * employee card and the employee's own "Belgelerim" self-service view - same component, so both places pick this up
 * automatically) so the same rule and copy is never duplicated.
 */
export function ExpiryBadge({ expiryDate }: ExpiryBadgeProps) {
  const { t, i18n } = useTranslation("documents");
  if (!expiryDate) return null;

  const daysLeft = daysUntil(expiryDate);
  const date = formatDateOnly(expiryDate, i18n.language);

  if (daysLeft < 0) {
    return (
      <Badge variant="filled" color="red">
        {t("documents:expiry.expiredBadge", { date })}
      </Badge>
    );
  }
  if (daysLeft <= 30) {
    return (
      <Badge variant="filled" color="orange">
        {t("documents:expiry.expiringBadge", { date })}
      </Badge>
    );
  }
  return null;
}
