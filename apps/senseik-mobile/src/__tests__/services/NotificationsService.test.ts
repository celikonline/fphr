import { notificationsService } from "@/services/NotificationsService";
import { apiClient } from "@/lib/apiClient";

jest.mock("@/lib/apiClient");

describe("NotificationsService", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe("listMyNotifications", () => {
    it("fetches the caller's notifications with paging/filter params", async () => {
      const mockResponse = {
        data: {
          items: [
            {
              id: "n-1",
              templateKey: "approval.task_assigned",
              title: "Onayınız bekleniyor",
              body: "Ayşe Yılmaz'ın izin talebi onayınızı bekliyor.",
              createdAt: "2026-09-27T08:00:00Z",
            },
          ],
          page: 1,
          pageSize: 20,
          totalCount: 1,
          unreadCount: 1,
        },
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await notificationsService.listMyNotifications({
        page: 1,
        pageSize: 20,
        unreadOnly: true,
      });

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/notifications/my", {
        params: { page: 1, pageSize: 20, unreadOnly: true },
      });
    });

    it("defaults items to an empty array when the API omits it", async () => {
      (apiClient.get as jest.Mock).mockResolvedValue({
        data: { page: 1, pageSize: 20, totalCount: 0, unreadCount: 0 },
      });

      const result = await notificationsService.listMyNotifications();

      expect(result.items).toEqual([]);
    });
  });

  describe("getUnreadCount", () => {
    it("returns the count from the API", async () => {
      (apiClient.get as jest.Mock).mockResolvedValue({ data: { count: 3 } });

      const result = await notificationsService.getUnreadCount();

      expect(result).toBe(3);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/notifications/my/unread-count");
    });

    it("defaults to 0 when the API omits count", async () => {
      (apiClient.get as jest.Mock).mockResolvedValue({ data: {} });

      const result = await notificationsService.getUnreadCount();

      expect(result).toBe(0);
    });
  });

  describe("markNotificationRead", () => {
    it("posts to the mark-read endpoint for the given id", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({ data: undefined });

      await notificationsService.markNotificationRead("n-1");

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/notifications/my/n-1/read");
    });
  });

  describe("markAllNotificationsRead", () => {
    it("posts to the mark-all-read endpoint", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({ data: undefined });

      await notificationsService.markAllNotificationsRead();

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/notifications/my/read-all");
    });
  });

  describe("getPreferences", () => {
    it("fetches the caller's notification preferences", async () => {
      const mockResponse = {
        data: [
          {
            templateKey: "request.created",
            category: "request",
            isMandatory: false,
            channels: [{ channel: "email", enabled: true, locked: false }],
          },
        ],
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await notificationsService.getPreferences();

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/notifications/my/preferences");
    });

    it("defaults to an empty array when the API returns nothing", async () => {
      (apiClient.get as jest.Mock).mockResolvedValue({ data: null });

      const result = await notificationsService.getPreferences();

      expect(result).toEqual([]);
    });
  });

  describe("updatePreferences", () => {
    it("puts the changed items to the preferences endpoint", async () => {
      (apiClient.put as jest.Mock).mockResolvedValue({ data: undefined });

      await notificationsService.updatePreferences([
        { templateKey: "request.created", channel: "email", enabled: false },
      ]);

      expect(apiClient.put).toHaveBeenCalledWith("/api/v1/notifications/my/preferences", {
        items: [{ templateKey: "request.created", channel: "email", enabled: false }],
      });
    });
  });
});
