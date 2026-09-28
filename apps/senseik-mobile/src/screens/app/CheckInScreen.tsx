import React, { useCallback, useEffect, useRef, useState } from "react";
import { ActivityIndicator, Linking, Platform, StyleSheet, Text, View } from "react-native";
import { useFocusEffect, useIsFocused } from "@react-navigation/native";
import { CameraView, useCameraPermissions, type BarcodeScanningResult } from "expo-camera";
import Constants from "expo-constants";
import { useTranslation } from "react-i18next";
import i18n from "@/lib/i18n";
import { ErrorBanner, PrimaryButton, Screen } from "@/components/ui";
import { getApiErrorMessage } from "@/lib/apiError";
import { biometricService } from "@/lib/biometric";
import {
  PUNCH_ACTIONS,
  availableActions,
  nextPunchAction,
  punchErrorMessage,
  punchLabel,
  punchTimes,
  qrButtonLabel,
  sendConfirmedPunch,
  shouldRenderMap,
  type PunchAction,
} from "@/lib/punch";
import {
  attendanceService,
  type CheckInLocation,
  type MyCheckInStatus,
  type SelfCheckInResult,
} from "@/services/AttendanceService";
import { palette, radius, text, tones } from "@/theme/theme";
import { ClockCard } from "./checkin/ClockCard";
import { nearestLocation, punchLocationId } from "./checkin/geofence";
import { MapHeader, type Coordinates } from "./checkin/MapHeader";
import { PunchTiles } from "./checkin/PunchTiles";
import { useDeviceLocation, type DeviceLocation } from "./checkin/useDeviceLocation";

const SHOW_MAP = shouldRenderMap(
  Constants.expoConfig?.extra as { hasMapsKey?: boolean } | undefined,
  Platform.OS
);

function placeLabel(location: DeviceLocation, lastResult: SelfCheckInResult | null): string {
  if (lastResult?.locationName) return lastResult.locationName;
  if (location.coords) {
    return `${location.coords.latitude.toFixed(5)}, ${location.coords.longitude.toFixed(5)}`;
  }
  if (location.status === "denied") return i18n.t("checkin:place.denied");
  if (location.status === "servicesOff") return i18n.t("checkin:place.servicesOff");
  if (location.status === "unavailable") return i18n.t("checkin:place.unavailable");
  return i18n.t("checkin:place.locating");
}

/** Android: jumps straight to the location toggle; iOS has no such deep link, falls back to app settings. */
async function openLocationSettings(): Promise<void> {
  if (Platform.OS === "android") {
    try {
      await Linking.sendIntent("android.settings.LOCATION_SOURCE_SETTINGS");
      return;
    } catch {
      // Fall through to the app's own settings screen below.
    }
  }
  await Linking.openSettings();
}

function successMessage(result: SelfCheckInResult): string {
  const tile = PUNCH_ACTIONS.includes(result.direction)
    ? punchLabel(result.direction).tile
    : i18n.t("checkin:success.fallbackTile");
  const place = result.locationName ? ` · ${result.locationName}` : "";
  return i18n.t("checkin:success.saved", { tile, time: result.time.slice(0, 5), place });
}

/**
 * Giriş / Çıkış tab (spec §6): map (or location card), live clock, 2×2 punch tiles and one primary
 * button for the next punch, all by GPS. QR scanning stays as the secondary way in.
 */
