import React from "react";
import { act, create, type ReactTestInstance, type ReactTestRenderer } from "react-test-renderer";
import { ActivityIndicator, Alert, Text, TouchableOpacity } from "react-native";
import * as WebBrowser from "expo-web-browser";
import { LeaveRequestListItem } from "@/components/leave/LeaveRequestListItem";
import { leaveService, type LeaveRequest } from "@/services/LeaveService";

jest.mock("@/services/LeaveService");
// Ionicons checks expo-font's isLoaded, which the shared expo-font mock (setup.ts) lacks.
jest.mock("@expo/vector-icons", () => ({ Ionicons: () => null }));
jest.mock("expo-web-browser", () => ({ openBrowserAsync: jest.fn() }));

function buildRequest(overrides: Partial<LeaveRequest> = {}): LeaveRequest {
  return {
    id: "request-1",
    employeeId: "employee-1",
    leaveTypeId: "type-1",
    leaveTypeCode: "annual",
    startDate: "2026-10-01",
    endDate: "2026-10-03",
    calculatedDays: 3,
    status: "pending",
    submittedAt: "2026-09-20T10:00:00Z",
    leaveTypeName: "Yıllık izin",
    ...overrides,
  };
}

function renderComponent(element: React.ReactElement): ReactTestRenderer {
  let renderer!: ReactTestRenderer;
  act(() => {
    renderer = create(element);
  });
  return renderer;
}

/**
 * The Text host component's `.children` can hold nested test-instance/element
 * wrappers rather than raw strings (depends on the RN test-renderer mock), so a
 * plain `.join("")` on them yields `[object Object]`. Flatten recursively instead.
 */
function extractText(node: unknown): string {
  if (typeof node === "string") return node;
  if (typeof node === "number") return String(node);
  if (Array.isArray(node)) return node.map(extractText).join("");
  if (node && typeof node === "object" && "children" in (node as { children?: unknown })) {
    return extractText((node as { children?: unknown }).children);
  }
  return "";
}

