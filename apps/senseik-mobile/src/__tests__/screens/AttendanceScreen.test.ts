import { formatOvertimeUndertime } from "@/screens/app/AttendanceScreen";

describe("formatOvertimeUndertime", () => {
  it("shows fazla mesai when worked minutes exceed planned minutes", () => {
    const result = formatOvertimeUndertime({ plannedMinutes: 480, workedMinutes: 540 });

    expect(result).toBe("Fazla mesai: 1 sa 0 dk");
  });

  it("shows eksik mesai when worked minutes are below planned minutes", () => {
    const result = formatOvertimeUndertime({ plannedMinutes: 480, workedMinutes: 420 });

    expect(result).toBe("Eksik mesai: 1 sa 0 dk");
  });

  it("returns null when worked minutes match planned minutes exactly", () => {
    const result = formatOvertimeUndertime({ plannedMinutes: 480, workedMinutes: 480 });

    expect(result).toBeNull();
  });

  it("returns null on a non-working day (no planned minutes), even if hours were worked", () => {
    const result = formatOvertimeUndertime({ plannedMinutes: 0, workedMinutes: 60 });

    expect(result).toBeNull();
  });
});
