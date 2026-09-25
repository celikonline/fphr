import { Clock } from "lucide-react";
import SkeletonPage from "@/components/skeleton-page";

/** Vardiya tanımları: iskelet (zaman-devam W0-c); asıl ekran ilgili şeritte doldurulur. */
export default function ShiftDefinitionsPage() {
  return (
    <SkeletonPage
      ns="shifts"
      titleKey="title"
      subtitleKey="subtitle"
      permissions={["attendance.shiftdefinition.view"]}
      icon={<Clock size={24} />}
    />
  );
}
