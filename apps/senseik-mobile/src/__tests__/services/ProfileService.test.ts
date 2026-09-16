import { profileService } from "@/services/ProfileService";
import { apiClient } from "@/lib/apiClient";

jest.mock("@/lib/apiClient");

describe("ProfileService", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe("getMyProfile", () => {
    it("fetches the caller's own employee profile", async () => {
      const mockResponse = {
        data: {
          displayName: "Ayşe Yılmaz",
          titleName: "Yazılım Mühendisi",
          departmentName: "Mühendislik",
          hireDate: "2022-03-01",
          workEmail: "ayse.yilmaz@ornek.com",
          mobilePhone: "+90 555 000 00 00",
          workPhone: null
        }
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await profileService.getMyProfile();

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/employees/me");
    });

    it("propagates the API error when the caller has no employee record", async () => {
      const apiError = { response: { status: 404, data: { error: { message: "Çalışan bulunamadı" } } } };
      (apiClient.get as jest.Mock).mockRejectedValue(apiError);

      await expect(profileService.getMyProfile()).rejects.toEqual(apiError);
    });
  });
});
