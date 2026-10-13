import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Badge } from "@mantine/core";
import { listPayItems, type PayItemDirection } from "@/services/payroll-pay-items.service";

/** Pay item definitions incl. inactive ones, used only to look up a definition's direction by id. */
const DIRECTION_LOOKUP_KEY = ["payroll", "pay-items", "direction-lookup"] as const;

/** id -> direction of every definition; assignments of older servers carry no direction. Missing = earning. */
export function usePayItemDirections(): (definitionId: string, own?: PayItemDirection | null) => PayItemDirection {
  const { data } = useQuery({ queryKey: DIRECTION_LOOKUP_KEY, queryFn: () => listPayItems(true), retry: false });
  const byId = new Map((data ?? []).map((item) => [item.id, item.direction] as const));
  return (definitionId, own) => own ?? byId.get(definitionId) ?? "earning";
}

/** "Kazanç" / "Kesinti" badge (deduction in red). */
export function DirectionBadge({ direction }: { direction: PayItemDirection }) {
  const { t } = useTranslation(["payroll"]);
  return (
    <Badge color={direction === "deduction" ? "red" : "teal"} variant="light">
      {t(`payroll:payItems.direction.${direction}`)}
    </Badge>
  );
}
