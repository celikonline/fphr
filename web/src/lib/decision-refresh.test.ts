import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { QueryClient } from "@tanstack/react-query";
import { DECISION_REFRESH_DELAYS_MS, refreshAfterDecision } from "./decision-refresh";

describe("refreshAfterDecision (O-14)", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  function spyClient() {
    const queryClient = new QueryClient();
    const spy = vi.spyOn(queryClient, "invalidateQueries").mockResolvedValue(undefined);
    return { queryClient, spy };
  }

  it("invalidates the workflow and leave queries immediately", () => {
    const { queryClient, spy } = spyClient();

    refreshAfterDecision(queryClient);

    expect(spy).toHaveBeenCalledTimes(2);
    expect(spy).toHaveBeenCalledWith({ queryKey: ["workflow"] });
    expect(spy).toHaveBeenCalledWith({ queryKey: ["leave"] });
  });

  it("repeats the invalidation after each delay so a late outbox status flip is picked up", () => {
    const { queryClient, spy } = spyClient();

    refreshAfterDecision(queryClient);
    spy.mockClear();

    vi.advanceTimersByTime(DECISION_REFRESH_DELAYS_MS[0] - 1);
    expect(spy).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);
    expect(spy).toHaveBeenCalledTimes(2);

    vi.advanceTimersByTime(DECISION_REFRESH_DELAYS_MS[1] - DECISION_REFRESH_DELAYS_MS[0]);
    expect(spy).toHaveBeenCalledTimes(4);
  });
});
