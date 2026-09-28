import type { GoalStatus } from "@/services/GoalsService";
import type { Tone } from "@/theme/theme";

export const GOAL_STATUS_LABELS: Record<GoalStatus, string> = {
  open: "Açık",
  inProgress: "Devam ediyor",
  completed: "Tamamlandı",
  cancelled: "İptal edildi",
  archived: "Arşivlendi",
};

/** Maps a `GoalStatus` onto the shared `StatusPill` tone palette (mobil yeniden tasarım Task 6). */
export function goalStatusTone(status: GoalStatus): Tone {
  switch (status) {
    case "completed":
      return "success";
    case "inProgress":
      return "info";
    case "cancelled":
      return "danger";
    case "open":
    case "archived":
    default:
      return "neutral";
  }
}

/** Goals whose progress the caller can still edit self-service (mirrors `Goal.UpdateProgress` domain guard intent). */
export const GOAL_EDITABLE_STATUSES: ReadonlySet<GoalStatus> = new Set(["open", "inProgress"]);
