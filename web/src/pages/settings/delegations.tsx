import { Workflow } from "lucide-react";
import SkeletonPage from "@/components/skeleton-page";

/** Onay vekaletleri: iskelet (zaman-devam W0-c); asıl ekran ilgili şeritte doldurulur. */
export default function DelegationsPage() {
  return (
    <SkeletonPage
      ns="delegations"
      titleKey="title"
      subtitleKey="subtitle"
      permissions={["workflow.delegation.manage", "workflow.my.delegation"]}
      icon={<Workflow size={24} />}
    />
  );
}
