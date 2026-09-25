import { useState } from "react";
import { useTranslation } from "react-i18next";
import { getAttendanceErrorMessage } from "@/lib/attendance-error";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Badge, Button, Card, Group, Stack, Text, TextInput, Title } from "@mantine/core";
import { LogIn, LogOut, MapPin, QrCode } from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getMyCheckInStatus, gpsCheckIn, qrCheckIn, type PunchDirection, type SelfCheckInResult } from "@/services/attendance.service";

const STATUS_QUERY_KEY = ["attendance", "my-checkin-status"];

/**
 * Self-servis QR/GPS check-in (S-31 kabul kriteri 1/2): "Puantajım"dan ayrı, tek amaçlı bir sayfa - çalışan burada
 * ya lokasyon girişindeki QR kodu elle girer (kamera taraması mobil uygulamada, bkz. handoff) ya da "Konumumla
 * giriş yap" ile tarayıcının GPS'ini kullanır. attendance.my.checkin.
 */
export default function SelfCheckInPage() {
  const canCheckIn = usePermission("attendance.my.checkin");
  if (!canCheckIn) return <NoAccess />;
  return <SelfCheckIn />;
}

function SelfCheckIn() {
  const { t } = useTranslation(["attendance", "common"]);
  const queryClient = useQueryClient();
  const [code, setCode] = useState("");
  const [geoError, setGeoError] = useState<string | null>(null);
  const [lastResult, setLastResult] = useState<SelfCheckInResult | null>(null);

  const statusQuery = useQuery({ queryKey: STATUS_QUERY_KEY, queryFn: getMyCheckInStatus });
  const direction: PunchDirection = statusQuery.data?.isCheckedIn ? "out" : "in";

  function onSuccess(result: SelfCheckInResult) {
    setLastResult(result);
    setGeoError(null);
    queryClient.invalidateQueries({ queryKey: STATUS_QUERY_KEY });
    toast({
      variant: "success",
      title: t("common:success"),
      description: t(result.direction === "in" ? "attendance:selfCheckIn.checkedInAt" : "attendance:selfCheckIn.checkedOutAt", { time: result.time.slice(0, 5) }),
    });
  }

  function onError(error: unknown) {
    toast({ variant: "destructive", title: t("common:error"), description: getAttendanceErrorMessage(error, t("attendance:genericError"), t) });
  }

  const qrMutation = useMutation({ mutationFn: qrCheckIn, onSuccess, onError });
  const gpsMutation = useMutation({ mutationFn: gpsCheckIn, onSuccess, onError });

  /** Best-effort konum: geofence'li lokasyonlarda sunucu zorunlu tutar ve reddeder; izin yoksa konumsuz gönderilir. */
  function readPosition(): Promise<{ latitude: number; longitude: number } | null> {
    if (!navigator.geolocation) return Promise.resolve(null);
    return new Promise((resolve) => {
      navigator.geolocation.getCurrentPosition(
        (position) => resolve({ latitude: position.coords.latitude, longitude: position.coords.longitude }),
        () => resolve(null),
        { enableHighAccuracy: true, timeout: 10_000 },
      );
    });
  }

  async function handleQrSubmit() {
    if (!code.trim()) return;
    const position = await readPosition();
    qrMutation.mutate({ code: code.trim().toUpperCase(), direction, ...(position ?? {}) });
  }

  function handleGpsCheckIn() {
    setGeoError(null);
    if (!navigator.geolocation) {
      setGeoError(t("attendance:selfCheckIn.geolocationUnsupported"));
      return;
    }

    navigator.geolocation.getCurrentPosition(
      (position) => {
        gpsMutation.mutate({ latitude: position.coords.latitude, longitude: position.coords.longitude, direction });
      },
      () => setGeoError(t("attendance:selfCheckIn.geolocationDenied")),
      { enableHighAccuracy: true, timeout: 10_000 },
    );
  }

  const isCheckedIn = statusQuery.data?.isCheckedIn ?? false;
  const actionLabel = isCheckedIn ? t("attendance:selfCheckIn.checkOut") : t("attendance:selfCheckIn.checkIn");
  const isBusy = qrMutation.isPending || gpsMutation.isPending;

  return (
    <div className="max-w-lg space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <LogIn size={24} /> {t("attendance:selfCheckIn.title")}
        </Title>
        <Text c="dimmed">{t("attendance:selfCheckIn.subtitle")}</Text>
      </div>

      <Card withBorder padding="lg">
        <Group justify="space-between" align="center">
          <Text size="sm" c="dimmed">
            {t("attendance:selfCheckIn.currentStatus")}
          </Text>
          <Badge color={isCheckedIn ? "green" : "gray"} size="lg" leftSection={isCheckedIn ? <LogIn size={12} /> : <LogOut size={12} />}>
            {isCheckedIn ? t("attendance:selfCheckIn.statusInside") : t("attendance:selfCheckIn.statusOutside")}
          </Badge>
        </Group>
        {statusQuery.data?.checkIn && (
          <Text size="xs" c="dimmed" mt="xs">
            {t("attendance:selfCheckIn.todayCheckIn", { time: statusQuery.data.checkIn.slice(0, 5) })}
            {statusQuery.data.checkOut ? ` · ${t("attendance:selfCheckIn.todayCheckOut", { time: statusQuery.data.checkOut.slice(0, 5) })}` : ""}
          </Text>
        )}
      </Card>

      <Card withBorder padding="lg">
        <Stack gap="md">
          <Title order={3} size="h5" className="flex items-center gap-2">
            <QrCode size={18} /> {t("attendance:selfCheckIn.qrTitle")}
          </Title>
          <Text size="sm" c="dimmed">
            {t("attendance:selfCheckIn.qrHint")}
          </Text>
          <Text size="xs" c="dimmed">
            {t("attendance:selfCheckIn.qrGeofenceHint")}
          </Text>
          <TextInput
            id="checkin-code"
            placeholder={t("attendance:selfCheckIn.qrCodePlaceholder")}
            value={code}
            onChange={(e) => setCode(e.currentTarget.value)}
            styles={{ input: { textTransform: "uppercase" } }}
          />
          <Button onClick={handleQrSubmit} loading={qrMutation.isPending} disabled={isBusy || !code.trim()}>
            {actionLabel}
          </Button>
        </Stack>
      </Card>

      <Card withBorder padding="lg">
        <Stack gap="md">
          <Title order={3} size="h5" className="flex items-center gap-2">
            <MapPin size={18} /> {t("attendance:selfCheckIn.gpsTitle")}
          </Title>
          <Text size="sm" c="dimmed">
            {t("attendance:selfCheckIn.gpsHint")}
          </Text>
          {geoError && (
            <Alert color="red" variant="light">
              {geoError}
            </Alert>
          )}
          <Button variant="light" onClick={handleGpsCheckIn} loading={gpsMutation.isPending} disabled={isBusy}>
            {t("attendance:selfCheckIn.gpsButton", { action: actionLabel })}
          </Button>
        </Stack>
      </Card>

      {lastResult && (
        <Alert color="green" variant="light">
          {t(lastResult.direction === "in" ? "attendance:selfCheckIn.lastCheckIn" : "attendance:selfCheckIn.lastCheckOut", {
            time: lastResult.time.slice(0, 5),
            location: lastResult.locationName ?? t("attendance:selfCheckIn.unknownLocation"),
          })}
        </Alert>
      )}
    </div>
  );
}
