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
 * No fix within this window surfaces "unavailable" (with its "Tekrar dene") instead of spinning on
 * "Konum alınıyor…" forever - Balanced accuracy can stall indoors or with a weak GPS lock.
 */
const FIRST_FIX_TIMEOUT_MS = 15000;

/**
 * Watches the device position while `active` (the tab is focused) and stops on blur/unmount.
 * `retry` restarts the watch (the explicit "Tekrar dene"). `recheck` - run on return to the
 * foreground and on pull-to-refresh - only acts after a denied permission or disabled location
 * services: it reads that state without prompting and restarts once it is resolved, so granting the
 * permission or turning services back on ("Ayarları aç") takes effect without leaving the tab (see
 * `shouldRetryOnForeground`).
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
    if (statusRef.current === "servicesOff") {
      Location.hasServicesEnabledAsync()
        .then((enabled) => {
          if (enabled) retry();
        })
        // Still off as far as we can tell; the banner keeps offering "Ayarları aç".
        .catch(() => undefined);
      return;
    }
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
    let firstFixTimeout: ReturnType<typeof setTimeout> | null = null;

    function clearFirstFixTimeout() {
      if (firstFixTimeout) {
        clearTimeout(firstFixTimeout);
        firstFixTimeout = null;
      }
    }

    async function start() {
      try {
        const servicesEnabled = await Location.hasServicesEnabledAsync();
        if (cancelled) return;
        if (!servicesEnabled) {
          setLocation({ status: "servicesOff", coords: null, accuracy: null });
          return;
        }
        const permission = await Location.requestForegroundPermissionsAsync();
        if (cancelled) return;
        if (permission.status !== "granted") {
          setLocation({ status: "denied", coords: null, accuracy: null });
          return;
        }
        firstFixTimeout = setTimeout(() => {
          if (!cancelled) setLocation((current) => ({ ...current, status: "unavailable" }));
        }, FIRST_FIX_TIMEOUT_MS);
        const subscription = await Location.watchPositionAsync(
          { accuracy: Location.Accuracy.Balanced, distanceInterval: DISTANCE_INTERVAL_METERS },
          (position) => {
            clearFirstFixTimeout();
            setLocation({
              status: "ready",
              coords: { latitude: position.coords.latitude, longitude: position.coords.longitude },
              accuracy: position.coords.accuracy ?? null,
            });
          }
        );
        if (cancelled) {
          subscription.remove();
          return;
        }
        watcher = subscription;
      } catch {
        clearFirstFixTimeout();
        if (!cancelled) setLocation((current) => ({ ...current, status: "unavailable" }));
      }
    }

    void start();
    return () => {
      cancelled = true;
      clearFirstFixTimeout();
      watcher?.remove();
    };
  }, [active, attempt]);

  return { ...location, retry, recheck };
}
