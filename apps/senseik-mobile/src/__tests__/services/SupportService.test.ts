import { supportService } from "@/services/SupportService";
import { apiClient } from "@/lib/apiClient";

jest.mock("@/lib/apiClient");

describe("SupportService", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe("searchArticles", () => {
    it("passes q as a query param when given", async () => {
      const articles = [{ slug: "a", category: "general", title: "A", relatedRoutes: [] }];
      (apiClient.get as jest.Mock).mockResolvedValue({ data: articles });

      await expect(supportService.searchArticles("izin")).resolves.toEqual(articles);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/support/articles", {
        params: { q: "izin" },
      });
    });

    it("omits params when q is not given", async () => {
      (apiClient.get as jest.Mock).mockResolvedValue({ data: [] });

      await supportService.searchArticles();

      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/support/articles", {
        params: undefined,
      });
    });

    it("propagates errors from apiClient", async () => {
      const error = new Error("network down");
      (apiClient.get as jest.Mock).mockRejectedValue(error);

      await expect(supportService.searchArticles("izin")).rejects.toThrow(error);
    });
  });

  describe("getArticle", () => {
    it("requests the article by slug and returns response.data", async () => {
      const article = {
        slug: "leave-request",
        category: "leaveAndOvertime",
        title: "İzin talebi",
        body: "...",
        media: [],
      };
      (apiClient.get as jest.Mock).mockResolvedValue({ data: article });

      await expect(supportService.getArticle("leave-request")).resolves.toEqual(article);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/support/articles/leave-request");
    });

    it("propagates errors from apiClient", async () => {
      const error = new Error("not found");
      (apiClient.get as jest.Mock).mockRejectedValue(error);

      await expect(supportService.getArticle("missing")).rejects.toThrow(error);
    });
  });

  describe("createTicket", () => {
    it("defaults sourceRoute to 'mobile' when not given", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({ data: "ticket-1" });

      await expect(
        supportService.createTicket({ subject: "Konu", body: "Gövde", category: "general" })
      ).resolves.toBe("ticket-1");
      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/support/tickets", {
        subject: "Konu",
        body: "Gövde",
        category: "general",
        sourceRoute: "mobile",
      });
    });

    it("keeps the given sourceRoute when provided", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({ data: "ticket-2" });

      await supportService.createTicket({
        subject: "Konu",
        body: "Gövde",
        category: "payroll",
        sourceRoute: "web",
      });

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/support/tickets", {
        subject: "Konu",
        body: "Gövde",
        category: "payroll",
        sourceRoute: "web",
      });
    });

    it("propagates errors from apiClient", async () => {
      const error = new Error("validation failed");
      (apiClient.post as jest.Mock).mockRejectedValue(error);

      await expect(
        supportService.createTicket({ subject: "Konu", body: "Gövde", category: "other" })
      ).rejects.toThrow(error);
    });
  });

  describe("listMyTickets", () => {
    it("requests the caller's tickets and returns response.data", async () => {
      const tickets = [
        {
          id: "1",
          requestedByUserId: "u1",
          subject: "Konu",
          body: "Gövde",
          category: "general" as const,
          channel: "mobile",
          status: "open" as const,
          sourceRoute: "mobile",
          resolution: null,
          answeredAt: null,
          closedAt: null,
          createdAt: "2026-09-27T08:00:00Z",
        },
      ];
      (apiClient.get as jest.Mock).mockResolvedValue({ data: tickets });

      await expect(supportService.listMyTickets()).resolves.toEqual(tickets);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/support/tickets/mine");
    });

    it("propagates errors from apiClient", async () => {
      const error = new Error("server error");
      (apiClient.get as jest.Mock).mockRejectedValue(error);

      await expect(supportService.listMyTickets()).rejects.toThrow(error);
    });
  });
});
