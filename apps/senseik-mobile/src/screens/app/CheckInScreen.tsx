import React, { useCallback, useEffect, useRef, useState } from "react";
import { ActivityIndicator, Linking, Platform, StyleSheet, Text, View } from "react-native";
import { useFocusEffect, useIsFocused } from "@react-navigation/native";
import { CameraView, useCameraPermissions, type BarcodeScanningResult } from "expo-camera";
import Constants from "expo-constants";
import { ErrorBanner, PrimaryButton, Screen } from "@/components/ui";
import { getApiErrorMessage } from "@/lib/apiError";
import {
  PUNCH_LABELS,
  availableActions,
  nextPunchAction,
  punchTimes,
  qrButtonLabel,
  shouldRenderMap,
  type PunchAction,
} from "@/lib/punch";
import {
  attendanceService,
  type MyCheckInStatus,
  type SelfCheckInResult,
} from "@/services/AttendanceService";
import { palette, radius, text, tones } from "@/theme/theme";
import { ClockCard } from "./checkin/ClockCard";
import { MapHeader, type Coordinates } from "./checkin/MapHeader";
import { PunchTiles } from "./checkin/PunchTiles";
import { useDeviceLocation, type DeviceLocation } from "./checkin/useDeviceLocation";

const PUNCH_ERROR = "Giriş/çıkış kaydedilemedi. Lütfen tekrar deneyin.";
const SHOW_MAP = shouldRenderMap(
  Constants.expoConfig?.extra as { hasMapsKey?: boolean } | undefined,
  Platform.OS
);

function placeLabel(location: DeviceLocation, lastResult: SelfCheckInResult | null): string {
  if (lastResult?.locationName) return lastResult.locationName;
  if (location.coords) {
    return `${location.coords.latitude.toFixed(5)}, ${location.coords.longitude.toFixed(5)}`;
  }
  if (location.status === "denied") return "Konum izni verilmedi";
  if (location.status === "unavailable") return "Konum alınamadı";
  return "Konum alınıyor…";
}

function successMessage(result: SelfCheckInResult): string {
  const tile = PUNCH_LABELS[result.direction]?.tile ?? "Kayıt";
  const place = result.locationName ? ` · ${result.locationName}` : "";
  return `${tile} kaydedildi: ${result.time.slice(0, 5)}${place}`;
}

/**
 * Giriş / Çıkış tab (spec §6): map (or location card), live clock, 2×2 punch tiles and one primary
 * button for the next punch, all by GPS. QR scanning stays as the secondary way in.
 */
export default function CheckInScreen() {
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
      setStatusError(getApiErrorMessage(err, "Giriş/çıkış durumu yüklenemedi."));
    }
  }, []);

  useFocusEffect(
    useCallback(() => {
      void loadStatus();
    }, [loadStatus])
  );

  useEffect(() => {
    if (!isFocused) setScanning(false);
  }, [isFocused]);

  /** Runs one punch; the status is the source of truth afterwards, so it is always refetched. */
  async function submit(kind: PunchAction | "qr", send: () => Promise<SelfCheckInResult>) {
    if (inFlightRef.current) return;
    inFlightRef.current = true;
    setBusy(kind);
    setError(null);
    try {
      const result = await send();
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
      setError(getApiErrorMessage(err, PUNCH_ERROR));
    } finally {
      inFlightRef.current = false;
      setBusy(null);
    }
  }

  function punch(action: PunchAction, coords: Coordinates) {
    void submit(action, () => attendanceService.gpsCheckIn({ ...coords, direction: action }));
  }

  async function startScan() {
    setError(null);
    if (!cameraPermission?.granted) {
      const response = await requestCameraPermission();
      if (!response.granted) {
        setError("Kamera izni verilmedi. QR kodu okutmak için ayarlardan kamera iznini açın.");
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
    await loadStatus();
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
          <PrimaryButton label="Vazgeç" variant="secondary" onPress={() => setScanning(false)} />
        </View>
      </Screen>
    );
  }

  const coords = location.coords;
  const workingSince = status?.checkIn && !status.checkOut ? status.checkIn : null;
  const breakMinutes = status?.breakMinutesToday ?? 0;

  return (
    <Screen withTabBar refreshing={refreshing} onRefresh={() => void onRefresh()}>
      <MapHeader
        showMap={SHOW_MAP}
        position={coords}
        placeLabel={placeLabel(location, lastResult)}
      />
      {location.status === "denied" ? (
        <View style={styles.banner}>
          <ErrorBanner
            message="Konum izni verilmedi. Konumla giriş/çıkış için ayarlardan konum iznini açın."
            actionLabel="Ayarları aç"
            onRetry={() => void Linking.openSettings()}
          />
        </View>
      ) : null}
      {location.status === "unavailable" && !coords ? (
        <View style={styles.banner}>
          <ErrorBanner message="Konum alınamadı." onRetry={location.retry} />
        </View>
      ) : null}

      <ClockCard
        active={isFocused}
        workingSince={workingSince}
        accuracyMeters={location.accuracy}
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
            <Text style={styles.breakTotal}>Bugünkü mola: {breakMinutes} dk</Text>
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
              <PrimaryButton label="Bugün tamamlandı" disabled onPress={() => undefined} />
            ) : (
              <PrimaryButton
                label={PUNCH_LABELS[next].action}
                onPress={() => coords && punch(next, coords)}
                loading={busy === next}
                disabled={busy !== null || !coords}
              />
            )}
            {next === "out" ? (
              <PrimaryButton
                label={PUNCH_LABELS.breakStart.action}
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
