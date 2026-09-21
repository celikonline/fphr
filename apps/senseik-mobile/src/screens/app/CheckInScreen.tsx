import React, { useCallback, useEffect, useRef, useState } from "react";
import { ActivityIndicator, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { CameraView, useCameraPermissions, type BarcodeScanningResult } from "expo-camera";
import * as Location from "expo-location";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import { attendanceService, type MyCheckInStatus, type SelfCheckInResult } from "@/services/AttendanceService";

/** Best-effort device position; geofence'siz lokasyonda konum zorunlu olmadığı için izin/okuma hatası engel değildir. */
async function tryGetPosition(): Promise<{ latitude: number; longitude: number } | undefined> {
  try {
    const permission = await Location.requestForegroundPermissionsAsync();
    if (permission.status !== "granted") return undefined;
    const position = await Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.Balanced });
    return { latitude: position.coords.latitude, longitude: position.coords.longitude };
  } catch {
    return undefined;
  }
}

function errorMessage(error: unknown): string {
  const detail = (error as { response?: { data?: { detail?: string } } })?.response?.data?.detail;
  return detail || "Giriş/çıkış kaydedilemedi. Lütfen tekrar deneyin.";
}

export default function CheckInScreen() {
  const [permission, requestPermission] = useCameraPermissions();
  const [status, setStatus] = useState<MyCheckInStatus | null>(null);
  const [scanning, setScanning] = useState(false);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<SelfCheckInResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const handlingRef = useRef(false);

  const loadStatus = useCallback(async () => {
    try {
      setStatus(await attendanceService.getMyCheckInStatus());
    } catch {
      setStatus(null);
    }
  }, []);

  useEffect(() => {
    void loadStatus();
  }, [loadStatus]);

  const direction = status?.isCheckedIn ? "out" : "in";
  const actionLabel = direction === "in" ? "Giriş yap" : "Çıkış yap";

  async function startScan() {
    setError(null);
    setResult(null);
    if (!permission?.granted) {
      const response = await requestPermission();
      if (!response.granted) {
        setError("Kamera izni verilmedi. QR kodu okutmak için ayarlardan kamera iznini açın.");
        return;
      }
    }
    handlingRef.current = false;
    setScanning(true);
  }

  async function onScanned({ data }: BarcodeScanningResult) {
    if (handlingRef.current) return;
    handlingRef.current = true;
    setScanning(false);
    setBusy(true);
    try {
      const position = await tryGetPosition();
      const response = await attendanceService.qrCheckIn({
        code: data.trim().toUpperCase(),
        direction,
        ...(position ?? {})
      });
      setResult(response);
      await loadStatus();
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  if (scanning) {
    return (
      <View style={styles.container}>
        <CameraView
          style={styles.camera}
          facing="back"
          barcodeScannerSettings={{ barcodeTypes: ["qr"] }}
          onBarcodeScanned={event => void onScanned(event)}
        />
        <TouchableOpacity style={styles.secondaryButton} onPress={() => setScanning(false)}>
          <Text style={styles.secondaryButtonText}>Vazgeç</Text>
        </TouchableOpacity>
      </View>
    );
  }

  return (
    <View style={styles.container}>
      <Text style={styles.title}>{status?.isCheckedIn ? "İçeridesiniz" : "Dışarıdasınız"}</Text>
      <Text style={styles.hint}>
        Lokasyon girişindeki QR kodu okutarak {direction === "in" ? "giriş" : "çıkış"} kaydı oluşturun. Lokasyonda
        geofence tanımlıysa konumunuz da doğrulanır.
      </Text>

      {error && (
        <View style={styles.errorBanner}>
          <Text style={styles.errorText}>{error}</Text>
        </View>
      )}
      {result && (
        <View style={styles.successBanner}>
          <Text style={styles.successText}>
            {result.direction === "in" ? "Giriş" : "Çıkış"} kaydedildi: {result.time.slice(0, 5)}
            {result.locationName ? ` · ${result.locationName}` : ""}
          </Text>
        </View>
      )}

      {busy ? (
        <ActivityIndicator size="large" color={colors.primary} />
      ) : (
        <TouchableOpacity style={styles.primaryButton} onPress={() => void startScan()}>
          <Text style={styles.primaryButtonText}>QR okut · {actionLabel}</Text>
        </TouchableOpacity>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.background, padding: spacing.xl },
  camera: { flex: 1, borderRadius: 8, overflow: "hidden", marginBottom: spacing.lg },
  title: { ...typography.h2, color: colors.text, marginBottom: spacing.sm },
  hint: { ...typography.bodySm, color: colors.textSecondary, marginBottom: spacing.lg },
  primaryButton: { backgroundColor: colors.primary, borderRadius: 8, padding: spacing.lg, alignItems: "center" },
  primaryButtonText: { ...typography.body, color: colors.white, fontWeight: "600" },
  secondaryButton: { borderWidth: 1, borderColor: colors.border, borderRadius: 8, padding: spacing.md, alignItems: "center" },
  secondaryButtonText: { ...typography.body, color: colors.text },
  errorBanner: { backgroundColor: "#fdecea", borderRadius: 8, padding: spacing.md, marginBottom: spacing.md },
  errorText: { ...typography.bodySm, color: colors.error },
  successBanner: { backgroundColor: "#e8f7ec", borderRadius: 8, padding: spacing.md, marginBottom: spacing.md },
  successText: { ...typography.bodySm, color: colors.secondaryDark }
});
