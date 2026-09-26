import { apiClient } from "@/lib/apiClient";

/**
 * `apiClient` is normally imported through `jest.mock("@/lib/apiClient")` by the service tests, so
 * this is the one place the real axios instance's defaults get exercised. Reaches into the private
 * `instance` field on purpose (there is no public getter, same as `getBaseURL()` does internally) -
 * this is a regression guard for D4 (leave/error responses must come back localized in Turkish).
 */
describe("apiClient default headers", () => {
  it("sends Accept-Language: tr on every request (the app has no i18n and is Turkish-only)", () => {
    const instance = (
      apiClient as unknown as { instance: { defaults: { headers: Record<string, unknown> } } }
    ).instance;

    expect(instance.defaults.headers["Accept-Language"]).toBe("tr");
  });
});
