import {
  anomalyLabels,
  attendanceDayStatus,
  formatPunchLine,
  matchesAttendanceFilter,
} from "@/screens/app/attendance/attendanceStatus";

describe("attendanceDayStatus", () => {
  it("is completed when there is no anomaly", () => {
    expect(attendanceDayStatus({ anomalies: "none" })).toBe("completed");
    expect(attendanceDayStatus({ anomalies: "" })).toBe("completed");
  });

  it.each(["noData", "missingCheckIn", "missingCheckOut", "lateArrival, missingCheckOut"])(
    "is missing when a punch is missing (%s)",
    (anomalies) => {
      expect(attendanceDayStatus({ anomalies })).toBe("missing");
    }
  );

  it("is on leave when the day is covered by an approved leave", () => {
    expect(attendanceDayStatus({ anomalies: "workedWhileOnLeave" })).toBe("onLeave");
  });

  it.each(["lateArrival", "overwork", "earlyLeave, underwork", "unplannedWork"])(
    "is an anomaly for other flags (%s)",
    (anomalies) => {
      expect(attendanceDayStatus({ anomalies })).toBe("anomaly");
    }
  );
});

describe("matchesAttendanceFilter", () => {
  it("keeps everything for 'all'", () => {
    expect(matchesAttendanceFilter("completed", "all")).toBe(true);
    expect(matchesAttendanceFilter("missing", "all")).toBe(true);
  });

  it("keeps only missing days for 'missing'", () => {
    expect(matchesAttendanceFilter("missing", "missing")).toBe(true);
    expect(matchesAttendanceFilter("anomaly", "missing")).toBe(false);
  });

  it("keeps anomaly and worked-on-leave days for 'anomaly'", () => {
    expect(matchesAttendanceFilter("anomaly", "anomaly")).toBe(true);
    expect(matchesAttendanceFilter("onLeave", "anomaly")).toBe(true);
    expect(matchesAttendanceFilter("completed", "anomaly")).toBe(false);
    expect(matchesAttendanceFilter("missing", "anomaly")).toBe(false);
  });
});

describe("formatPunchLine", () => {
  it("shows HH:mm check-in and check-out", () => {
    expect(formatPunchLine({ checkIn: "09:02:00", checkOut: "18:10:30" })).toBe(
      "Giriş 09:02 · Çıkış 18:10"
    );
  });

  it("shows a dash for a missing punch", () => {
    expect(formatPunchLine({ checkIn: "09:02:00", checkOut: null })).toBe("Giriş 09:02 · Çıkış —");
    expect(formatPunchLine({})).toBe("Giriş — · Çıkış —");
  });
});

describe("anomalyLabels", () => {
  it("maps combined flags to Turkish labels (same wording as web)", () => {
    expect(anomalyLabels("lateArrival, overwork")).toEqual(["Geç giriş", "Fazla çalışma"]);
    expect(anomalyLabels("none")).toEqual([]);
  });
});
