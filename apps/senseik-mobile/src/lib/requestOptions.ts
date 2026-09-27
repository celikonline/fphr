import type React from "react";
import type { Ionicons } from "@expo/vector-icons";
import type { AccentKey } from "@/theme/theme";
import type { RequestKind } from "@/types/navigation";

type IoniconName = React.ComponentProps<typeof Ionicons>["name"];

export type RequestOption = {
  kind: RequestKind;
  label: string;
  description: string;
  icon: IoniconName;
  accent: AccentKey;
};

/** The request types the FAB can start, in sheet order (mockup `.sheet .req`). Also the icon/accent
 * source for request cards (Taleplerim, İzinlerim). */
export const REQUEST_OPTIONS: readonly RequestOption[] = [
  {
    kind: "leave",
    label: "İzin",
    description: "Yıllık, mazeret, raporlu…",
    icon: "sunny-outline",
    accent: "green",
  },
  {
    kind: "overtime",
    label: "Fazla mesai",
    description: "Planlanan veya yapılan mesai",
    icon: "time-outline",
    accent: "blue",
  },
  {
    kind: "expense",
    label: "Masraf",
    description: "Fiş veya fatura ile",
    icon: "receipt-outline",
    accent: "amber",
  },
  {
    kind: "advance",
    label: "Avans",
    description: "Maaştan mahsup edilir",
    icon: "wallet-outline",
    accent: "coral",
  },
];
