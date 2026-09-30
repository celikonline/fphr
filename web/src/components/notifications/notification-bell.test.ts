import { describe, expect, it } from "vitest";
import { isSafeAppPath, resolveNotificationActionUrl } from "./notification-bell";

describe("isSafeAppPath", () => {
  it("allows same-origin app paths", () => {
    expect(isSafeAppPath("/app/inbox")).toBe(true);
    expect(isSafeAppPath("/login")).toBe(true);
  });

  it("rejects protocol-relative, absolute and non-http(s) URLs", () => {
    expect(isSafeAppPath("//evil.example.com")).toBe(false);
    expect(isSafeAppPath("https://evil.example.com")).toBe(false);
    expect(isSafeAppPath("javascript:alert(1)")).toBe(false);
    expect(isSafeAppPath("data:text/html,<script>alert(1)</script>")).toBe(false);
  });
});

describe("resolveNotificationActionUrl (P06/FG-009)", () => {
  it("redirects a mobile-only request detail path to the workflow inbox", () => {
    expect(resolveNotificationActionUrl("/app/requests/3fa85f64-5717-4562-b3fc-2c963f66afa6")).toBe(
      "/app/inbox"
    );
  });

  it("leaves the existing /app/requests and /app/requests/visa web pages untouched", () => {
    expect(resolveNotificationActionUrl("/app/requests")).toBe("/app/requests");
    expect(resolveNotificationActionUrl("/app/requests/visa")).toBe("/app/requests/visa");
  });

  it("leaves every other safe app path unchanged", () => {
    expect(resolveNotificationActionUrl("/app/inbox")).toBe("/app/inbox");
    expect(resolveNotificationActionUrl("/app/onboarding")).toBe("/app/onboarding");
  });
});
