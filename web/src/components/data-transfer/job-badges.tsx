import { useTranslation } from "react-i18next";
import { Badge, type BadgeProps } from "@/components/ui/badge";
import type { ImportJobStatus, ImportRowOutcome } from "@/services/data-transfer.service";

const STATUS_VARIANT: Record<ImportJobStatus, BadgeProps["variant"]> = {
  Uploaded: "secondary",
  Mapped: "outline",
  Completed: "success",
  CompletedWithErrors: "default",
  Failed: "destructive",
};

const OUTCOME_VARIANT: Record<ImportRowOutcome, BadgeProps["variant"]> = {
  Inserted: "success",
  Updated: "default",
  Skipped: "secondary",
  Error: "destructive",
};

export function JobStatusBadge({ status }: { status: string }) {
  const { t } = useTranslation(["dataTransfer"]);
  return (
    <Badge variant={STATUS_VARIANT[status as ImportJobStatus] ?? "outline"} className="whitespace-nowrap">
      {t(`dataTransfer:status.${status}`, status)}
    </Badge>
  );
}

export function RowOutcomeBadge({ outcome }: { outcome: string }) {
  const { t } = useTranslation(["dataTransfer"]);
  return (
    <Badge variant={OUTCOME_VARIANT[outcome as ImportRowOutcome] ?? "outline"} className="whitespace-nowrap">
      {t(`dataTransfer:outcome.${outcome}`, outcome)}
    </Badge>
  );
}
