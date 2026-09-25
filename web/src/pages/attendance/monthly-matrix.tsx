import { FileSpreadsheet } from "lucide-react";
import SkeletonPage from "@/components/skeleton-page";

/** Aylık puantaj matrisi: iskelet (zaman-devam W0-c); asıl ekran ilgili şeritte doldurulur. */
export default function MonthlyMatrixPage() {
  return (
    <SkeletonPage
      ns="attendance-daily"
      titleKey="monthlyMatrix.title"
      subtitleKey="monthlyMatrix.subtitle"
      permissions={["attendance.daily.view"]}
      icon={<FileSpreadsheet size={24} />}
    />
  );
}
