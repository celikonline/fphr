import { ClipboardList } from "lucide-react";
import SkeletonPage from "@/components/skeleton-page";

/** Günlük puantaj listesi (/app/attendance/daily/:status): iskelet (zaman-devam W0-c); asıl ekran ilgili şeritte doldurulur. */
export default function DailyListPage() {
  return (
    <SkeletonPage
      ns="attendance-daily"
      titleKey="dailyList.title"
      subtitleKey="dailyList.subtitle"
      permissions={["attendance.daily.view"]}
      icon={<ClipboardList size={24} />}
    />
  );
}
