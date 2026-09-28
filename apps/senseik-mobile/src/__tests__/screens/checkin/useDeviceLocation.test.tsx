import React from "react";
import { AppState, type AppStateStatus } from "react-native";
import { act, create } from "react-test-renderer";
import * as Location from "expo-location";
import { useDeviceLocation } from "@/screens/app/checkin/useDeviceLocation";

jest.mock("expo-location", () => ({
  Accuracy: { Balanced: 3, High: 4 },
  requestForegroundPermissionsAsync: jest.fn(),
  getForegroundPermissionsAsync: jest.fn(),
  hasServicesEnabledAsync: jest.fn(),
  watchPositionAsync: jest.fn(),
}));

const mockedLocation = Location as jest.Mocked<typeof Location>;
type Hook = ReturnType<typeof useDeviceLocation>;

let appStateListener: ((state: AppStateStatus) => void) | null = null;
let latest: Hook | null = null;

function Probe() {
  latest = useDeviceLocation(true);
  return null;
}

async function flush() {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
}

async function mount() {
  await act(async () => {
    create(<Probe />);
  });
  await flush();
}

async function foreground() {
  await act(async () => {
    appStateListener?.("active");
  });
  await flush();
}

beforeEach(() => {
  // Fake timers everywhere: the hook now schedules a real 15s first-fix timeout on every mount, and
  // `mount()` never unmounts its renderer between tests - without this, those timers stay pending
  // and fire for real after the suite finishes ("Cannot log after tests are done").
  jest.useFakeTimers();
  jest.clearAllMocks();
  latest = null;
  jest.spyOn(AppState, "addEventListener").mockImplementation((_type, listener) => {
    appStateListener = listener as (state: AppStateStatus) => void;
    return { remove: jest.fn() } as unknown as ReturnType<typeof AppState.addEventListener>;
  });
  mockedLocation.requestForegroundPermissionsAsync.mockResolvedValue({
    status: "granted",
  } as Location.LocationPermissionResponse);
  mockedLocation.getForegroundPermissionsAsync.mockResolvedValue({
    status: "granted",
  } as Location.LocationPermissionResponse);
  mockedLocation.hasServicesEnabledAsync.mockResolvedValue(true);
});

afterEach(() => {
  jest.useRealTimers();
});

describe("useDeviceLocation", () => {
  it("watches with balanced accuracy", async () => {
    mockedLocation.watchPositionAsync.mockResolvedValue({ remove: jest.fn() });
    await mount();
    expect(mockedLocation.watchPositionAsync).toHaveBeenCalledWith(
      expect.objectContaining({ accuracy: Location.Accuracy.Balanced }),
      expect.any(Function)
    );
  });

  it("does not restart watching on foreground or pull-to-refresh after 'unavailable', only on Tekrar dene", async () => {
    // Declining Google's "Location Accuracy" dialog makes the watch fail.
    mockedLocation.watchPositionAsync.mockRejectedValue(new Error("location settings unsatisfied"));
    await mount();
    expect(latest?.status).toBe("unavailable");
    expect(mockedLocation.watchPositionAsync).toHaveBeenCalledTimes(1);

    await foreground();
    await act(async () => latest?.recheck());
    await flush();
    expect(mockedLocation.watchPositionAsync).toHaveBeenCalledTimes(1);

    await act(async () => latest?.retry());
    await flush();
    expect(mockedLocation.watchPositionAsync).toHaveBeenCalledTimes(2);
  });

  it("re-checks without prompting on foreground after 'denied' and starts once granted", async () => {
    mockedLocation.requestForegroundPermissionsAsync.mockResolvedValue({
      status: "denied",
    } as Location.LocationPermissionResponse);
    mockedLocation.getForegroundPermissionsAsync.mockResolvedValue({
      status: "denied",
    } as Location.LocationPermissionResponse);
    mockedLocation.watchPositionAsync.mockResolvedValue({ remove: jest.fn() });
    await mount();
    expect(latest?.status).toBe("denied");

    await foreground();
    expect(mockedLocation.getForegroundPermissionsAsync).toHaveBeenCalledTimes(1);
    expect(mockedLocation.requestForegroundPermissionsAsync).toHaveBeenCalledTimes(1);
    expect(mockedLocation.watchPositionAsync).not.toHaveBeenCalled();

    // Granted in the system settings, then back to the app.
    mockedLocation.getForegroundPermissionsAsync.mockResolvedValue({
      status: "granted",
    } as Location.LocationPermissionResponse);
    mockedLocation.requestForegroundPermissionsAsync.mockResolvedValue({
      status: "granted",
    } as Location.LocationPermissionResponse);
    await foreground();
    expect(mockedLocation.watchPositionAsync).toHaveBeenCalledTimes(1);
  });

  it("goes straight to 'servicesOff' - without prompting for permission - when location services are off", async () => {
    mockedLocation.hasServicesEnabledAsync.mockResolvedValue(false);
    await mount();

    expect(latest?.status).toBe("servicesOff");
    expect(mockedLocation.requestForegroundPermissionsAsync).not.toHaveBeenCalled();
    expect(mockedLocation.watchPositionAsync).not.toHaveBeenCalled();
  });

  it("re-checks without prompting on foreground after 'servicesOff' and starts once services are on", async () => {
    mockedLocation.hasServicesEnabledAsync.mockResolvedValue(false);
    mockedLocation.watchPositionAsync.mockResolvedValue({ remove: jest.fn() });
    await mount();
    expect(latest?.status).toBe("servicesOff");

    await foreground();
    expect(mockedLocation.hasServicesEnabledAsync).toHaveBeenCalledTimes(2);
    expect(mockedLocation.watchPositionAsync).not.toHaveBeenCalled();

    // Turned on in the system settings, then back to the app.
    mockedLocation.hasServicesEnabledAsync.mockResolvedValue(true);
    await foreground();
    expect(mockedLocation.watchPositionAsync).toHaveBeenCalledTimes(1);
  });

  it("moves from 'pending' to 'unavailable' after the first-fix timeout when no position ever arrives", async () => {
    mockedLocation.watchPositionAsync.mockResolvedValue({ remove: jest.fn() });
    await mount();
    expect(latest?.status).toBe("pending");

    await act(async () => {
      jest.advanceTimersByTime(15000);
      await Promise.resolve();
    });

    expect(latest?.status).toBe("unavailable");
  });

  it("does not auto-restart after the first-fix timeout on foreground, only on Tekrar dene", async () => {
    mockedLocation.watchPositionAsync.mockResolvedValue({ remove: jest.fn() });
    await mount();
    await act(async () => {
      jest.advanceTimersByTime(15000);
      await Promise.resolve();
    });
    expect(latest?.status).toBe("unavailable");
    expect(mockedLocation.watchPositionAsync).toHaveBeenCalledTimes(1);

    await foreground();
    expect(mockedLocation.watchPositionAsync).toHaveBeenCalledTimes(1);

    await act(async () => latest?.retry());
    await flush();
    expect(mockedLocation.watchPositionAsync).toHaveBeenCalledTimes(2);
  });
});
