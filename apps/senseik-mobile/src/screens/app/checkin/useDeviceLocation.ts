import { useCallback, useEffect, useState } from "react";
import { AppState } from "react-native";
import * as Location from "expo-location";
import type { Coordinates } from "./MapHeader";

export type DeviceLocation = {
  /** `denied`: foreground permission refused; `unavailable`: permission granted but no fix could be read. */
  status: "pending" | "denied" | "unavailable" | "ready";
  coords: Coordinates | null;
  /** `coords.accuracy` in meters; `null` when the platform does not report it. */
  accuracy: number | null;
};

const INITIAL: DeviceLocation = { status: "pending", coords: null, accuracy: null };
/** Only re-render for movements that matter at the map's 60 m circle scale. */
const DISTANCE_INTERVAL_METERS = 10;

/**
 * Watches the device position while `active` (the tab is focused) and stops on blur/unmount. The
 * permission is re-checked when the app returns to the foreground, so granting it in the system
 * settings ("Ayarları aç") takes effect without leaving the tab.
 */
export function useDeviceLocation(active: boolean): DeviceLocation & { retry: () => void } {
  const [location, setLocation] = useState<DeviceLocation>(INITIAL);
  const [attempt, setAttempt] = useState(0);
  const retry = useCallback(() => setAttempt((value) => value + 1), []);

  useEffect(() => {
    const subscription = AppState.addEventListener("change", (next) => {
      if (next === "active") retry();
    });
    return () => subscription.remove();
  }, [retry]);

  useEffect(() => {
    if (!active) return undefined;
    let cancelled = false;
    let watcher: Location.LocationSubscription | null = null;

    async function start() {
      try {
        const permission = await Location.requestForegroundPermissionsAsync();
        if (cancelled) return;
        if (permission.status !== "granted") {
          setLocation({ status: "denied", coords: null, accuracy: null });
          return;
        }
        const subscription = await Location.watchPositionAsync(
          { accuracy: Location.Accuracy.High, distanceInterval: DISTANCE_INTERVAL_METERS },
          (position) =>
            setLocation({
              status: "ready",
              coords: { latitude: position.coords.latitude, longitude: position.coords.longitude },
              accuracy: position.coords.accuracy ?? null,
            })
        );
        if (cancelled) {
          subscription.remove();
          return;
        }
        watcher = subscription;
      } catch {
        if (!cancelled) setLocation((current) => ({ ...current, status: "unavailable" }));
      }
    }

    void start();
    return () => {
      cancelled = true;
      watcher?.remove();
    };
  }, [active, attempt]);

  return { ...location, retry };
}
