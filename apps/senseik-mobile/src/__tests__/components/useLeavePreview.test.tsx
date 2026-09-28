import React from "react";
import { act, create, type ReactTestRenderer } from "react-test-renderer";
import { useLeavePreview } from "@/components/leave/useLeavePreview";
import {
  leaveService,
  type LeaveHalfDayPeriod,
  type LeaveRequestPreview,
  type RequestableLeaveType,
} from "@/services/LeaveService";

jest.mock("@/services/LeaveService");

const mockedPreviewMyLeaveRequest = leaveService.previewMyLeaveRequest as jest.Mock;

type Hook = ReturnType<typeof useLeavePreview>;
type SelectedType = Pick<RequestableLeaveType, "id" | "unit"> | null;

type ProbeProps = {
  selectedType: SelectedType;
  startDate: string;
  endDate: string;
  startTime: string;
  endTime: string;
  halfDayPeriod: LeaveHalfDayPeriod;
};

let latest: Hook | null = null;

function Probe(props: ProbeProps) {
  latest = useLeavePreview(
    props.selectedType,
    props.startDate,
    props.endDate,
    props.startTime,
    props.endTime,
    props.halfDayPeriod
  );
  return null;
}

const baseProps: ProbeProps = {
  selectedType: { id: "type-1", unit: "fullDay" },
  startDate: "2026-09-28",
  endDate: "2026-09-30",
  startTime: "09:00",
  endTime: "18:00",
  halfDayPeriod: "morning",
};

function buildPreview(overrides: Partial<LeaveRequestPreview> = {}): LeaveRequestPreview {
  return { days: 2, weeklyRestDays: 0, publicHolidayDays: 0, warnings: [], ...overrides };
}

async function flush() {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
}

async function advance(ms: number) {
  await act(async () => {
    jest.advanceTimersByTime(ms);
    await Promise.resolve();
    await Promise.resolve();
  });
}

let renderer: ReactTestRenderer | null = null;

async function mount(props: ProbeProps) {
  await act(async () => {
    renderer = create(<Probe {...props} />);
  });
  await flush();
}

async function update(props: ProbeProps) {
  await act(async () => {
    renderer?.update(<Probe {...props} />);
  });
  await flush();
}

beforeEach(() => {
  jest.useFakeTimers();
  jest.clearAllMocks();
  latest = null;
  renderer = null;
});

afterEach(() => {
  jest.useRealTimers();
});

describe("useLeavePreview (B-mobil-006)", () => {
  it("does not call the API before the 300ms debounce elapses", async () => {
    mockedPreviewMyLeaveRequest.mockResolvedValue(buildPreview());
    await mount(baseProps);

    await advance(299);
    expect(mockedPreviewMyLeaveRequest).not.toHaveBeenCalled();

    await advance(1);
    expect(mockedPreviewMyLeaveRequest).toHaveBeenCalledTimes(1);
  });

  it("resets preview/previewError and skips the request once buildPreviewInput returns null (no selected type)", async () => {
    mockedPreviewMyLeaveRequest.mockResolvedValue(buildPreview());
    await mount(baseProps);
    await advance(300);
    expect(latest?.preview).toEqual(buildPreview());

    await update({ ...baseProps, selectedType: null });
    expect(latest?.preview).toBeNull();
    expect(latest?.previewError).toBeNull();
    expect(latest?.previewLoading).toBe(false);

    await advance(500);
    expect(mockedPreviewMyLeaveRequest).toHaveBeenCalledTimes(1);
  });

  it("only writes the latest (most recent) request's result when an earlier request resolves after a newer one", async () => {
    let resolveFirst: (value: LeaveRequestPreview) => void = () => {};
    let resolveSecond: (value: LeaveRequestPreview) => void = () => {};
    const firstPromise = new Promise<LeaveRequestPreview>((resolve) => {
      resolveFirst = resolve;
    });
    const secondPromise = new Promise<LeaveRequestPreview>((resolve) => {
      resolveSecond = resolve;
    });
    mockedPreviewMyLeaveRequest
      .mockImplementationOnce(() => firstPromise)
      .mockImplementationOnce(() => secondPromise);

    await mount(baseProps);
    await advance(300);
    expect(mockedPreviewMyLeaveRequest).toHaveBeenCalledTimes(1);

    // A field changes before the first request resolves - this fires a second (newer) request.
    await update({ ...baseProps, endDate: "2026-10-05" });
    await advance(300);
    expect(mockedPreviewMyLeaveRequest).toHaveBeenCalledTimes(2);

    const secondResult = buildPreview({ days: 5 });
    await act(async () => {
      resolveSecond(secondResult);
      await Promise.resolve();
      await Promise.resolve();
    });
    expect(latest?.preview).toEqual(secondResult);

    // The stale first request finally resolves - its result must be discarded (previewRequestIdRef guard).
    const firstResult = buildPreview({ days: 99 });
    await act(async () => {
      resolveFirst(firstResult);
      await Promise.resolve();
      await Promise.resolve();
    });
    expect(latest?.preview).toEqual(secondResult);
  });

  it("fills previewError with getApiErrorMessage's fallback when previewMyLeaveRequest rejects", async () => {
    mockedPreviewMyLeaveRequest.mockRejectedValue(new Error("network down"));
    await mount(baseProps);
    await advance(300);

    expect(latest?.preview).toBeNull();
    expect(latest?.previewError).toBe("Önizleme yüklenemedi.");
    expect(latest?.previewLoading).toBe(false);
  });
});
