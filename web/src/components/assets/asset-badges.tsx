import { useTranslation } from "react-i18next";
import { Badge } from "@mantine/core";
import { formatDateOnly, toDateOnly } from "@/lib/dates";
import {
  isWarrantyExpired,
  isWarrantyExpiring,
  type Asset,
  type AssetCondition,
} from "@/services/assets.service";

/** Warranty follow-up: red once ended, orange within 60 days of ending. Text carries the meaning, not just the colour. */
export function WarrantyBadge({ asset }: { asset: Asset }) {
  const { t, i18n } = useTranslation(["assets"]);
  if (!asset.warrantyEndsOn) return null;
  const today = toDateOnly(new Date());
  const expired = isWarrantyExpired(asset, today);
  if (!expired && !isWarrantyExpiring(asset, today)) return null;
  return (
    <Badge variant="light" color={expired ? "red" : "orange"}>
      {t(expired ? "assets:warranty.expired" : "assets:warranty.expiring", {
        date: formatDateOnly(asset.warrantyEndsOn, i18n.language),
      })}
    </Badge>
  );
}

/** An assigned asset whose assignee has not confirmed receipt yet. */
export function UnacknowledgedBadge({ asset }: { asset: Asset }) {
  const { t } = useTranslation(["assets"]);
  if (asset.status !== "assigned" || asset.acknowledgedAt) return null;
  return (
    <Badge variant="light" color="orange">
      {t("assets:acknowledgement.pending")}
    </Badge>
  );
}

export const CONDITION_COLOR: Record<AssetCondition, string> = {
  good: "green",
  fair: "orange",
  damaged: "red",
};

/** Shown only for fair/damaged equipment; "good" is the unremarkable default. */
export function ConditionBadge({ condition }: { condition?: AssetCondition | null }) {
  const { t } = useTranslation(["assets"]);
  if (!condition || condition === "good") return null;
  return (
    <Badge variant="light" color={CONDITION_COLOR[condition]}>
      {t(`assets:enum.condition.${condition}`)}
    </Badge>
  );
}
