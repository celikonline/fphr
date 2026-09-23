import { apiClient } from "@/lib/apiClient";

/**
 * Performance/Goals module (self-service "Hedeflerim"): mirrors `GoalsController.cs`'s `/performance/goals/me`
 * group (`GET me`, `POST {id}/progress/me`) - the same self-service surface `LeaveService`/`ApprovalsService`
 * follow for their modules. Only what a phone needs: the caller's own goals and updating their own progress.
 * There is no goal-approval endpoint in the backend today (`GoalsController.cs` has no such action, and
 * `GoalStatus` - Open/InProgress/Completed/Cancelled/Archived - has no pending-approval state) - see TK-028
 * handoff note; HR/manager-facing endpoints (create for employee, team list, cancel) stay web-only for now.
 */
const GOALS_BASE = "/api/v1/performance/goals";

/** Mirrors `GoalStatus` (Sense.Modules.Performance.Domain.Goals), camelCase JsonStringEnumConverter. */
export type GoalStatus = "open" | "inProgress" | "completed" | "cancelled" | "archived";

/** `GoalDto`: `employeeName` is only populated on the team view, which this self-service surface does not call. */
export interface Goal {
  id: string;
  employeeId: string;
  title: string;
  description?: string | null;
  targetDate: string;
  progress: number;
  status: GoalStatus;
  reviewCycleId?: string | null;
  employeeName?: string | null;
}

export class GoalsService {
  async getMyGoals(): Promise<Goal[]> {
    const response = await apiClient.get<Goal[]>(`${GOALS_BASE}/me`);
    return response.data;
  }

  async updateMyGoalProgress(id: string, progress: number): Promise<void> {
    await apiClient.post(`${GOALS_BASE}/${id}/progress/me`, { progress });
  }
}

export const goalsService = new GoalsService();
