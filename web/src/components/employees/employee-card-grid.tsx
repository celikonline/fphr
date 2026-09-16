import { useTranslation } from "react-i18next";
import { Avatar, Badge, Checkbox, SimpleGrid, Skeleton, Stack, Text, Tooltip, UnstyledButton } from "@mantine/core";
import type { EmployeeListItem } from "@/services/employees.service";
import { avatarColor, EmployeeStatusBadge, initials } from "./employee-badges";

const SKELETON_CARDS = 10;
const GRID_COLS = { base: 1, xs: 2, sm: 3, md: 4, lg: 5 };

interface EmployeeCardGridProps {
  items: EmployeeListItem[];
  isLoading?: boolean;
  /** Dims the cards while a background refetch (new page/filter) is running. */
  isFetching?: boolean;
  emptyMessage: React.ReactNode;
  unitNames: ReadonlyMap<string, string>;
  onOpen: (employee: EmployeeListItem) => void;
  /** Bulk selection; the checkbox is hidden when omitted. */
  isSelected?: (id: string) => boolean;
  onToggle?: (id: string, checked: boolean) => void;
}

/** Card ("grid") view of the employee list: photo placeholder, name, title, work email and phone. */
export default function EmployeeCardGrid({
  items,
  isLoading,
  isFetching,
  emptyMessage,
  unitNames,
  onOpen,
  isSelected,
  onToggle,
}: EmployeeCardGridProps) {
  if (isLoading) {
    return (
      <SimpleGrid cols={GRID_COLS} spacing="md" p="md">
        {Array.from({ length: SKELETON_CARDS }, (_, i) => (
          <Skeleton key={i} h={220} radius="md" />
        ))}
      </SimpleGrid>
    );
  }

  if (items.length === 0) {
    return (
      <Text p="lg" ta="center" size="sm" c="dimmed">
        {emptyMessage}
      </Text>
    );
  }

  return (
    <SimpleGrid cols={GRID_COLS} spacing="md" p="md" style={{ opacity: isFetching ? 0.6 : undefined, transition: "opacity 150ms" }}>
      {items.map((employee) => (
        <EmployeeGridCard
          key={employee.id}
          employee={employee}
          unitName={employee.orgUnitId ? unitNames.get(employee.orgUnitId) : undefined}
          onOpen={onOpen}
          selected={isSelected?.(employee.id)}
          onToggle={onToggle}
        />
      ))}
    </SimpleGrid>
  );
}

function EmployeeGridCard({
  employee,
  unitName,
  onOpen,
  selected,
  onToggle,
}: {
  employee: EmployeeListItem;
  unitName: string | undefined;
  onOpen: (employee: EmployeeListItem) => void;
  selected: boolean | undefined;
  onToggle: ((id: string, checked: boolean) => void) | undefined;
}) {
  const { t } = useTranslation(["employees"]);
  const subtitle = employee.titleName ?? unitName;

  return (
    <div className="relative">
      <UnstyledButton
        onClick={() => onOpen(employee)}
        className="h-full w-full rounded-md border p-4 text-center transition-shadow hover:shadow-md"
        style={{
          borderColor: selected ? "var(--mantine-primary-color-filled)" : "var(--mantine-color-default-border)",
          background: selected ? "var(--mantine-primary-color-light)" : "var(--mantine-color-body)",
        }}
      >
        <Stack gap={4} align="center">
          <Avatar size={80} radius="md" color={avatarColor(employee.id)} mb="xs">
            {initials(employee.firstName, employee.lastName)}
          </Avatar>
          <Text size="sm" fw={600} lineClamp={1} title={employee.displayName}>
            {employee.displayName}
          </Text>
          <Text size="sm" lineClamp={1} title={subtitle ?? undefined}>
            {subtitle ?? "—"}
          </Text>
          <Text size="xs" c="dimmed" truncate="end" maw="100%" title={employee.workEmail ?? undefined}>
            {employee.workEmail ?? "—"}
          </Text>
          <Text size="xs" c="dimmed">
            {employee.workPhone ?? "—"}
          </Text>
          {(employee.status !== "Active" || employee.hasMissingInfo) && (
            <div className="mt-1 flex flex-wrap justify-center gap-1">
              {employee.status !== "Active" && <EmployeeStatusBadge status={employee.status} />}
              {employee.hasMissingInfo && (
                <Tooltip label={t("employees:filters.missingInfoHint")}>
                  <Badge size="xs" variant="light" color="orange">
                    {t("employees:filters.missingInfo")}
                  </Badge>
                </Tooltip>
              )}
            </div>
          )}
        </Stack>
      </UnstyledButton>
      {onToggle && (
        <Checkbox
          className="absolute left-3 top-3"
          aria-label={employee.displayName}
          checked={selected ?? false}
          onChange={(e) => onToggle(employee.id, e.currentTarget.checked)}
        />
      )}
    </div>
  );
}
