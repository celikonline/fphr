import React from "react";
import { Text } from "react-native";
import { act, create, type ReactTestInstance, type ReactTestRenderer } from "react-test-renderer";
import { AxiosError, AxiosHeaders } from "axios";
import CheckInScreen from "@/screens/app/CheckInScreen";
import { attendanceService, type CheckInLocation } from "@/services/AttendanceService";
import { biometricService } from "@/lib/biometric";

// Ionicons checks expo-font's isLoaded, which the shared expo-font mock (setup.ts) lacks.
jest.mock("@expo/vector-icons", () => ({ Ionicons: () => null }));
jest.mock("react-native-safe-area-context", () => ({
  useSafeAreaInsets: () => ({ top: 0, right: 0, bottom: 0, left: 0 }),
}));
jest.mock("@react-navigation/native", () => {
  const { useEffect } = jest.requireActual("react");
  return {
    useIsFocused: () => true,
    useFocusEffect: (effect: () => void) => useEffect(effect, [effect]),
  };
});
jest.mock("expo-camera", () => {
  const { View } = jest.requireActual("react-native");
  return { CameraView: View, useCameraPermissions: () => [{ granted: true }, jest.fn()] };
});
jest.mock("@/screens/app/checkin/useDeviceLocation", () => ({
  useDeviceLocation: () => ({
    status: "ready",
    // ~111 m north of the office below.
    coords: { latitude: 39.921, longitude: 32.85 },
    accuracy: 12,
    retry: jest.fn(),
    recheck: jest.fn(),
  }),
}));
jest.mock("@/services/AttendanceService", () => ({
  attendanceService: {
    getMyCheckInStatus: jest.fn(),
    getCheckInLocations: jest.fn(),
    gpsCheckIn: jest.fn(),
    qrCheckIn: jest.fn(),
  },
}));
jest.mock("@/lib/biometric", () => ({
  biometricService: { confirmAction: jest.fn() },
}));

const service = jest.mocked(attendanceService);
const confirmAction = jest.mocked(biometricService.confirmAction);

const office: CheckInLocation = {
  locationId: "loc-office",
  name: "Merkez Ofis",
  latitude: 39.92,
  longitude: 32.85,
  radiusMeters: 150,
};
const position = { latitude: 39.921, longitude: 32.85 };

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

function button(renderer: ReactTestRenderer, label: string): ReactTestInstance {
  return renderer.root.findAll(
    (node) => node.props.accessibilityLabel === label && typeof node.props.onPress === "function"
  )[0];
}

/** Rendered screens, unmounted after each test so ClockCard's 1 s tick never outlives it. */
const mounted: ReactTestRenderer[] = [];

async function mount(): Promise<ReactTestRenderer> {
  let renderer!: ReactTestRenderer;
  await act(async () => {
    renderer = create(<CheckInScreen />);
  });
  mounted.push(renderer);
  return renderer;
}

async function renderScreen(locations: CheckInLocation[]): Promise<ReactTestRenderer> {
  service.getMyCheckInStatus.mockResolvedValue({ isCheckedIn: false, date: "2026-09-28" });
  service.getCheckInLocations.mockResolvedValue(locations);
  return mount();
}

async function pressCheckIn(renderer: ReactTestRenderer) {
  await act(async () => {
    button(renderer, "Giriş yap").props.onPress();
  });
}

const punchResult = {
  attendanceDayId: "day-1",
  date: "2026-09-28",
  direction: "in" as const,
  time: "09:00:00",
};

