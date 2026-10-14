import type { ReactNode } from "react";
import { Stack, Text } from "@mantine/core";

interface AttendanceEmptyStateProps {
  title: string;
  description?: string;
  action?: ReactNode;
}

/** A consistent empty-list message with a clear next step for attendance screens. */
export default function AttendanceEmptyState({ title, description, action }: AttendanceEmptyStateProps) {
  return (
    <Stack align="center" gap="xs" py="xl" px="md">
      <Text size="sm" c="dimmed" ta="center">{title}</Text>
      {description && <Text size="sm" c="dimmed" ta="center">{description}</Text>}
      {action}
    </Stack>
  );
}
