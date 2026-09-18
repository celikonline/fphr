import { describe, it, expect, vi, beforeEach } from "vitest";
import * as accessSettingsService from "./access-settings.service";
import type { AccessSettings } from "@/types";

// Mock apiClient
// vi.mock factories are hoisted above regular top-level statements, so the
// mocked value must be created with vi.hoisted() to avoid a TDZ error.
const mockApiClient = vi.hoisted(() => ({
  get: vi.fn(),
  put: vi.fn(),
}));

vi.mock("@senseik/api-client", () => ({
  apiClient: mockApiClient,
}));

describe("access-settings.service", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("getAccessSettings", () => {
    it("fetches access settings from correct endpoint", async () => {
      const mockSettings: AccessSettings = {
        showOrgInfo: true,
        showUpcomingLeaves: false,
        showUpcomingBirthdays: true,
        showEventCalendar: true,
        showFiles: false,
        showAssets: true,
        showWorkPhoneNumbers: false,
        showOrgChart: true,
        showCustomFields: false,
        showCalendarMenu: true,
        showAllLeavesInCalendar: false,
      };

      mockApiClient.get.mockResolvedValueOnce({ data: mockSettings });

      const result = await accessSettingsService.getAccessSettings();

      expect(mockApiClient.get).toHaveBeenCalledWith("/v1/identity/settings/access");
      expect(result).toEqual(mockSettings);
    });

    it("returns all access setting keys", async () => {
      const mockSettings: AccessSettings = {
        showOrgInfo: true,
        showUpcomingLeaves: true,
        showUpcomingBirthdays: true,
        showEventCalendar: true,
        showFiles: true,
        showAssets: true,
        showWorkPhoneNumbers: true,
        showOrgChart: true,
        showCustomFields: true,
        showCalendarMenu: true,
        showAllLeavesInCalendar: true,
      };

      mockApiClient.get.mockResolvedValueOnce({ data: mockSettings });

      const result = await accessSettingsService.getAccessSettings();

      // Verify each group's keys are present
      const homeKeys = ["showOrgInfo", "showUpcomingLeaves", "showUpcomingBirthdays", "showEventCalendar"];
      const accountKeys = ["showFiles", "showAssets", "showWorkPhoneNumbers", "showOrgChart", "showCustomFields"];
      const calendarKeys = ["showCalendarMenu", "showAllLeavesInCalendar"];

      [...homeKeys, ...accountKeys, ...calendarKeys].forEach((key: string) => {
        expect(result).toHaveProperty(key);
      });
    });

    it("handles null/missing fields safely (WhenWritingNull)", async () => {
      const partialSettings = {
        showOrgInfo: true,
        showUpcomingLeaves: false,
        // other fields omitted by API (WhenWritingNull behavior)
      } as unknown as AccessSettings;

      mockApiClient.get.mockResolvedValueOnce({ data: partialSettings });

      const result = await accessSettingsService.getAccessSettings();

      expect(result.showOrgInfo).toBe(true);
      expect(result.showUpcomingLeaves).toBe(false);
      // Missing fields should be undefined (or kept as-is)
      expect(result.showEventCalendar).toBeUndefined();
    });
  });

  describe("updateAccessSettings", () => {
    it("sends updated settings to correct endpoint", async () => {
      const updates: AccessSettings = {
        showOrgInfo: false,
        showUpcomingLeaves: true,
        showUpcomingBirthdays: false,
        showEventCalendar: true,
        showFiles: true,
        showAssets: false,
        showWorkPhoneNumbers: true,
        showOrgChart: false,
        showCustomFields: true,
        showCalendarMenu: true,
        showAllLeavesInCalendar: false,
      };

      mockApiClient.put.mockResolvedValueOnce({ data: null });

      await accessSettingsService.updateAccessSettings(updates);

      expect(mockApiClient.put).toHaveBeenCalledWith("/v1/identity/settings/access", updates);
    });

    it("returns undefined on successful update (204 No Content)", async () => {
      const updates: AccessSettings = {
        showOrgInfo: true,
        showUpcomingLeaves: true,
        showUpcomingBirthdays: true,
        showEventCalendar: true,
        showFiles: true,
        showAssets: true,
        showWorkPhoneNumbers: true,
        showOrgChart: true,
        showCustomFields: true,
        showCalendarMenu: true,
        showAllLeavesInCalendar: true,
      };

      mockApiClient.put.mockResolvedValueOnce({ data: null });

      const result = await accessSettingsService.updateAccessSettings(updates);

      expect(result).toBeUndefined();
    });

    it("allows partial update with mixed boolean values", async () => {
      const mixedSettings: AccessSettings = {
        showOrgInfo: true,
        showUpcomingLeaves: false,
        showUpcomingBirthdays: true,
        showEventCalendar: false,
        showFiles: true,
        showAssets: false,
        showWorkPhoneNumbers: true,
        showOrgChart: false,
        showCustomFields: true,
        showCalendarMenu: false,
        showAllLeavesInCalendar: true,
      };

      mockApiClient.put.mockResolvedValueOnce({ data: null });

      await accessSettingsService.updateAccessSettings(mixedSettings);

      const callArgument = mockApiClient.put.mock.calls[0][1];
      expect(callArgument).toEqual(mixedSettings);
    });
  });

  describe("ACCESS_SETTING_GROUPS", () => {
    it("defines three access setting groups", () => {
      expect(accessSettingsService.ACCESS_SETTING_GROUPS).toHaveLength(3);
    });

    it("defines home group with correct keys", () => {
      const homeGroup = accessSettingsService.ACCESS_SETTING_GROUPS.find((g) => g.group === "home");
      expect(homeGroup).toBeDefined();
      expect(homeGroup!.keys).toEqual([
        "showOrgInfo",
        "showUpcomingLeaves",
        "showUpcomingBirthdays",
        "showEventCalendar",
      ]);
    });

    it("defines account group with correct keys", () => {
      const accountGroup = accessSettingsService.ACCESS_SETTING_GROUPS.find((g) => g.group === "account");
      expect(accountGroup).toBeDefined();
      expect(accountGroup!.keys).toEqual([
        "showFiles",
        "showAssets",
        "showWorkPhoneNumbers",
        "showOrgChart",
        "showCustomFields",
      ]);
    });

    it("defines calendar group with correct keys", () => {
      const calendarGroup = accessSettingsService.ACCESS_SETTING_GROUPS.find((g) => g.group === "calendar");
      expect(calendarGroup).toBeDefined();
      expect(calendarGroup!.keys).toEqual(["showCalendarMenu", "showAllLeavesInCalendar"]);
    });
  });

  describe("error handling", () => {
    it("propagates getAccessSettings API error", async () => {
      const apiError = new Error("Server error");

      mockApiClient.get.mockRejectedValueOnce(apiError);

      await expect(accessSettingsService.getAccessSettings()).rejects.toBe(apiError);
    });

    it("propagates updateAccessSettings API error", async () => {
      const apiError = new Error("Permission denied");
      const settings: AccessSettings = {
        showOrgInfo: true,
        showUpcomingLeaves: true,
        showUpcomingBirthdays: true,
        showEventCalendar: true,
        showFiles: true,
        showAssets: true,
        showWorkPhoneNumbers: true,
        showOrgChart: true,
        showCustomFields: true,
        showCalendarMenu: true,
        showAllLeavesInCalendar: true,
      };

      mockApiClient.put.mockRejectedValueOnce(apiError);

      await expect(accessSettingsService.updateAccessSettings(settings)).rejects.toBe(apiError);
    });
  });
});
