import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router";
import { Anchor } from "@mantine/core";
import { getEmployee } from "@/services/employees.service";

const EMPLOYEE_NAME_STALE_MS = 5 * 60_000;

interface EmployeeNameLinkProps {
  id: string;
  /** Employee card tab to open, e.g. "assets"; defaults to the card's first tab. */
  tab?: string;
}

/**
 * Resolves an employee id (as carried by other modules' DTOs) to a link with the display name, sharing the employee
 * card's `["employees", "detail", id]` cache entry. Shows a dash when the caller cannot read the record.
 */
export default function EmployeeNameLink({ id, tab }: EmployeeNameLinkProps) {
  const query = useQuery({
    queryKey: ["employees", "detail", id],
    queryFn: () => getEmployee(id),
    staleTime: EMPLOYEE_NAME_STALE_MS,
    retry: false,
  });
  if (!query.data) return <>{query.isLoading ? "…" : "—"}</>;
  return (
    <Anchor component={Link} to={tab ? `/app/employees/${id}/${tab}` : `/app/employees/${id}`} size="sm">
      {query.data.displayName}
    </Anchor>
  );
}
