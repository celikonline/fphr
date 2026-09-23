import { peopleService } from "@/services/PeopleService";
import { apiClient } from "@/lib/apiClient";

jest.mock("@/lib/apiClient");

describe("PeopleService", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe("listPeople", () => {
    it("fetches the company directory and returns the paged result", async () => {
      const mockResponse = {
        data: {
          items: [
            {
              id: "11111111-1111-1111-1111-111111111111",
              displayName: "Ayşe Yılmaz",
              titleName: "Yazılım Mühendisi",
              departmentName: "Mühendislik",
              workEmail: "ayse.yilmaz@ornek.com",
              workPhone: "+90 555 000 00 00",
            },
          ],
          page: 1,
          pageSize: 100,
          totalCount: 1,
          totalPages: 1,
          hasNext: false,
          hasPrevious: false,
        },
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await peopleService.listPeople({ q: "Ayşe", pageSize: 100 });

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/employees/directory", {
        params: { q: "Ayşe", page: undefined, pageSize: 100 },
      });
    });

    it("omits an empty search term instead of sending q=''", async () => {
      const mockResponse = {
        data: {
          items: [],
          page: 1,
          pageSize: 100,
          totalCount: 0,
          totalPages: 0,
          hasNext: false,
          hasPrevious: false,
        },
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      await peopleService.listPeople({ q: "" });

      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/employees/directory", {
        params: { q: undefined, page: undefined, pageSize: undefined },
      });
    });

    it("propagates the API error", async () => {
      const apiError = { response: { status: 403, data: { error: { message: "Yetkiniz yok" } } } };
      (apiClient.get as jest.Mock).mockRejectedValue(apiError);

      await expect(peopleService.listPeople()).rejects.toEqual(apiError);
    });
  });
});
