import { useEffect, useState } from "react";
import { Alert, Button, Group, Text } from "@mantine/core";
import { Timer } from "lucide-react";
import { apiClient } from "@senseik/api-client";
import { useAuthStore } from "@/store/auth.store";

interface LicenseResponse { status: string; plan: string; employeeLimit: number; expiresAt: string }
interface License extends LicenseResponse { observedAt: number }

/** Sales context remains visible while the customer evaluates the actual HR product. */
export function CommercialLicenseBanner() {
  const [license, setLicense] = useState<License | null>(null);
  const token = useAuthStore((state) => state.token);
  useEffect(() => {
    if (!token) return;
    let cancelled = false;
    void apiClient.get<LicenseResponse | null>("/v1/identity/commercial/mine").then(({ data }) => {
      if (!cancelled) setLicense(data ? { ...data, observedAt: Date.now() } : null);
    }).catch(() => {});
    return () => { cancelled = true; };
  }, [token]);
  if (!license) return null;
  const days = Math.max(0, Math.ceil((Date.parse(license.expiresAt) - license.observedAt) / 86400000));
  const salesUrl = import.meta.env.VITE_SALES_URL || "http://localhost:4173";
  return (
    <Alert color="violet" icon={<Timer size={18} />} mb="md" radius="md">
      <Group justify="space-between" wrap="wrap">
        <Text size="sm">{license.status === "trial" ? `SenseHR demo · ${days} gün kaldı` : "SenseHR lisansınız aktif"} · {license.employeeLimit} çalışan kapasitesi</Text>
        <Button component="a" href={`${salesUrl}/giris`} size="xs" variant="light">Paket & lisans</Button>
      </Group>
    </Alert>
  );
}
