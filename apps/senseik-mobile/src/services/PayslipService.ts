import { apiClient } from "@/lib/apiClient";

/**
 * Bordro pusulası onay/itiraz (zaman-devam E2), self-service: `api/v1/payroll/me/payslips`.
 * `web/src/services/payslip-ack.service.ts` aynı uçları çağırır.
 */
const PAYSLIP_BASE = "/api/v1/payroll/me/payslips";

/** Sunucu tarafında itiraz durumu (camelCase enum). */
export type PayslipObjectionStatus = "pending" | "accepted" | "rejected";

export interface MyPayslip {
  payslipId: string;
  year: number;
  month: number;
  grossPay: number;
  netPay: number;
  currency?: string | null;
  /** Bordro çalışana açıldığında dolar; boşsa henüz yayınlanmamıştır. */
  publishedAt?: string | null;
  acknowledgedAt?: string | null;
  objectionStatus?: PayslipObjectionStatus | null;
  objectionReason?: string | null;
  objectionResolutionNote?: string | null;
}

/**
 * Onayla yalnız yayınlanmış, daha önce onaylanmamış ve bekleyen itirazı olmayan bordroda aktiftir
 * (itiraz sonuçlanınca - accepted/rejected - yeniden mümkün).
 */
export function canAcknowledge(payslip: MyPayslip): boolean {
  return (
    Boolean(payslip.publishedAt) && !payslip.acknowledgedAt && payslip.objectionStatus !== "pending"
  );
}

/** Backend sürüm başına tek itiraza izin verir: yayınlanmış, onaysız ve itirazı hiç olmayan bordroda aktif. */
export function canObject(payslip: MyPayslip): boolean {
  return Boolean(payslip.publishedAt) && !payslip.acknowledgedAt && !payslip.objectionStatus;
}

/** İtirazda gerekçe zorunlu; boş/boşluk gerekçe reddedilir. Geçerliyse kırpılmış gerekçeyi döndürür. */
export function validateObjectionReason(reason: string): string | null {
  const trimmed = reason.trim();
  return trimmed.length > 0 ? trimmed : null;
}

export const payslipService = {
  async getMyPayslips(): Promise<MyPayslip[]> {
    const { data } = await apiClient.get<MyPayslip[]>(PAYSLIP_BASE);
    return data;
  },

  /** Güncel satırı döndürür (acknowledgedAt sunucu zamanı); yanıtta satır yoksa listeyi yeniden çeker. */
  async acknowledge(payslipId: string): Promise<MyPayslip | null> {
    const { data } = await apiClient.post<MyPayslip | "" | null>(
      `${PAYSLIP_BASE}/${payslipId}/acknowledge`
    );
    if (data && typeof data === "object" && data.acknowledgedAt) return data;
    const list = await payslipService.getMyPayslips();
    return list.find((item) => item.payslipId === payslipId) ?? null;
  },

  async object(payslipId: string, reason: string): Promise<void> {
    const trimmed = validateObjectionReason(reason);
    if (!trimmed) throw new Error("Itiraz gerekcesi zorunludur.");
    await apiClient.post(`${PAYSLIP_BASE}/${payslipId}/object`, { reason: trimmed });
  }
};
