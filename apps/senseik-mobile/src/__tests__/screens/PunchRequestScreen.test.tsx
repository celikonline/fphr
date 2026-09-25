import React from "react";
import { Alert } from "react-native";
import { act, create, type ReactTestRenderer } from "react-test-renderer";
import PunchRequestScreen from "@/screens/app/PunchRequestScreen";
import {
  canCancelPunchRequest,
  isFutureTime,
  parseLocalDateTime,
  punchRequestService,
  type PunchRequest,
} from "@/services/PunchRequestService";

jest.mock("@/services/PunchRequestService", () => {
  const actual = jest.requireActual("@/services/PunchRequestService");
  return {
    ...actual,
    punchRequestService: { create: jest.fn(), listMine: jest.fn(), cancel: jest.fn() },
  };
});

const service = punchRequestService as jest.Mocked<typeof punchRequestService>;

function request(id: string, status: PunchRequest["status"]): PunchRequest {
  return {
    id,
    employeeId: "e1",
    occurredAtUtc: "2026-09-20T05:30:00Z",
    direction: "in",
    reason: "sebep",
    status,
  };
}

async function renderScreen(): Promise<ReactTestRenderer> {
  let renderer!: ReactTestRenderer;
  await act(async () => {
    renderer = create(<PunchRequestScreen />);
  });
  return renderer;
}

function setText(renderer: ReactTestRenderer, testID: string, value: string) {
  act(() => {
    renderer.root.findByProps({ testID }).props.onChangeText(value);
  });
}

async function press(renderer: ReactTestRenderer, testID: string) {
  await act(async () => {
    renderer.root.findByProps({ testID }).props.onPress();
  });
}

function flat(node: unknown): string {
  if (typeof node === "string") return node;
  if (Array.isArray(node)) return node.map(flat).join("");
  if (node && typeof node === "object" && "children" in (node as object)) {
    return flat((node as { children: unknown }).children);
  }
  return "";
}

beforeEach(() => {
  jest.clearAllMocks();
  jest.spyOn(Alert, "alert").mockImplementation(() => undefined);
  service.listMine.mockResolvedValue([]);
  service.create.mockResolvedValue("new");
  service.cancel.mockResolvedValue();
});

describe("PunchRequest helpers", () => {
  it("parses local date/time and rejects invalid values", () => {
    expect(parseLocalDateTime("2026-09-20", "08:30")?.getHours()).toBe(8);
    expect(parseLocalDateTime("2026-02-31", "08:30")).toBeNull();
    expect(parseLocalDateTime("2026-09-20", "25:00")).toBeNull();
    expect(parseLocalDateTime("abc", "08:30")).toBeNull();
  });

  it("future_time_blocked: flags instants after now", () => {
    const now = new Date(2026, 8, 25, 12, 0);
    expect(isFutureTime(new Date(2026, 8, 25, 12, 1), now)).toBe(true);
    expect(isFutureTime(new Date(2026, 8, 25, 11, 59), now)).toBe(false);
  });

  it("cancel_only_pending: only pending is cancellable", () => {
    expect(canCancelPunchRequest("pending")).toBe(true);
    expect(["approved", "rejected", "cancelled"].some((s) => canCancelPunchRequest(s as never))).toBe(false);
  });
});

describe("PunchRequestScreen", () => {
  it("future_time_blocked: does not call the service for a future time", async () => {
    const renderer = await renderScreen();
    const tomorrow = new Date(Date.now() + 86_400_000);
    const pad = (n: number) => String(n).padStart(2, "0");
    setText(renderer, "punch-date", `${tomorrow.getFullYear()}-${pad(tomorrow.getMonth() + 1)}-${pad(tomorrow.getDate())}`);
    setText(renderer, "punch-time", "09:00");
    setText(renderer, "punch-reason", "unuttum");
    await press(renderer, "punch-submit");
    expect(service.create).not.toHaveBeenCalled();
    expect(Alert.alert).toHaveBeenCalledWith("Geçersiz zaman", "Gelecek bir zaman için talep oluşturulamaz.");
  });

  it("submit_calls_service: sends UTC instant, direction and reason", async () => {
    const renderer = await renderScreen();
    setText(renderer, "punch-date", "2026-09-20");
    setText(renderer, "punch-time", "08:30");
    setText(renderer, "punch-reason", " unuttum ");
    await press(renderer, "punch-direction-out");
    await press(renderer, "punch-submit");
    expect(service.create).toHaveBeenCalledWith({
      occurredAtUtc: new Date(2026, 8, 20, 8, 30).toISOString(),
      direction: "out",
      reason: "unuttum",
    });
  });

  it("status_chip_updates: chip reflects status after refetch", async () => {
    service.listMine.mockResolvedValueOnce([request("p1", "pending")]);
    const renderer = await renderScreen();
    expect(flat(renderer.root.findByProps({ testID: "punch-status-p1" }))).toBe("Bekliyor");
    service.listMine.mockResolvedValueOnce([request("p1", "cancelled")]);
    await press(renderer, "punch-cancel-p1");
    expect(service.cancel).toHaveBeenCalledWith("p1");
    expect(flat(renderer.root.findByProps({ testID: "punch-status-p1" }))).toBe("İptal edildi");
  });

  it("cancel_only_pending: renders cancel only for pending rows", async () => {
    service.listMine.mockResolvedValueOnce([request("p1", "pending"), request("a1", "approved"), request("r1", "rejected")]);
    const renderer = await renderScreen();
    expect(renderer.root.findAllByProps({ testID: "punch-cancel-p1" }).length).toBeGreaterThan(0);
    expect(renderer.root.findAllByProps({ testID: "punch-cancel-a1" })).toHaveLength(0);
    expect(renderer.root.findAllByProps({ testID: "punch-cancel-r1" })).toHaveLength(0);
  });
});
