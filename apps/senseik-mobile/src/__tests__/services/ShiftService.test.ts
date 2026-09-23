import { shiftService } from "@/services/ShiftService";
import { apiClient } from "@/lib/apiClient";

jest.mock("@/lib/apiClient");

describe("ShiftService", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe("getMyShifts", () => {
    it("fetches the caller's own published shifts for a date range", async () => {
      const mockResponse = {
        data: [
          {
            id: "shift-1",
            employeeId: "employee-1",
            date: "2026-09-22",
            start: "09:00:00",
            end: "18:00:00",
            breakMinutes: 60,
            isNight: false,
            note: null,
            status: "published",
            publishedAt: "2026-09-20T12:00:00Z",
            shiftMinutes: 480,
          },
        ],
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await shiftService.getMyShifts({ from: "2026-09-22", to: "2026-09-28" });

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/attendance/shifts/me", {
        params: { from: "2026-09-22", to: "2026-09-28" },
      });
    });
  });

  describe("getTeamShifts", () => {
    it("fetches the shifts the caller's data scope allows", async () => {
      const mockResponse = { data: [] as unknown[] };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await shiftService.getTeamShifts({ from: "2026-09-22", to: "2026-09-28" });

      expect(result).toEqual([]);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/attendance/shifts", {
        params: { from: "2026-09-22", to: "2026-09-28" },
      });
    });

    it("propagates a 403 when the caller has no attendance.shift.view permission", async () => {
      const apiError = { response: { status: 403, data: { error: { message: "Forbidden" } } } };
      (apiClient.get as jest.Mock).mockRejectedValue(apiError);

      await expect(
        shiftService.getTeamShifts({ from: "2026-09-22", to: "2026-09-28" })
      ).rejects.toEqual(apiError);
    });
  });

  describe("availability", () => {
    it("fetches the caller's own active availability entries", async () => {
      const mockResponse = {
        data: [
          {
            id: "availability-1",
            employeeId: "employee-1",
            date: "2026-09-25",
            start: null,
            end: null,
            kind: "unavailable",
            note: null,
            status: "active",
          },
        ],
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await shiftService.getMyAvailability({ from: "2026-09-22", to: "2026-09-28" });

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/attendance/availability/me", {
        params: { from: "2026-09-22", to: "2026-09-28" },
      });
    });

    it("submits a whole-day unavailability entry", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({ data: "availability-1" });
      const input = { date: "2026-09-25", kind: "unavailable" as const, note: "Doktor randevusu" };

      const result = await shiftService.submitMyAvailability(input);

      expect(result).toBe("availability-1");
      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/attendance/availability/me", input);
    });

    it("cancels an existing availability entry", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({ data: undefined });

      await shiftService.cancelMyAvailability("availability-1");

      expect(apiClient.post).toHaveBeenCalledWith(
        "/api/v1/attendance/availability/me/availability-1/cancel"
      );
    });
  });
});
