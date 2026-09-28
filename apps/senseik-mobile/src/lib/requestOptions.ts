import type React from "react";
import type { Ionicons } from "@expo/vector-icons";
import type { AccentKey } from "@/theme/theme";
import type { RequestKind } from "@/types/navigation";

type IoniconName = React.ComponentProps<typeof Ionicons>["name"];

export type RequestOption = {
  kind: RequestKind;
  /** i18n key (`requests` namespace) - render with `t(option.labelKey)`. */
  labelKey: string;
  /** i18n key (`requests` namespace) - render with `t(option.descriptionKey)`. */
  descriptionKey: string;
  icon: IoniconName;
  accent: AccentKey;
};

/** The request types the FAB can start, in sheet order (mockup `.sheet .req`). Also the icon/accent
 * source for request cards (Taleplerim, İzinlerim). */
export const REQUEST_OPTIONS: readonly RequestOption[] = [
  {
    kind: "leave",
    labelKey: "requests:options.leave.label",
    descriptionKey: "requests:options.leave.description",
    icon: "sunny-outline",
    accent: "green",
  },
  {
    kind: "overtime",
    labelKey: "requests:options.overtime.label",
    descriptionKey: "requests:options.overtime.description",
    icon: "time-outline",
    accent: "blue",
  },
  {
    kind: "expense",
    labelKey: "requests:options.expense.label",
    descriptionKey: "requests:options.expense.description",
    icon: "receipt-outline",
    accent: "amber",
  },
  {
    kind: "advance",
    labelKey: "requests:options.advance.label",
    descriptionKey: "requests:options.advance.description",
    icon: "wallet-outline",
    accent: "coral",
  },
  {
    kind: "punchCorrection",
    labelKey: "requests:options.punchCorrection.label",
    descriptionKey: "requests:options.punchCorrection.description",
    icon: "create-outline",
    accent: "teal",
  },
];
