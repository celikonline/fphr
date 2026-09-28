import { attendanceService } from "@/services/AttendanceService";
import { apiClient } from "@/lib/apiClient";

jest.mock("@/lib/apiClient");

describe("AttendanceService", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe("self check-in", () => {
    it("reads whether the caller is currently checked in", async () => {
      const data = { isCheckedIn: true, date: "2026-09-21", checkIn: "09:00:00" };
      (apiClient.get as jest.Mock).mockResolvedValue({ data });

      const result = await attendanceService.getMyCheckInStatus();

      expect(result).toEqual(data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/attendance/self-checkin/status");
    });

    it("posts the scanned QR code with direction and position", async () => {
      const data = {
        attendanceDayId: "day-1",
        date: "2026-09-21",
        direction: "in",
        time: "09:00:00",
      };
      (apiClient.post as jest.Mock).mockResolvedValue({ data });
      const input = { code: "AB12CD34", direction: "in" as const, latitude: 39.9, longitude: 32.8 };

      const result = await attendanceService.qrCheckIn(input);

      expect(result).toEqual(data);
      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/attendance/self-checkin/qr", input);
    });

    it("posts a GPS punch, including the break directions", async () => {
      const data = {
        attendanceDayId: "day-1",
        date: "2026-09-27",
        direction: "breakStart",
        time: "12:00:00",
      };
      (apiClient.post as jest.Mock).mockResolvedValue({ data });
      const input = { latitude: 41.01, longitude: 28.97, direction: "breakStart" as const };

      const result = await attendanceService.gpsCheckIn(input);

      expect(result).toEqual(data);
      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/attendance/self-checkin/gps", input);
    });
  });

  describe("getMyAttendanceDays", () => {
    it("fetches the caller's own recorded days for a date range", async () => {
      const mockResponse = {
        data: [
          {
            id: "day-1",
            employeeId: "employee-1",
            date: "2026-09-16",
            checkIn: "09:00:00",
            checkOut: "18:00:00",
            plannedStart: "09:00:00",
            plannedEnd: "18:00:00",
            plannedMinutes: 480,
            workedMinutes: 480,
            anomalies: "none",
            resolutionState: "unresolved",
            source: "manual",
            allowedActions: [],
          },
        ],
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await attendanceService.getMyAttendanceDays({
        from: "2026-09-01",
        to: "2026-09-30",
      });

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/attendance/timesheet/me", {
        params: { from: "2026-09-01", to: "2026-09-30" },
      });
    });

    it("opts into leave-only rows via includeLeaveDays, unlike the default call", async () => {
      (apiClient.get as jest.Mock).mockResolvedValue({ data: [] });

      await attendanceService.getMyAttendanceDays({ from: "2026-09-01", to: "2026-09-30" }, true);

      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/attendance/timesheet/me", {
        params: { from: "2026-09-01", to: "2026-09-30", includeLeaveDays: true },
      });
    });
  });

  describe("getMyTimesheetSummary", () => {
    it("fetches the caller's own period totals", async () => {
      const mockResponse = { data: { workedDays: 20, workedMinutes: 9600 } };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await attendanceService.getMyTimesheetSummary({
        from: "2026-09-01",
        to: "2026-09-30",
      });

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/attendance/timesheet/me/summary", {
        params: { from: "2026-09-01", to: "2026-09-30" },
      });
    });

    it("propagates the API error when the caller has no employee record", async () => {
      const apiError = {
        response: { status: 404, data: { error: { message: "Çalışan bulunamadı" } } },
      };
      (apiClient.get as jest.Mock).mockRejectedValue(apiError);

      await expect(
        attendanceService.getMyTimesheetSummary({ from: "2026-09-01", to: "2026-09-30" })
      ).rejects.toEqual(apiError);
    });
  });
});
