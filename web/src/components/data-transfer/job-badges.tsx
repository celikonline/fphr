import { useTranslation } from "react-i18next";
import { Badge, type BadgeProps } from "@mantine/core";
import type { ImportJobStatus, ImportRowOutcome } from "@/services/data-transfer.service";

type BadgeLook = Pick<BadgeProps, "variant" | "color">;

const OUTLINE: BadgeLook = { variant: "outline", color: "gray" };

const STATUS_LOOK: Record<ImportJobStatus, BadgeLook> = {
  Uploaded: { variant: "light", color: "gray" },
  Mapped: OUTLINE,
  Completed: { variant: "light", color: "green" },
  CompletedWithErrors: { variant: "filled" },
  Failed: { variant: "filled", color: "red" },
};

const OUTCOME_LOOK: Record<ImportRowOutcome, BadgeLook> = {
  Inserted: { variant: "light", color: "green" },
  Updated: { variant: "filled" },
  Skipped: { variant: "light", color: "gray" },
  Error: { variant: "filled", color: "red" },
};

export function JobStatusBadge({ status }: { status: string }) {
  const { t } = useTranslation(["dataTransfer"]);
  return (
    <Badge {...(STATUS_LOOK[status as ImportJobStatus] ?? OUTLINE)} className="shrink-0">
      {t(`dataTransfer:status.${status}`, status)}
    </Badge>
  );
}

export function RowOutcomeBadge({ outcome }: { outcome: string }) {
  const { t } = useTranslation(["dataTransfer"]);
  return (
    <Badge {...(OUTCOME_LOOK[outcome as ImportRowOutcome] ?? OUTLINE)} className="shrink-0">
      {t(`dataTransfer:outcome.${outcome}`, outcome)}
    </Badge>
  );
}
