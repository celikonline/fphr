import { CalendarRange } from "lucide-react";
import SkeletonPage from "@/components/skeleton-page";

/** Vardiya rotasyonları: iskelet (zaman-devam W0-c); asıl ekran ilgili şeritte doldurulur. */
export default function RotationsPage() {
  return (
    <SkeletonPage
      ns="rotations"
      titleKey="title"
      subtitleKey="subtitle"
      permissions={["attendance.rotation.view"]}
      icon={<CalendarRange size={24} />}
    />
  );
}
