import * as WebBrowser from "expo-web-browser";
import {
  ATTACHMENT_OPEN_ERROR_MESSAGE,
  openLeaveAttachment,
  shouldIgnoreAttachmentTap,
} from "@/lib/attachmentDownload";

jest.mock("expo-web-browser", () => ({
  openBrowserAsync: jest.fn(),
}));

describe("openLeaveAttachment (senseik-mobile-011/U1)", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    (WebBrowser.openBrowserAsync as jest.Mock).mockResolvedValue({ type: "opened" });
  });

  it("fetches a fresh download URL and opens it in-app (Custom Tabs/SFSafariViewController)", async () => {
    const fetchDownloadUrl = jest
      .fn()
      .mockResolvedValue({ url: "https://storage.example/a.pdf?sig=1" });

    await openLeaveAttachment(fetchDownloadUrl);

    expect(fetchDownloadUrl).toHaveBeenCalledTimes(1);
    expect(WebBrowser.openBrowserAsync).toHaveBeenCalledWith("https://storage.example/a.pdf?sig=1");
  });

  it("calls fetchDownloadUrl again on every invocation - never caches the URL", async () => {
    const fetchDownloadUrl = jest
      .fn()
      .mockResolvedValueOnce({ url: "https://storage.example/a.pdf?sig=1" })
      .mockResolvedValueOnce({ url: "https://storage.example/a.pdf?sig=2" });

    await openLeaveAttachment(fetchDownloadUrl);
    await openLeaveAttachment(fetchDownloadUrl);

    expect(fetchDownloadUrl).toHaveBeenCalledTimes(2);
    expect(WebBrowser.openBrowserAsync).toHaveBeenNthCalledWith(
      1,
      "https://storage.example/a.pdf?sig=1"
    );
    expect(WebBrowser.openBrowserAsync).toHaveBeenNthCalledWith(
      2,
      "https://storage.example/a.pdf?sig=2"
    );
  });

  it("propagates a download-url failure (403/404) so the caller can show the generic error", async () => {
    const apiError = {
      response: { status: 403, data: { error: { code: "leave.employee.not_in_scope" } } },
    };
    const fetchDownloadUrl = jest.fn().mockRejectedValue(apiError);

    await expect(openLeaveAttachment(fetchDownloadUrl)).rejects.toEqual(apiError);
    expect(WebBrowser.openBrowserAsync).not.toHaveBeenCalled();
  });

  it("propagates a network error the same way as an API error", async () => {
    const networkError = new Error("Network Error");
    const fetchDownloadUrl = jest.fn().mockRejectedValue(networkError);

    await expect(openLeaveAttachment(fetchDownloadUrl)).rejects.toThrow("Network Error");
  });

  it("propagates a failure from opening the browser itself", async () => {
    const fetchDownloadUrl = jest.fn().mockResolvedValue({ url: "https://storage.example/a.pdf" });
    (WebBrowser.openBrowserAsync as jest.Mock).mockRejectedValue(new Error("no browser available"));

    await expect(openLeaveAttachment(fetchDownloadUrl)).rejects.toThrow("no browser available");
  });
});

describe("ATTACHMENT_OPEN_ERROR_MESSAGE (error mapping: 403/404/network all show the same generic message)", () => {
  it("is a single generic Turkish message", () => {
    expect(ATTACHMENT_OPEN_ERROR_MESSAGE).toBe("Belge açılamadı. Lütfen tekrar deneyin.");
  });
});

describe("shouldIgnoreAttachmentTap (double-tap guard)", () => {
  it("ignores a tap while another attachment is opening", () => {
    expect(shouldIgnoreAttachmentTap("doc-1")).toBe(true);
  });

  it("allows a tap when nothing is currently opening", () => {
    expect(shouldIgnoreAttachmentTap(null)).toBe(false);
  });
});
