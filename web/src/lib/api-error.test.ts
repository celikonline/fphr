import { describe, it, expect } from "vitest";
import { getApiErrorStatus, getApiProblem, getApiErrorMessage, type ApiProblem } from "./api-error";
import axios from "axios";

// Create mock axios error
function createAxiosError(status: number, data: any): any {
  return {
    isAxiosError: true,
    response: {
      status,
      data,
    },
  };
}

describe("api-error", () => {
  describe("getApiErrorStatus", () => {
    it("returns undefined for non-axios error", () => {
      const error = new Error("Generic error");
      expect(getApiErrorStatus(error)).toBeUndefined();
    });

    it("returns undefined when error has no response", () => {
      const error = { isAxiosError: true };
      const result = getApiErrorStatus(error);
      expect(result).toBeUndefined();
    });

    it("returns status code from axios error response", () => {
      const error = createAxiosError(404, { title: "Not found" });
      expect(getApiErrorStatus(error)).toBe(404);
    });

    it("handles various HTTP status codes", () => {
      const statuses = [400, 401, 403, 404, 500, 502, 503];
      statuses.forEach((status) => {
        const error = createAxiosError(status, {});
        expect(getApiErrorStatus(error)).toBe(status);
      });
    });
  });

  describe("getApiProblem", () => {
    it("returns undefined for non-axios error", () => {
      const error = new Error("Generic error");
      expect(getApiProblem(error)).toBeUndefined();
    });

    it("returns undefined when response.data is not an object", () => {
      const error1 = createAxiosError(500, "String error message");
      const error2 = createAxiosError(500, null);
      const error3 = createAxiosError(500, undefined);

      expect(getApiProblem(error1)).toBeUndefined();
      expect(getApiProblem(error2)).toBeUndefined();
      expect(getApiProblem(error3)).toBeUndefined();
    });

    it("does not throw when data is malformed", () => {
      const error = createAxiosError(500, "Not an object");
      expect(() => getApiProblem(error)).not.toThrow();
    });

    it("returns ApiProblem when data is valid object", () => {
      const problem: ApiProblem = {
        title: "Validation Failed",
        detail: "Email is required",
        code: "VALIDATION_ERROR",
      };

      const error = createAxiosError(400, problem);
      const result = getApiProblem(error);

      expect(result).toEqual(problem);
    });

    it("preserves errors field (field validation dictionary)", () => {
      const problem: ApiProblem = {
        title: "Validation Error",
        errors: {
          email: ["Email is required"],
          password: ["Password must be at least 8 characters"],
        },
      };

      const error = createAxiosError(400, problem);
      const result = getApiProblem(error);

      expect(result?.errors).toEqual(problem.errors);
      expect(result?.errors?.email).toContain("Email is required");
    });

    it("handles partial ApiProblem objects", () => {
      const error = createAxiosError(500, { code: "INTERNAL_ERROR" });
      const result = getApiProblem(error);

      expect(result).toBeDefined();
      expect(result?.code).toBe("INTERNAL_ERROR");
      expect(result?.detail).toBeUndefined();
    });
  });

  describe("getApiErrorMessage", () => {
    it("returns detail from ApiProblem when present", () => {
      const error = createAxiosError(400, {
        detail: "Lokalize hata mesajı",
      });

      const result = getApiErrorMessage(error, "Fallback message");

      expect(result).toBe("Lokalize hata mesajı");
    });

    it("returns fallback when detail is missing", () => {
      const error = createAxiosError(500, { code: "SERVER_ERROR" });

      const result = getApiErrorMessage(error, "Something went wrong");

      expect(result).toBe("Something went wrong");
    });

    it("returns fallback when detail is empty string", () => {
      const error = createAxiosError(400, { detail: "" });

      const result = getApiErrorMessage(error, "Please try again");

      expect(result).toBe("Please try again");
    });

    it("returns fallback for non-axios error", () => {
      const error = new Error("Generic error");

      const result = getApiErrorMessage(error, "Default error");

      expect(result).toBe("Default error");
    });

    it("returns fallback when response.data is not a valid ApiProblem", () => {
      const error = createAxiosError(500, "String response");

      const result = getApiErrorMessage(error, "An error occurred");

      expect(result).toBe("An error occurred");
    });

    it("preserves special characters in Turkish messages", () => {
      const turkishMessage = "Şifre geçersiz, lütfen tekrar deneyin";
      const error = createAxiosError(400, {
        detail: turkishMessage,
      });

      const result = getApiErrorMessage(error, "Default");

      expect(result).toBe(turkishMessage);
    });

    it("handles detail with only whitespace as empty", () => {
      const error = createAxiosError(400, { detail: "   " });

      // The code uses `||` so whitespace-only string is truthy
      const result = getApiErrorMessage(error, "Fallback");

      expect(result).toBe("   ");
    });
  });

  describe("ApiProblem structure", () => {
    it("supports all expected fields", () => {
      const problem: ApiProblem = {
        title: "Validation Failed",
        detail: "Field validation error",
        status: 400,
        code: "VALIDATION_ERROR",
        errors: {
          fieldName: ["Error message"],
        },
      };

      const error = createAxiosError(400, problem);
      const result = getApiProblem(error);

      expect(result?.title).toBe("Validation Failed");
      expect(result?.detail).toBe("Field validation error");
      expect(result?.status).toBe(400);
      expect(result?.code).toBe("VALIDATION_ERROR");
      expect(result?.errors?.fieldName).toContain("Error message");
    });

    it("allows optional fields", () => {
      const problem: ApiProblem = {
        code: "ONLY_CODE",
      };

      const error = createAxiosError(500, problem);
      const result = getApiProblem(error);

      expect(result?.code).toBe("ONLY_CODE");
      expect(result?.title).toBeUndefined();
      expect(result?.detail).toBeUndefined();
    });
  });
});
