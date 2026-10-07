import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Card, SimpleGrid, Skeleton, Text } from "@mantine/core";
import { getAssetSummary, type AssetStatus, type AssetSummary } from "@/services/assets.service";

interface SummaryItem {
  key: string;
  label: string;
  count: number;
  /** Filterable by status: clicking applies it (`null` = clear the filter). Items without it are display-only. */
  filter?: AssetStatus | null;
  /** Counts above zero draw attention (border colour only; the label already says what the number means). */
  warn?: "orange" | "red";
}

interface AssetSummaryCardsProps {
  status: AssetStatus | null;
  onStatusChange: (status: AssetStatus | null) => void;
}

function buildItems(summary: AssetSummary, t: (key: string) => string): SummaryItem[] {
  return [
    { key: "total", label: t("assets:summary.total"), count: summary.total, filter: null },
    {
      key: "unassigned",
      label: t("assets:summary.unassigned"),
      count: summary.unassigned,
      filter: "unassigned",
    },
    {
      key: "assigned",
      label: t("assets:summary.assigned"),
      count: summary.assigned,
      filter: "assigned",
    },
    {
      key: "pendingAssignment",
      label: t("assets:summary.pendingAssignment"),
      count: summary.pendingAssignment,
      filter: "pendingAssignment",
    },
    {
      key: "unacknowledged",
      label: t("assets:summary.unacknowledged"),
      count: summary.unacknowledged,
      warn: "orange",
    },
    {
      key: "warrantyExpired",
      label: t("assets:summary.warrantyExpired"),
      count: summary.warrantyExpired,
      warn: "red",
    },
    {
      key: "warrantyExpiring",
      label: t("assets:summary.warrantyExpiring"),
      count: summary.warrantyExpiring,
      warn: "orange",
    },
    {
      key: "replacementOverdue",
      label: t("assets:summary.replacementOverdue"),
      count: summary.replacementOverdue,
      warn: "red",
    },
    {
      key: "lost",
      label: t("assets:summary.lost"),
      count: summary.lost,
      filter: "lost",
      warn: "red",
    },
  ];
}

/** Inventory dashboard strip (`GET /summary`); status cards double as filters, the rest are read-only counters. */
export default function AssetSummaryCards({ status, onStatusChange }: AssetSummaryCardsProps) {
  const { t } = useTranslation(["assets"]);
  const query = useQuery({ queryKey: ["assets", "summary"], queryFn: getAssetSummary });

  if (query.isLoading) return <Skeleton height={72} />;
  if (query.isError || !query.data) {
    return (
      <Text size="sm" c="dimmed">
        {t("assets:summary.loadFailed")}
      </Text>
    );
  }

  return (
    <SimpleGrid
      cols={{ base: 2, sm: 3, lg: 5 }}
      spacing="sm"
      aria-label={t("assets:summary.title")}
      role="group"
    >
      {buildItems(query.data, t).map((item) => {
        const clickable = item.filter !== undefined;
        const active = clickable && item.filter === status;
        const attention = item.warn && item.count > 0;
        const borderColor = active
          ? "var(--mantine-primary-color-filled)"
          : attention
            ? `var(--mantine-color-${item.warn}-6)`
            : undefined;
        const content = (
          <>
            <Text size="xs" c="dimmed">
              {item.label}
            </Text>
            <Text fw={700} size="xl">
              {item.count}
            </Text>
          </>
        );
        return clickable ? (
          <Card
            key={item.key}
            component="button"
            type="button"
            withBorder
            padding="sm"
            ta="left"
            aria-pressed={active}
            style={{ cursor: "pointer", borderColor }}
            onClick={() => onStatusChange(active ? null : (item.filter ?? null))}
          >
            {content}
          </Card>
        ) : (
          <Card key={item.key} withBorder padding="sm" style={{ borderColor }}>
            {content}
          </Card>
        );
      })}
    </SimpleGrid>
  );
}
