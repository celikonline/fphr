import { Stamp } from "lucide-react";
import SkeletonPage from "@/components/skeleton-page";

/** Kayıt talepleri: iskelet (zaman-devam W0-c); asıl ekran ilgili şeritte doldurulur. */
export default function PunchRequestsPage() {
  return (
    <SkeletonPage
      ns="attendance-daily"
      titleKey="punchRequests.title"
      subtitleKey="punchRequests.subtitle"
      permissions={["attendance.punchrequest.manage", "attendance.my.punchrequest"]}
      icon={<Stamp size={24} />}
    />
  );
}
