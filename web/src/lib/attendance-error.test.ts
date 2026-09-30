import { describe, expect, it } from "vitest";
import { AxiosError } from "axios";
import { getAttendanceErrorMessage } from "./attendance-error";

const t = (key: string) => `t:${key}`;

function problem(code: string, detail: string) {
  return new AxiosError("x", "400", undefined, undefined, {
    status: 403, statusText: "", headers: {}, config: {} as never, data: { code, detail },
  });
}

describe("getAttendanceErrorMessage", () => {
  it("translates the locked-month code instead of trusting the server text", () => {
    expect(getAttendanceErrorMessage(problem("attendance.timesheet.period_locked", "server text"), "fb", t)).toBe(
      "t:attendance:errors.periodLocked",
    );
  });

  it("falls back to the server detail, then the fallback", () => {
    expect(getAttendanceErrorMessage(problem("other", "server text"), "fb", t)).toBe("server text");
    expect(getAttendanceErrorMessage(new Error("boom"), "fb", t)).toBe("fb");
  });
});
