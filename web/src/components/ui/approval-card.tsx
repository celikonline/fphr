import type { ReactNode } from "react";
import { Card, Group, Stack, Text } from "@mantine/core";

/** Matches a bare UUID (v1-v5, any case) with no other surrounding text. */
const RAW_ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Callers sometimes pass a record's raw id as `subtitle` before a human-readable label is wired up.
 * Showing `3f2b9c4a-...` to an HR user is not acceptable content, so a bare-GUID subtitle is treated
 * as "no label available" and simply not rendered - no placeholder text is invented.
 */
function isRawIdentifier(value: ReactNode): boolean {
  return typeof value === "string" && RAW_ID_PATTERN.test(value.trim());
}

export interface ApprovalCardField {
  label: ReactNode;
  value: ReactNode;
}

export interface ApprovalCardProps {
  /** Request type / record name, e.g. `t("workflow:requestType.leave")`. */
  title: ReactNode;
  /** Secondary line under the title, e.g. the subject/record id. */
  subtitle?: ReactNode;
  /** Small badge(s) shown next to the title (status, fallback-role marker, ...). */
  badges?: ReactNode;
  /** Requester / date / step-number style key-value pairs, laid out in a row. */
  fields?: ApprovalCardField[];
  /** Decide/override/approve/reject buttons - left to the caller since each flow's action set differs. */
  actions?: ReactNode;
  className?: string;
}

/**
 * Faz 1 ortak bileşen: a single pending-approval row as a card (request type, requester, date/step,
 * decide actions) instead of a raw table row - reusable across Workflow inbox, Leave, Expense, etc.
 * approval lists. First consumer: `pages/inbox.tsx`.
 */
export function ApprovalCard({ title, subtitle, badges, fields, actions, className }: ApprovalCardProps) {
  return (
    <Card withBorder padding="md" className={className}>
      <Group justify="space-between" align="flex-start" wrap="nowrap" gap="md">
        <Stack gap={4} className="min-w-0 flex-1">
          <Group gap="xs" wrap="wrap" align="center">
            <Text fw={600}>{title}</Text>
            {badges}
          </Group>
          {subtitle && !isRawIdentifier(subtitle) && (
            <Text size="xs" c="dimmed" truncate>
              {subtitle}
            </Text>
          )}
          {fields && fields.length > 0 && (
            <Group gap="lg" mt={4} wrap="wrap">
              {fields.map((field, index) => (
                <Stack gap={0} key={index}>
                  <Text size="xs" c="dimmed">
                    {field.label}
                  </Text>
                  <Text size="sm">{field.value}</Text>
                </Stack>
              ))}
            </Group>
          )}
        </Stack>
        {actions && (
          <Group gap="xs" wrap="nowrap" className="shrink-0">
            {actions}
          </Group>
        )}
      </Group>
    </Card>
  );
}
