import { attendanceService } from "@/services/AttendanceService";
import { apiClient } from "@/lib/apiClient";

jest.mock("@/lib/apiClient");

describe("AttendanceService", () => {
  beforeEach(() => {
    jest.clearAllMocks();
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
            allowedActions: []
          }
        ]
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await attendanceService.getMyAttendanceDays({ from: "2026-09-01", to: "2026-09-30" });

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/attendance/timesheet/me", {
        params: { from: "2026-09-01", to: "2026-09-30" }
      });
    });
  });

  describe("getMyTimesheetSummary", () => {
    it("fetches the caller's own period totals", async () => {
      const mockResponse = { data: { workedDays: 20, workedMinutes: 9600 } };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await attendanceService.getMyTimesheetSummary({ from: "2026-09-01", to: "2026-09-30" });

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/attendance/timesheet/me/summary", {
        params: { from: "2026-09-01", to: "2026-09-30" }
      });
    });

    it("propagates the API error when the caller has no employee record", async () => {
      const apiError = { response: { status: 404, data: { error: { message: "Çalışan bulunamadı" } } } };
      (apiClient.get as jest.Mock).mockRejectedValue(apiError);

      await expect(attendanceService.getMyTimesheetSummary({ from: "2026-09-01", to: "2026-09-30" })).rejects.toEqual(apiError);
    });
  });
});
