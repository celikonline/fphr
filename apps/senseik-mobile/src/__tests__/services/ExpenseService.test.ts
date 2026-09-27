import { apiClient } from "@/lib/apiClient";
import { expenseService, ExpenseClaimDraftIncompleteError } from "@/services/ExpenseService";

jest.mock("@/lib/apiClient");

describe("ExpenseService", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe("getCategories", () => {
    it("fetches the tenant's expense category catalog", async () => {
      const mockResponse = {
        data: [{ id: "cat-1", code: "travel", nameKey: "expense.category.travel", isActive: true }],
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await expenseService.getCategories();

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/expense/categories");
    });
  });

  describe("getMyExpenseClaims", () => {
    it("fetches the caller's own expense claims", async () => {
      const mockResponse = {
        data: [{ id: "claim-1", employeeId: "e-1", totalAmount: 100, status: "draft", items: [] }],
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await expenseService.getMyExpenseClaims();

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/expense/claims/my");
    });
  });

  describe("submitSingleItemExpenseClaim (TK-031 decision #2: draft -> item -> submit)", () => {
    it("runs create -> addItem -> submit in that order and returns the claim id", async () => {
      (apiClient.post as jest.Mock)
        .mockResolvedValueOnce({ data: "claim-1" }) // create
        .mockResolvedValueOnce({ data: "item-1" }) // addItem
        .mockResolvedValueOnce({ data: undefined }); // submit

      const claimId = await expenseService.submitSingleItemExpenseClaim({
        categoryId: "cat-1",
        amount: 250.5,
        receiptDate: "2026-09-27",
        currency: "TRY",
        description: "Taksi",
      });

      expect(claimId).toBe("claim-1");
      expect(apiClient.post).toHaveBeenNthCalledWith(1, "/api/v1/expense/claims", {
        description: "Taksi",
      });
      expect(apiClient.post).toHaveBeenNthCalledWith(2, "/api/v1/expense/claims/claim-1/items", {
        categoryId: "cat-1",
        amount: 250.5,
        receiptDate: "2026-09-27",
        currency: "TRY",
        description: "Taksi",
      });
      expect(apiClient.post).toHaveBeenNthCalledWith(3, "/api/v1/expense/claims/claim-1/submit");
    });

    it("defaults the draft description to null when none is given", async () => {
      (apiClient.post as jest.Mock)
        .mockResolvedValueOnce({ data: "claim-1" })
        .mockResolvedValueOnce({ data: "item-1" })
        .mockResolvedValueOnce({ data: undefined });

      await expenseService.submitSingleItemExpenseClaim({
        categoryId: "cat-1",
        amount: 50,
        receiptDate: "2026-09-27",
        currency: "TRY",
      });

      expect(apiClient.post).toHaveBeenNthCalledWith(1, "/api/v1/expense/claims", {
        description: null,
      });
    });

    it("throws ExpenseClaimDraftIncompleteError carrying the claimId when adding the item fails - never leaves the caller unaware of the half-created draft", async () => {
      const addItemError = { response: { status: 400 } };
      (apiClient.post as jest.Mock)
        .mockResolvedValueOnce({ data: "claim-1" })
        .mockRejectedValueOnce(addItemError);

      let caughtError: unknown;
      try {
        await expenseService.submitSingleItemExpenseClaim({
          categoryId: "cat-1",
          amount: 50,
          receiptDate: "2026-09-27",
          currency: "TRY",
        });
      } catch (error) {
        caughtError = error;
      }

      expect(caughtError).toBeInstanceOf(ExpenseClaimDraftIncompleteError);
      expect(caughtError).toMatchObject({ claimId: "claim-1", cause: addItemError });
    });

    it("throws ExpenseClaimDraftIncompleteError when submit fails after the item was added", async () => {
      const submitError = { response: { status: 409 } };
      (apiClient.post as jest.Mock)
        .mockResolvedValueOnce({ data: "claim-1" })
        .mockResolvedValueOnce({ data: "item-1" })
        .mockRejectedValueOnce(submitError);

      await expect(
        expenseService.submitSingleItemExpenseClaim({
          categoryId: "cat-1",
          amount: 50,
          receiptDate: "2026-09-27",
          currency: "TRY",
        })
      ).rejects.toMatchObject({ claimId: "claim-1", cause: submitError });
    });
  });

  describe("getMyAdvanceRequests", () => {
    it("fetches the caller's own advance requests", async () => {
      const mockResponse = {
        data: [{ id: "adv-1", employeeId: "e-1", amount: 500, status: "submitted" }],
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await expenseService.getMyAdvanceRequests();

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/expense/advances/my");
    });
  });

  describe("createAdvanceRequest", () => {
    it("posts the advance request payload", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({ data: "adv-1" });

      const id = await expenseService.createAdvanceRequest({
        amount: 1000,
        currency: "TRY",
        requestedDate: "2026-09-27",
        isInstallmentRequested: false,
        description: null,
      });

      expect(id).toBe("adv-1");
      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/expense/advances", {
        amount: 1000,
        currency: "TRY",
        requestedDate: "2026-09-27",
        isInstallmentRequested: false,
        description: null,
      });
    });
  });

  describe("error mapping", () => {
    it("propagates the API error when fetching advance requests fails", async () => {
      const apiError = { response: { status: 403 } };
      (apiClient.get as jest.Mock).mockRejectedValue(apiError);

      await expect(expenseService.getMyAdvanceRequests()).rejects.toEqual(apiError);
    });
  });
});
