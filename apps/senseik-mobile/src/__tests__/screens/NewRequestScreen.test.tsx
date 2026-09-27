import React from "react";
import { act, create, type ReactTestInstance, type ReactTestRenderer } from "react-test-renderer";
import { Text } from "react-native";
import NewRequestScreen from "@/screens/app/NewRequestScreen";
import { LeaveRequestForm } from "@/components/leave/LeaveRequestForm";
import { leaveService } from "@/services/LeaveService";
import type { RequestKind } from "@/types/navigation";

// Ionicons checks expo-font's isLoaded, which the shared expo-font mock (setup.ts) lacks.
jest.mock("@expo/vector-icons", () => ({ Ionicons: () => null }));
jest.mock("react-native-safe-area-context", () => ({
  useSafeAreaInsets: () => ({ top: 0, right: 0, bottom: 0, left: 0 }),
}));
jest.mock("@/services/LeaveService", () => ({
  leaveService: { getMyRequestableLeaveTypes: jest.fn() },
}));
jest.mock("@/components/leave/LeaveRequestForm", () => ({ LeaveRequestForm: jest.fn(() => null) }));

function mockFormStub(label: string) {
  const { Text: RNText } = jest.requireActual("react-native");
  const ReactActual = jest.requireActual("react");
  return { __esModule: true, default: () => ReactActual.createElement(RNText, null, label) };
}
jest.mock("@/components/requests/OvertimeRequestSection", () => mockFormStub("overtime-form"));
jest.mock("@/components/requests/ExpenseClaimSection", () => mockFormStub("expense-form"));
jest.mock("@/components/requests/AdvanceRequestSection", () => mockFormStub("advance-form"));

const leaveTypes = [{ id: "annual-id", name: "Yıllık izin" }];

function extractText(node: unknown): string {
  if (typeof node === "string") return node;
  if (typeof node === "number") return String(node);
  if (Array.isArray(node)) return node.map(extractText).join("");
  if (node && typeof node === "object" && "children" in (node as { children?: unknown })) {
    return extractText((node as { children?: unknown }).children);
  }
  return "";
}

function texts(renderer: ReactTestRenderer): string[] {
  return renderer.root.findAllByType(Text).map((node) => extractText(node.children));
}

async function renderScreen(kind: RequestKind) {
  const goBack = jest.fn();
  const props = {
    navigation: { goBack },
    route: { key: "NewRequest-key", name: "NewRequest", params: { kind } },
  } as unknown as React.ComponentProps<typeof NewRequestScreen>;
  let renderer!: ReactTestRenderer;
  await act(async () => {
    renderer = create(<NewRequestScreen {...props} />);
  });
  return { renderer, goBack };
}

function findButton(renderer: ReactTestRenderer, label: string): ReactTestInstance {
  return renderer.root.findAll(
    (node) => node.props.accessibilityLabel === label && typeof node.props.onPress === "function"
  )[0];
}

describe("NewRequestScreen", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it.each([
    ["overtime", "Fazla mesai talebi", "overtime-form"],
    ["expense", "Masraf talebi", "expense-form"],
    ["advance", "Avans talebi", "advance-form"],
  ] as const)("shows the %s title and its existing form", async (kind, title, form) => {
    const { renderer } = await renderScreen(kind);

    expect(texts(renderer)).toEqual(expect.arrayContaining([title, form]));
  });

  it("loads the requestable leave types, hands them to LeaveRequestForm and closes on success", async () => {
    (leaveService.getMyRequestableLeaveTypes as jest.Mock).mockResolvedValue(leaveTypes);
    const { renderer, goBack } = await renderScreen("leave");

    expect(texts(renderer)).toContain("İzin talebi");
    const formProps = (LeaveRequestForm as jest.Mock).mock.lastCall[0];
    expect(formProps.requestableTypes).toEqual(leaveTypes);

    act(() => formProps.onSubmitted());

    expect(goBack).toHaveBeenCalledTimes(1);
  });

  it("shows an error instead of the leave form when the leave types fail to load", async () => {
    (leaveService.getMyRequestableLeaveTypes as jest.Mock).mockRejectedValue(new Error("offline"));
    const { renderer } = await renderScreen("leave");

    expect(texts(renderer)).toContain("İzin türleri yüklenemedi. Lütfen tekrar deneyin.");
    expect(LeaveRequestForm).not.toHaveBeenCalled();
  });

  it("closes from the header's close button", async () => {
    const { renderer, goBack } = await renderScreen("advance");

    act(() => findButton(renderer, "Kapat").props.onPress());

    expect(goBack).toHaveBeenCalledTimes(1);
  });
});
