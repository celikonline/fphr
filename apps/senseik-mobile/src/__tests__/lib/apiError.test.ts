import axios from "axios";
import { getApiErrorMessage, getApiProblem } from "@/lib/apiError";

describe("getApiProblem", () => {
  it("returns undefined for a non-axios error", () => {
    expect(getApiProblem(new Error("boom"))).toBeUndefined();
  });

  it("returns the ProblemDetails body of an axios error", () => {
    const error = {
      isAxiosError: true,
      response: { data: { detail: "Yetersiz bakiye", code: "leave.errors.invalid_range" } },
    };
    jest.spyOn(axios, "isAxiosError").mockReturnValue(true);

    expect(getApiProblem(error)).toEqual({
      detail: "Yetersiz bakiye",
      code: "leave.errors.invalid_range",
    });

    jest.restoreAllMocks();
  });
});

describe("getApiErrorMessage", () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it("returns the server's localized detail when present (leave.errors.* is already Turkish via Accept-Language: tr)", () => {
    jest.spyOn(axios, "isAxiosError").mockReturnValue(true);
    const error = {
      isAxiosError: true,
      response: { data: { detail: "Bu tarih aralığında zaten onaylı bir izniniz var." } },
    };

    expect(getApiErrorMessage(error, "Genel hata")).toBe(
      "Bu tarih aralığında zaten onaylı bir izniniz var."
    );
  });

  it("falls back to the given message when the server sends no detail", () => {
    jest.spyOn(axios, "isAxiosError").mockReturnValue(true);
    const error = { isAxiosError: true, response: { data: {} } };

    expect(getApiErrorMessage(error, "İzin talebi gönderilemedi.")).toBe(
      "İzin talebi gönderilemedi."
    );
  });

  it("falls back to the given message for a network error with no response", () => {
    jest.spyOn(axios, "isAxiosError").mockReturnValue(true);
    const error = { isAxiosError: true };

    expect(getApiErrorMessage(error, "İzin talebi gönderilemedi.")).toBe(
      "İzin talebi gönderilemedi."
    );
  });
});
