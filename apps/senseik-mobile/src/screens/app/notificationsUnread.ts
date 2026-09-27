/**
 * N1: pure unread-count transitions for the bell badge, kept out of `NotificationsScreen`/
 * `DashboardScreen` so the arithmetic (never go below zero, "mark all" always zeroes it) is
 * unit-testable without mounting either screen.
 */

/** After marking a single notification read: unchanged if it was already read, else -1 (never below 0). */
export function unreadCountAfterMarkingRead(current: number, wasAlreadyRead: boolean): number {
  if (wasAlreadyRead) {
    return current;
  }
  return Math.max(0, current - 1);
}

/** After "Tümünü okundu işaretle". */
export function unreadCountAfterMarkingAllRead(): number {
  return 0;
}
