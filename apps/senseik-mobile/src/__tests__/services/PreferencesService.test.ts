import { preferencesService } from "@/services/PreferencesService";
import { apiClient } from "@/lib/apiClient";

jest.mock("@/lib/apiClient");

describe("PreferencesService (FG-001)", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe("updateMyLanguage", () => {
    it("puts the language to the caller's own identity endpoint", async () => {
      (apiClient.put as jest.Mock).mockResolvedValue({ status: 204 });

      await preferencesService.updateMyLanguage("en");

      expect(apiClient.put).toHaveBeenCalledWith("/api/v1/identity/me/language", {
        language: "en",
      });
    });

    it("propagates the API error", async () => {
      const apiError = {
        response: { status: 400, data: { code: "identity.user.language_unsupported" } },
      };
      (apiClient.put as jest.Mock).mockRejectedValue(apiError);

      await expect(preferencesService.updateMyLanguage("tr")).rejects.toEqual(apiError);
    });
  });

  describe("syncMyLanguage", () => {
    it("resolves true when the server accepts the language", async () => {
      (apiClient.put as jest.Mock).mockResolvedValue({ status: 204 });

      await expect(preferencesService.syncMyLanguage("tr")).resolves.toBe(true);
      expect(apiClient.put).toHaveBeenCalledWith("/api/v1/identity/me/language", {
        language: "tr",
      });
    });

    it("swallows a rejected language (400) and resolves false", async () => {
      (apiClient.put as jest.Mock).mockRejectedValue({
        response: { status: 400, data: { code: "identity.user.language_unsupported" } },
      });

      await expect(preferencesService.syncMyLanguage("en")).resolves.toBe(false);
    });

    it("swallows a network error and resolves false", async () => {
      (apiClient.put as jest.Mock).mockRejectedValue(new Error("Network Error"));

      await expect(preferencesService.syncMyLanguage("en")).resolves.toBe(false);
    });
  });
});
