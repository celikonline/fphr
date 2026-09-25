import { beforeAll, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import ShiftTimeline, { formatMinute, shiftSegments, snapToStep } from "./shift-timeline";

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (q: string) => ({
      matches: false,
      media: q,
      addEventListener() {},
      removeEventListener() {},
    }),
  });
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
});

describe("shift timeline helpers", () => {
  it("timeline_snaps_to_15_minutes", () => {
    expect(snapToStep(7)).toBe(0);
    expect(snapToStep(8)).toBe(15);
    expect(snapToStep(547)).toBe(540);
    expect(snapToStep(1439)).toBe(1440);
    expect(snapToStep(-5)).toBe(0);
  });

  it("overnight_shift_wraps_display", () => {
    expect(shiftSegments(480, 1020)).toEqual([{ from: 480, to: 1020 }]);
    expect(shiftSegments(1320, 360)).toEqual([
      { from: 1320, to: 1440 },
      { from: 0, to: 360 },
    ]);
    expect(shiftSegments(600, 600)).toEqual([
      { from: 600, to: 1440 },
      { from: 0, to: 600 },
    ]);
    expect(formatMinute(1320)).toBe("22:00");
  });
});

describe("ShiftTimeline", () => {
  const wrap = (ui: React.ReactElement) => render(<MantineProvider>{ui}</MantineProvider>);

  it("timeline_snaps_to_15_minutes: keyboard step moves 15 minutes", () => {
    const onChange = vi.fn();
    wrap(<ShiftTimeline startMinute={480} endMinute={1020} breaks={[]} onChange={onChange} />);
    const start = screen.getByRole("slider", { name: "start" });
    fireEvent.keyDown(start, { key: "ArrowRight" });
    expect(onChange).toHaveBeenCalledWith({ startMinute: 495, endMinute: 1020 });
  });

  it("overnight_shift_wraps_display: renders two segments", () => {
    wrap(<ShiftTimeline startMinute={1320} endMinute={360} breaks={[]} onChange={() => {}} />);
    expect(screen.getAllByTestId("shift-segment")).toHaveLength(2);
  });

  it("flexible_hides_fixed_end: no end slider when hideEnd", () => {
    wrap(
      <ShiftTimeline startMinute={480} endMinute={1020} breaks={[]} hideEnd onChange={() => {}} />
    );
    expect(screen.queryByRole("slider", { name: "end" })).toBeNull();
    expect(screen.getByRole("slider", { name: "start" })).toBeInTheDocument();
  });
});
