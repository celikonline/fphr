import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Badge, Card, Group, Skeleton, Stack, Text, Title } from "@mantine/core";
import { CalendarRange } from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import { getMyReviewCycles, type ReviewCycle, type ReviewCycleStatus } from "@/services/performance.service";

const STATUS_BADGE: Record<ReviewCycleStatus, { color?: string; variant: "light" | "filled" | "outline" }> = {
  draft: { variant: "outline", color: "gray" },
  active: { variant: "filled", color: "blue" },
  closed: { variant: "light", color: "gray" },
};

function ReviewCycleStatusBadge({ status }: { status: ReviewCycleStatus }) {
  const { t } = useTranslation(["performance"]);
  return <Badge {...STATUS_BADGE[status]}>{t(`performance:reviewCycles.status.${status}`)}</Badge>;
}

export default function ReviewCyclesPage() {
  const canView = usePermission("performance.my.reviews");
  if (!canView) return <NoAccess />;
  return <ReviewCycles />;
}

function ReviewCycles() {
  const { t, i18n } = useTranslation(["performance", "common"]);
  const cyclesQuery = useQuery({ queryKey: ["performance", "review-cycles", "me"], queryFn: getMyReviewCycles });

  const date = (value: string) => formatDateOnly(value, i18n.language);

  return (
    <div className="mx-auto max-w-4xl space-y-6 p-4 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <CalendarRange className="h-6 w-6" /> {t("performance:reviewCycles.title")}
        </Title>
        <Text c="dimmed">{t("performance:reviewCycles.subtitle")}</Text>
      </div>

      {cyclesQuery.isLoading ? (
        <Stack gap="md">
          <Skeleton h={90} />
          <Skeleton h={90} />
        </Stack>
      ) : cyclesQuery.isError ? (
        <Text size="sm" c="red">
          {getApiErrorMessage(cyclesQuery.error, t("performance:genericError"))}
        </Text>
      ) : !cyclesQuery.data || cyclesQuery.data.length === 0 ? (
        <Card withBorder padding="lg">
          <Text size="sm" c="dimmed" ta="center">
            {t("performance:reviewCycles.empty")}
          </Text>
        </Card>
      ) : (
        <Stack gap="md">
          {cyclesQuery.data.map((cycle: ReviewCycle) => (
            <Card key={cycle.id} withBorder padding="lg">
              <Group justify="space-between" align="flex-start" gap="sm" wrap="wrap">
                <div>
                  <Text fw={600}>{cycle.name}</Text>
                  <Text size="sm" c="dimmed">
                    {t("performance:reviewCycles.dateRange", { start: date(cycle.startDate), end: date(cycle.endDate) })}
                  </Text>
                </div>
                <ReviewCycleStatusBadge status={cycle.status} />
              </Group>
            </Card>
          ))}
        </Stack>
      )}
    </div>
  );
}