export default function CheckInScreen() {
  const { t } = useTranslation("checkin");
  const isFocused = useIsFocused();
  const location = useDeviceLocation(isFocused);
  const [cameraPermission, requestCameraPermission] = useCameraPermissions();
  const [status, setStatus] = useState<MyCheckInStatus | null>(null);
  const [statusError, setStatusError] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [scanning, setScanning] = useState(false);
  const [busy, setBusy] = useState<PunchAction | "qr" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [lastResult, setLastResult] = useState<SelfCheckInResult | null>(null);
  // FG-005: the caller's check-in location(s); `[]` (roaming, or not loaded) keeps today's behavior.
  const [locations, setLocations] = useState<CheckInLocation[]>([]);
  // This session's punch times, keyed by attendance day so an overnight-open app never shows
  // yesterday's break on today's tiles.
  const [session, setSession] = useState<{
    date: string;
    times: Partial<Record<PunchAction, string>>;
  }>({ date: "", times: {} });
  const inFlightRef = useRef(false);

  const loadStatus = useCallback(async () => {
    try {
      setStatus(await attendanceService.getMyCheckInStatus());
      setStatusError(null);
    } catch (err) {
      setStatusError(getApiErrorMessage(err, i18n.t("checkin:errors.statusLoad")));
    }
  }, []);

  const loadLocations = useCallback(async () => {
    try {
      setLocations(await attendanceService.getCheckInLocations());
    } catch {
      // Not blocking: without a location the punch goes without `locationId`, as before FG-005.
    }
  }, []);

  useFocusEffect(
    useCallback(() => {
      void loadStatus();
      void loadLocations();
    }, [loadStatus, loadLocations])
  );

  useEffect(() => {
    if (!isFocused) setScanning(false);
  }, [isFocused]);

  /**
   * Runs one punch after the biometric confirmation (FG-005, every punch kind incl. QR); a cancelled
   * confirmation sends nothing. The status is the source of truth afterwards, so it is always refetched.
   */
  async function submit(kind: PunchAction | "qr", send: () => Promise<SelfCheckInResult>) {
    if (inFlightRef.current) return;
    inFlightRef.current = true;
    setBusy(kind);
    setError(null);
    try {
      const result = await sendConfirmedPunch(
        () => biometricService.confirmAction(t("biometric.punchPrompt")),
        send
      );
      if (!result) {
        setError(t("biometric.cancelled"));
        return;
      }
      setLastResult(result);
      setSession((current) => ({
        date: result.date,
        times: {
          ...(current.date === result.date ? current.times : {}),
          [result.direction]: result.time,
        },
      }));
      await loadStatus();
    } catch (err) {
      setError(punchErrorMessage(err));
    } finally {
      inFlightRef.current = false;
      setBusy(null);
    }
  }

  function punch(action: PunchAction, coords: Coordinates) {
    // Karar 2: `locationId` only for a location with a radius - the server then enforces it.
    const locationId = punchLocationId(nearestLocation(locations, coords));
    void submit(action, () =>
      attendanceService.gpsCheckIn({
        ...coords,
        ...(locationId ? { locationId } : {}),
        direction: action,
      })
    );
  }

  async function startScan() {
    setError(null);
    if (!cameraPermission?.granted) {
      const response = await requestCameraPermission();
      if (!response.granted) {
        setError(t("errors.cameraDenied"));
        return;
      }
    }
    setScanning(true);
  }

  function onScanned({ data }: BarcodeScanningResult, direction: PunchAction) {
    if (inFlightRef.current) return;
    setScanning(false);
    // Geofence'siz lokasyonda konum zorunlu değil: konum yoksa QR yine de denenir, sunucu karar verir.
    const coords = location.coords ?? {};
    void submit("qr", () =>
      attendanceService.qrCheckIn({ code: data.trim().toUpperCase(), direction, ...coords })
    );
  }

  async function onRefresh() {
    setRefreshing(true);
    // Never `retry` here: after "unavailable" that would reopen the declined accuracy dialog.
    location.recheck();
    await Promise.all([loadStatus(), loadLocations()]);
    setRefreshing(false);
  }

  const next = status ? nextPunchAction(status) : null;

  if (scanning && next && next !== "done") {
    return (
      <Screen withTabBar scroll={false}>
        <CameraView
          style={styles.camera}
          facing="back"
          barcodeScannerSettings={{ barcodeTypes: ["qr"] }}
          onBarcodeScanned={(event) => onScanned(event, next)}
        />
        <View style={styles.actions}>
          <PrimaryButton
            label={t("common:actions.cancel")}
            variant="secondary"
            onPress={() => setScanning(false)}
          />
        </View>
      </Screen>
    );
  }

  const coords = location.coords;
  const nearest = nearestLocation(locations, coords);
  const workingSince = status?.checkIn && !status.checkOut ? status.checkIn : null;
  const breakMinutes = status?.breakMinutesToday ?? 0;

  return (
    <Screen withTabBar refreshing={refreshing} onRefresh={() => void onRefresh()}>
      <MapHeader
        showMap={SHOW_MAP}
        position={coords}
        placeLabel={placeLabel(location, lastResult)}
        geofence={nearest?.geofence ?? null}
      />
      {location.status === "denied" ? (
        <View style={styles.banner}>
          <ErrorBanner
            message={t("banners.denied")}
            actionLabel={t("banners.openSettings")}
            onRetry={() => void Linking.openSettings()}
          />
        </View>
      ) : null}
      {location.status === "servicesOff" ? (
        <View style={styles.banner}>
          <ErrorBanner
            message={t("banners.servicesOff")}
            actionLabel={t("banners.openSettings")}
            onRetry={() => void openLocationSettings()}
          />
        </View>
      ) : null}
      {location.status === "unavailable" && !coords ? (
        <View style={styles.banner}>
          <ErrorBanner message={t("banners.unavailable")} onRetry={location.retry} />
        </View>
      ) : null}

      <ClockCard
        active={isFocused}
        workingSince={workingSince}
        accuracyMeters={location.accuracy}
        location={nearest}
      />

      {statusError ? (
        <View style={styles.banner}>
          <ErrorBanner message={statusError} onRetry={() => void loadStatus()} />
        </View>
      ) : null}

      {status && next ? (
        <>
          <PunchTiles
            times={punchTimes(status, session.date === status.date ? session.times : {})}
            available={availableActions(status)}
            next={next}
            disabled={busy !== null || !coords}
            onPress={(action) => coords && punch(action, coords)}
          />
          {breakMinutes > 0 ? (
            <Text style={styles.breakTotal}>{t("breakTotal", { minutes: breakMinutes })}</Text>
          ) : null}
          {error ? (
            <View style={styles.banner}>
              <ErrorBanner message={error} />
            </View>
          ) : lastResult ? (
            <Text style={styles.success} accessibilityLiveRegion="polite">
              {successMessage(lastResult)}
            </Text>
          ) : null}
          <View style={styles.actions}>
            {next === "done" ? (
              <PrimaryButton label={t("doneToday")} disabled onPress={() => undefined} />
            ) : (
              <PrimaryButton
                label={punchLabel(next).action}
                onPress={() => coords && punch(next, coords)}
                loading={busy === next}
                disabled={busy !== null || !coords}
              />
            )}
            {next === "out" ? (
              <PrimaryButton
                label={punchLabel("breakStart").action}
                variant="secondary"
                icon="cafe-outline"
                onPress={() => coords && punch("breakStart", coords)}
                loading={busy === "breakStart"}
                disabled={busy !== null || !coords}
              />
            ) : null}
            <PrimaryButton
              label={qrButtonLabel(next)}
              variant="secondary"
              icon="qr-code-outline"
              onPress={() => void startScan()}
              loading={busy === "qr"}
              disabled={busy !== null || next === "done"}
            />
          </View>
        </>
      ) : statusError ? null : (
        <ActivityIndicator style={styles.loading} color={palette.white} />
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  camera: {
    flex: 1,
    marginHorizontal: 20,
    marginTop: 8,
    borderRadius: radius.card,
    overflow: "hidden",
  },
  banner: {
    marginTop: 12,
  },
  breakTotal: {
    ...text.caption,
    textAlign: "center",
    marginTop: 10,
  },
  success: {
    ...text.bodyMuted,
    color: tones.success.fg,
    textAlign: "center",
    marginTop: 12,
    marginHorizontal: 20,
  },
  actions: {
    gap: 10,
    marginHorizontal: 20,
    marginTop: 16,
  },
  loading: {
    paddingVertical: 24,
  },
});
