import { useTranslation } from "react-i18next";
import { Badge } from "@mantine/core";
import { formatDateOnly, toDateOnly } from "@/lib/dates";
import { isReplacementOverdue, type Asset } from "@/services/assets.service";

/** KKD periodic replacement date of an assigned item; red once it has passed. Renders nothing without a date. */
export default function ReplacementDueBadge({ asset }: { asset: Asset }) {
  const { t, i18n } = useTranslation(["assets"]);
  if (!asset.replacementDueOn) return null;
  const overdue = isReplacementOverdue(asset, toDateOnly(new Date()));
  return (
    <Badge variant="light" color={overdue ? "red" : "orange"}>
      {t(overdue ? "assets:replacement.overdue" : "assets:replacement.due", {
        date: formatDateOnly(asset.replacementDueOn, i18n.language),
      })}
    </Badge>
  );
}
