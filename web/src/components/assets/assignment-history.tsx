import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Badge, Card, Group, Modal, Skeleton, Stack, Text, Title } from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { getApiErrorMessage } from "@/lib/api-error";
import EmployeeNameLink from "@/components/employees/employee-name-link";
import { CONDITION_COLOR } from "@/components/assets/asset-badges";
import { formatMoment } from "@/components/assets/asset-format";
import { formatDateOnly } from "@/lib/dates";
import {
  getAssetHistory,
  type Asset,
  type AssetAssignment,
  type EmployeeAssetHistoryItem,
} from "@/services/assets.service";

interface AssignmentRowProps {
  /** Asset name (employee history) or the assignee (asset history). */
  title: ReactNode;
  /** Optional dim line under the title, e.g. category and serial number. */
  details?: string;
  assignment: AssetAssignment;
}

/** One ledger entry: who/what, the held period, how it ended and whether receipt was confirmed. */
function AssignmentRow({ title, details, assignment }: AssignmentRowProps) {
  const { t, i18n } = useTranslation(["assets"]);
  const isOpen = !assignment.returnedAt;
  const period = `${formatDateOnly(assignment.issuedAt, i18n.language)} – ${
    assignment.returnedAt
      ? formatDateOnly(assignment.returnedAt, i18n.language)
      : t("assets:history.ongoing")
  }`;
  const acknowledgement = assignment.acknowledgedAt
    ? t("assets:acknowledgement.done", {
        date: formatMoment(assignment.acknowledgedAt, i18n.language),
      })
    : t(isOpen ? "assets:acknowledgement.pending" : "assets:acknowledgement.never");

  return (
    <Card withBorder padding="sm">
      <Group justify="space-between" align="flex-start" wrap="wrap" gap="xs">
        <Stack gap={2} className="min-w-0">
          <Text fw={600} size="sm" component="div">
            {title}
          </Text>
          {details && (
            <Text size="xs" c="dimmed">
              {details}
            </Text>
          )}
          <Text size="xs" c="dimmed">
            {period}
          </Text>
          <Text size="xs" c="dimmed">
            {acknowledgement}
          </Text>
          {assignment.note && (
            <Text size="xs">{t("assets:history.note", { note: assignment.note })}</Text>
          )}
        </Stack>
        <Group gap="xs" wrap="wrap">
          {isOpen && (
            <Badge variant="light" color="green">
              {t("assets:history.open")}
            </Badge>
          )}
          {assignment.endReason === "lost" && (
            <Badge variant="light" color="red">
              {t("assets:history.endReason.lost")}
            </Badge>
          )}
          {assignment.endReason !== "lost" && assignment.returnedAt && (
            <Badge variant="light" color="gray">
              {t("assets:history.endReason.returned")}
            </Badge>
          )}
          {assignment.returnCondition && (
            <Badge variant="light" color={CONDITION_COLOR[assignment.returnCondition]}>
              {t("assets:history.returnCondition", {
                condition: t(`assets:enum.condition.${assignment.returnCondition}`),
              })}
            </Badge>
          )}
        </Group>
      </Group>
    </Card>
  );
}

/** Asset ledger (assets.assets.view), newest first, open assignment included. */
export function AssetHistoryDialog({ asset, onClose }: { asset: Asset; onClose: () => void }) {
  const { t } = useTranslation(["assets"]);
  const isMobile = useMediaQuery("(max-width: 390px)");
  const query = useQuery({
    queryKey: ["assets", "history", asset.id],
    queryFn: () => getAssetHistory(asset.id),
  });
  const rows = query.data ?? [];

  return (
    <Modal
      opened
      onClose={onClose}
      title={t("assets:history.title", { name: asset.name })}
      size="32rem"
      fullScreen={isMobile}
    >
      {query.isLoading && <Skeleton height={120} />}
      {query.isError && (
        <Text c="red">{getApiErrorMessage(query.error, t("assets:history.loadFailed"))}</Text>
      )}
      {query.isSuccess && rows.length === 0 && <Text c="dimmed">{t("assets:history.empty")}</Text>}
      <Stack gap="sm">
        {rows.map((assignment) => (
          <AssignmentRow
            key={assignment.id}
            title={<EmployeeNameLink id={assignment.employeeId} tab="assets" />}
            assignment={assignment}
          />
        ))}
      </Stack>
    </Modal>
  );
}

interface PastAssignmentsSectionProps {
  /** TanStack key; must start with "assets" so every asset mutation refreshes it. */
  queryKey: readonly unknown[];
  load: () => Promise<EmployeeAssetHistoryItem[]>;
  heading: string;
  /** Heading level; defaults to 4 (page level), the employee card passes 5 under its own title. */
  headingOrder?: 4 | 5;
  emptyText: string;
  /** Resolves a category id to its name for the dim details line (omit when the caller cannot read categories). */
  categoryName?: (categoryId: string) => string | undefined;
}

/** Closed assignments (returned or lost) of an employee, newest first - "Geçmiş zimmetlerim" and the card's history. */
export function PastAssignmentsSection({
  queryKey,
  load,
  heading,
  headingOrder = 4,
  emptyText,
  categoryName,
}: PastAssignmentsSectionProps) {
  const { t } = useTranslation(["assets"]);
  const query = useQuery({ queryKey, queryFn: load });
  const closed = (query.data ?? []).filter((item) => !!item.returnedAt);

  return (
    <Stack gap="sm">
      <Title order={headingOrder}>{heading}</Title>
      {query.isLoading && <Skeleton height={80} />}
      {query.isError && (
        <Text c="red">{getApiErrorMessage(query.error, t("assets:history.loadFailed"))}</Text>
      )}
      {query.isSuccess && closed.length === 0 && (
        <Text size="sm" c="dimmed">
          {emptyText}
        </Text>
      )}
      {closed.map((item) => {
        const details = [
          categoryName?.(item.categoryId),
          item.serialNumber && `${t("assets:section.serialNumber")}: ${item.serialNumber}`,
          item.assetTag && `${t("assets:section.assetTag")}: ${item.assetTag}`,
        ]
          .filter(Boolean)
          .join(" · ");
        return (
          <AssignmentRow
            key={item.id}
            title={item.assetName}
            details={details || undefined}
            assignment={item}
          />
        );
      })}
    </Stack>
  );
}