describe("CheckInScreen (FG-005)", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    confirmAction.mockResolvedValue(true);
    service.gpsCheckIn.mockResolvedValue(punchResult);
  });

  afterEach(() => {
    act(() => {
      mounted.splice(0).forEach((renderer) => renderer.unmount());
    });
  });

  it("shows the office, its distance and 'Ofis alanında', and draws the geofence circle", async () => {
    const renderer = await renderScreen([office]);

    const all = texts(renderer);
    expect(all).toContain("Ofis alanında");
    expect(all).toContain("Merkez Ofis · 111 m");
    const circle = renderer.root.findAll((node) => node.props.testID === "geofence-circle")[0];
    expect(circle.props.center).toEqual({ latitude: 39.92, longitude: 32.85 });
    expect(circle.props.radius).toBe(150);
  });

  it("shows 'Alan dışında' when the device is outside the radius", async () => {
    const renderer = await renderScreen([{ ...office, radiusMeters: 50 }]);
    expect(texts(renderer)).toContain("Alan dışında");
  });

  it("sends the geofenced location's id with the punch", async () => {
    const renderer = await renderScreen([office]);
    await pressCheckIn(renderer);

    expect(confirmAction).toHaveBeenCalledWith("Giriş/çıkış kaydı için kimliğinizi doğrulayın");
    expect(service.gpsCheckIn).toHaveBeenCalledWith({
      ...position,
      locationId: "loc-office",
      direction: "in",
    });
  });

  it("sends no locationId for a roaming employee and shows no geofence", async () => {
    const renderer = await renderScreen([]);
    await pressCheckIn(renderer);

    expect(service.gpsCheckIn).toHaveBeenCalledWith({ ...position, direction: "in" });
    const all = texts(renderer);
    expect(all).not.toContain("Ofis alanında");
    expect(all).not.toContain("Alan dışında");
    expect(renderer.root.findAll((node) => node.props.testID === "geofence-circle")).toHaveLength(
      0
    );
  });

  it("sends no locationId for a location without a radius", async () => {
    const renderer = await renderScreen([{ ...office, radiusMeters: null }]);
    await pressCheckIn(renderer);

    expect(service.gpsCheckIn).toHaveBeenCalledWith({ ...position, direction: "in" });
  });

  it("sends no locationId when the locations cannot be loaded", async () => {
    service.getMyCheckInStatus.mockResolvedValue({ isCheckedIn: false, date: "2026-09-28" });
    service.getCheckInLocations.mockRejectedValue(new Error("network"));
    const renderer = await mount();
    await pressCheckIn(renderer);

    expect(service.gpsCheckIn).toHaveBeenCalledWith({ ...position, direction: "in" });
  });

  it("sends no punch when the biometric prompt is cancelled", async () => {
    confirmAction.mockResolvedValue(false);
    const renderer = await renderScreen([office]);
    await pressCheckIn(renderer);

    expect(service.gpsCheckIn).not.toHaveBeenCalled();
    expect(texts(renderer)).toContain("Kimlik doğrulanmadı; kayıt oluşturulmadı.");
  });

  it("asks for biometrics before a break punch too", async () => {
    service.getMyCheckInStatus.mockResolvedValue({
      isCheckedIn: true,
      date: "2026-09-28",
      checkIn: "09:00:00",
    });
    service.getCheckInLocations.mockResolvedValue([office]);
    confirmAction.mockResolvedValue(false);
    const renderer = await mount();
    await act(async () => {
      button(renderer, "Molaya başla").props.onPress();
    });

    expect(confirmAction).toHaveBeenCalledTimes(1);
    expect(service.gpsCheckIn).not.toHaveBeenCalled();
  });

  it("explains a server-side outside-geofence rejection", async () => {
    const config = { headers: new AxiosHeaders() };
    service.gpsCheckIn.mockRejectedValue(
      new AxiosError("Request failed", "ERR_BAD_REQUEST", config, null, {
        data: { code: "attendance.self_checkin.outside_geofence", detail: "server" },
        status: 400,
        statusText: "Bad Request",
        headers: {},
        config,
      })
    );
    const renderer = await renderScreen([office]);
    await pressCheckIn(renderer);

    expect(texts(renderer)).toContain(
      "Ofis alanının dışındasınız; kayıt oluşturulmadı. Ofis alanına girip tekrar deneyin."
    );
  });
});
