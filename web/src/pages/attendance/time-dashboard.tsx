import { PieChart } from "lucide-react";
import SkeletonPage from "@/components/skeleton-page";

/** Zaman panosu: iskelet (zaman-devam W0-c); asıl ekran ilgili şeritte doldurulur. */
export default function TimeDashboardPage() {
  return (
    <SkeletonPage
      ns="attendance-daily"
      titleKey="timeDashboard.title"
      subtitleKey="timeDashboard.subtitle"
      permissions={["attendance.dashboard.view"]}
      icon={<PieChart size={24} />}
    />
  );
}
