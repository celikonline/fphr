import { useCallback, useEffect, useRef, useState } from "react";
import { AppState } from "react-native";
import * as Location from "expo-location";
import { shouldRetryOnForeground, type LocationStatus } from "./locationRetry";
import type { Coordinates } from "./MapHeader";

export type DeviceLocation = {
  status: LocationStatus;
  coords: Coordinates | null;
  /** `coords.accuracy` in meters; `null` when the platform does not report it. */
  accuracy: number | null;
};

const INITIAL: DeviceLocation = { status: "pending", coords: null, accuracy: null };
/** Only re-render for movements that matter at the map's 60 m circle scale. */
const DISTANCE_INTERVAL_METERS = 10;

/**
 * Watches the device position while `active` (the tab is focused) and stops on blur/unmount.
 * `retry` restarts the watch (the explicit "Tekrar dene"). `recheck` - run on return to the
 * foreground and on pull-to-refresh - only acts after a denied permission: it reads the permission
 * without prompting and restarts once it is granted, so granting it in the system settings
 * ("Ayarları aç") takes effect without leaving the tab (see `shouldRetryOnForeground`).
 */
export function useDeviceLocation(
  active: boolean
): DeviceLocation & { retry: () => void; recheck: () => void } {
  const [location, setLocation] = useState<DeviceLocation>(INITIAL);
  const [attempt, setAttempt] = useState(0);
  const statusRef = useRef<LocationStatus>(INITIAL.status);
  const retry = useCallback(() => setAttempt((value) => value + 1), []);

  const recheck = useCallback(() => {
    if (!shouldRetryOnForeground(statusRef.current)) return;
    Location.getForegroundPermissionsAsync()
      .then((permission) => {
        if (permission.status === "granted") retry();
      })
      // Still denied as far as we can tell; the banner keeps offering "Ayarları aç".
      .catch(() => undefined);
  }, [retry]);

  useEffect(() => {
    statusRef.current = location.status;
  }, [location.status]);

  useEffect(() => {
    const subscription = AppState.addEventListener("change", (next) => {
      if (next === "active") recheck();
    });
    return () => subscription.remove();
  }, [recheck]);

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
          { accuracy: Location.Accuracy.Balanced, distanceInterval: DISTANCE_INTERVAL_METERS },
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

  return { ...location, retry, recheck };
}