describe("LeaveRequestListItem", () => {
  it("shows the leave type, date range and status label", () => {
    const renderer = renderComponent(<LeaveRequestListItem request={buildRequest()} />);

    const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));

    expect(texts).toContain("Yıllık izin");
    expect(texts).toContain("Onay bekliyor");
    expect(texts.some((text) => text.includes("3 gün"))).toBe(true);
  });

  it("formats a decimal day count with the Turkish decimal comma (B2)", () => {
    const renderer = renderComponent(
      <LeaveRequestListItem request={buildRequest({ calculatedDays: 2.5 })} />
    );

    const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));

    expect(texts.some((text) => text.includes("2,5 gün"))).toBe(true);
  });

  it("falls back to the leave type code when no display name is present", () => {
    const renderer = renderComponent(
      <LeaveRequestListItem request={buildRequest({ leaveTypeName: null })} />
    );

    const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));

    expect(texts).toContain("annual");
  });

  it("is not pressable when no onPress handler is given", () => {
    const renderer = renderComponent(<LeaveRequestListItem request={buildRequest()} />);

    expect(renderer.root.findAllByType(TouchableOpacity)).toHaveLength(0);
  });

  it("calls onPress when the row is tapped", () => {
    const onPress = jest.fn();
    const renderer = renderComponent(
      <LeaveRequestListItem request={buildRequest()} onPress={onPress} />
    );

    act(() => {
      renderer.root.findByType(TouchableOpacity).props.onPress();
    });

    expect(onPress).toHaveBeenCalledTimes(1);
  });

  describe("Geri çek (H1: withdraw a pending request)", () => {
    it("shows a Geri çek button for a pending request when onWithdraw is given", () => {
      const onWithdraw = jest.fn();
      const renderer = renderComponent(
        <LeaveRequestListItem
          request={buildRequest({ status: "pending" })}
          onWithdraw={onWithdraw}
        />
      );

      const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));
      expect(texts).toContain("Geri çek");

      act(() => {
        renderer.root.findByType(TouchableOpacity).props.onPress();
      });
      expect(onWithdraw).toHaveBeenCalledTimes(1);
    });

    it("does not show a Geri çek button when onWithdraw is not given (e.g. ProfileScreen's summary usage)", () => {
      const renderer = renderComponent(
        <LeaveRequestListItem request={buildRequest({ status: "pending" })} />
      );

      const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));
      expect(texts).not.toContain("Geri çek");
    });

    it("does not show a Geri çek button for a non-pending request even when onWithdraw is given", () => {
      const renderer = renderComponent(
        <LeaveRequestListItem
          request={buildRequest({ status: "approved" })}
          onWithdraw={jest.fn()}
        />
      );

      const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));
      expect(texts).not.toContain("Geri çek");
    });

    it("shows a loading indicator instead of the label while withdrawing, and disables the button", () => {
      const renderer = renderComponent(
        <LeaveRequestListItem
          request={buildRequest({ status: "pending" })}
          onWithdraw={jest.fn()}
          isWithdrawing
        />
      );

      const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));
      expect(texts).not.toContain("Geri çek");
      expect(renderer.root.findAllByType(ActivityIndicator)).toHaveLength(1);
      expect(renderer.root.findByType(TouchableOpacity).props.disabled).toBe(true);
    });
  });

  describe("Ekler (senseik-mobile-011: lazy-loaded attachments)", () => {
    /** A `TouchableOpacity` test instance's own text is whatever its nested `Text` node(s) render -
     * not `.props.children` (a React element, not a string) and not `.children` (the rendered host
     * tree, which for a composite component like `TouchableOpacity` isn't just its JSX prop). */
    function touchableText(node: ReactTestInstance) {
      return node
        .findAllByType(Text)
        .map((textNode) => extractText(textNode.children))
        .join("");
    }

    function findByText(renderer: ReactTestRenderer, ...labels: string[]) {
      return renderer.root
        .findAllByType(TouchableOpacity)
        .find((node) => labels.includes(touchableText(node)));
    }

    function findToggle(renderer: ReactTestRenderer) {
      return findByText(renderer, "Ekler", "Ekleri gizle");
    }

    beforeEach(() => {
      jest.clearAllMocks();
      (WebBrowser.openBrowserAsync as jest.Mock).mockResolvedValue({ type: "opened" });
      jest.spyOn(Alert, "alert").mockImplementation(() => {});
    });

    afterEach(() => {
      jest.restoreAllMocks();
    });

    it("does not show the Ekler toggle when showAttachments is not given (e.g. ProfileScreen's summary usage)", () => {
      const renderer = renderComponent(<LeaveRequestListItem request={buildRequest()} />);
      expect(findToggle(renderer)).toBeUndefined();
    });

    it("does not fetch the request detail until the card is expanded - no N-requests-per-list", () => {
      renderComponent(<LeaveRequestListItem request={buildRequest()} showAttachments />);
      expect(leaveService.getMyLeaveRequest).not.toHaveBeenCalled();
    });

    it("fetches attachments on first expand and renders each name as its own tappable row", async () => {
      (leaveService.getMyLeaveRequest as jest.Mock).mockResolvedValue({
        attachments: [
          { documentId: "doc-1", fileName: "rapor.pdf", attachedAt: "2026-09-27T01:55:00Z" },
        ],
      });
      const renderer = renderComponent(
        <LeaveRequestListItem request={buildRequest()} showAttachments />
      );

      await act(async () => {
        findToggle(renderer)?.props.onPress();
      });

      expect(leaveService.getMyLeaveRequest).toHaveBeenCalledWith("request-1");
      const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));
      expect(texts).toContain("rapor.pdf");
    });

    it("shows 'Ek yok.' when the request has no attachments", async () => {
      (leaveService.getMyLeaveRequest as jest.Mock).mockResolvedValue({ attachments: [] });
      const renderer = renderComponent(
        <LeaveRequestListItem request={buildRequest()} showAttachments />
      );

      await act(async () => {
        findToggle(renderer)?.props.onPress();
      });

      const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));
      expect(texts).toContain("Ek yok.");
    });

    it("shows an error message when loading attachments fails, without throwing", async () => {
      (leaveService.getMyLeaveRequest as jest.Mock).mockRejectedValue({
        response: { status: 404 },
      });
      const renderer = renderComponent(
        <LeaveRequestListItem request={buildRequest()} showAttachments />
      );

      await act(async () => {
        findToggle(renderer)?.props.onPress();
      });

      const texts = renderer.root.findAllByType(Text).map((node) => extractText(node.children));
      expect(texts).toContain("Ekler yüklenemedi.");
    });

    it("does not refetch on a second expand (collapse/re-expand) once already loaded", async () => {
      (leaveService.getMyLeaveRequest as jest.Mock).mockResolvedValue({
        attachments: [
          { documentId: "doc-1", fileName: "rapor.pdf", attachedAt: "2026-09-27T01:55:00Z" },
        ],
      });
      const renderer = renderComponent(
        <LeaveRequestListItem request={buildRequest()} showAttachments />
      );

      await act(async () => {
        findToggle(renderer)?.props.onPress(); // expand -> fetches
      });
      await act(async () => {
        findToggle(renderer)?.props.onPress(); // collapse
      });
      await act(async () => {
        findToggle(renderer)?.props.onPress(); // re-expand -> cached, no refetch
      });

      expect(leaveService.getMyLeaveRequest).toHaveBeenCalledTimes(1);
    });

    it("tapping an attachment name fetches a fresh download URL and opens it", async () => {
      (leaveService.getMyLeaveRequest as jest.Mock).mockResolvedValue({
        attachments: [
          { documentId: "doc-1", fileName: "rapor.pdf", attachedAt: "2026-09-27T01:55:00Z" },
        ],
      });
      (leaveService.getMyAttachmentDownloadUrl as jest.Mock).mockResolvedValue({
        url: "https://storage.example/rapor.pdf?sig=1",
      });
      const renderer = renderComponent(
        <LeaveRequestListItem request={buildRequest()} showAttachments />
      );

      await act(async () => {
        findToggle(renderer)?.props.onPress();
      });
      const attachmentRow = findByText(renderer, "rapor.pdf");
      await act(async () => {
        await attachmentRow?.props.onPress();
      });

      expect(leaveService.getMyAttachmentDownloadUrl).toHaveBeenCalledWith("request-1", "doc-1");
      expect(WebBrowser.openBrowserAsync).toHaveBeenCalledWith(
        "https://storage.example/rapor.pdf?sig=1"
      );
    });

    it("shows the generic error alert when opening an attachment fails (403/404/network)", async () => {
      (leaveService.getMyLeaveRequest as jest.Mock).mockResolvedValue({
        attachments: [
          { documentId: "doc-1", fileName: "rapor.pdf", attachedAt: "2026-09-27T01:55:00Z" },
        ],
      });
      (leaveService.getMyAttachmentDownloadUrl as jest.Mock).mockRejectedValue({
        response: { status: 403 },
      });
      const renderer = renderComponent(
        <LeaveRequestListItem request={buildRequest()} showAttachments />
      );

      await act(async () => {
        findToggle(renderer)?.props.onPress();
      });
      const attachmentRow = findByText(renderer, "rapor.pdf");
      await act(async () => {
        await attachmentRow?.props.onPress();
      });

      expect(Alert.alert).toHaveBeenCalledWith("Hata", "Belge açılamadı. Lütfen tekrar deneyin.");
    });

    it("double-tap guard: a second tap while the first download is in flight does not fetch again", async () => {
      let resolveDownloadUrl: (value: { url: string }) => void = () => {};
      (leaveService.getMyLeaveRequest as jest.Mock).mockResolvedValue({
        attachments: [
          { documentId: "doc-1", fileName: "rapor.pdf", attachedAt: "2026-09-27T01:55:00Z" },
        ],
      });
      (leaveService.getMyAttachmentDownloadUrl as jest.Mock).mockReturnValue(
        new Promise((resolve) => (resolveDownloadUrl = resolve))
      );
      const renderer = renderComponent(
        <LeaveRequestListItem request={buildRequest()} showAttachments />
      );

      await act(async () => {
        findToggle(renderer)?.props.onPress();
      });
      // Captured once - the same TouchableOpacity's `.props` reflect its latest render even after
      // its child switches from the filename `Text` to a loading `ActivityIndicator`, so re-querying
      // by visible text (which disappears once mid-flight) would wrongly find nothing.
      const attachmentRow = findByText(renderer, "rapor.pdf");

      act(() => {
        void attachmentRow?.props.onPress();
      });
      // The row is now disabled (mid-flight) - a second tap must not fire another request.
      expect(attachmentRow?.props.disabled).toBe(true);
      act(() => {
        void attachmentRow?.props.onPress();
      });

      await act(async () => {
        resolveDownloadUrl({ url: "https://storage.example/rapor.pdf?sig=1" });
      });

      expect(leaveService.getMyAttachmentDownloadUrl).toHaveBeenCalledTimes(1);
    });
  });
});
