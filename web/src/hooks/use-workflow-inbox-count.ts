import { useQuery } from "@tanstack/react-query";
import { usePermission } from "@/components/permission-guard";
import { listInbox } from "@/services/workflow.service";

/** Same key `pages/inbox.tsx` uses, so the sidebar badge and the inbox page share one cached fetch. */
const INBOX_QUERY_KEY = ["workflow", "inbox"] as const;

/**
 * Pending "Onay kutusu" task count for the sidebar badge (senseik-047 D-03). 0 for callers without
 * `workflow.inbox.view` - the query never runs for them, matching NAV_ITEMS' own permission gate.
 */
export function useWorkflowInboxCount(): number {
  const canView = usePermission("workflow.inbox.view");
  const query = useQuery({
    queryKey: INBOX_QUERY_KEY,
    queryFn: listInbox,
    enabled: canView,
    staleTime: 30_000,
  });
  return canView ? (query.data?.length ?? 0) : 0;
}
