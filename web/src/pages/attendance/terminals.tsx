import { QrCode } from "lucide-react";
import SkeletonPage from "@/components/skeleton-page";

/** PDKS terminalleri: iskelet (zaman-devam W0-c); asıl ekran ilgili şeritte doldurulur. */
export default function TerminalsPage() {
  return (
    <SkeletonPage
      ns="terminals"
      titleKey="title"
      subtitleKey="subtitle"
      permissions={["attendance.terminal.view"]}
      icon={<QrCode size={24} />}
    />
  );
}
