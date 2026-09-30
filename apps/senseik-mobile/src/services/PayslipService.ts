import { File, Paths } from "expo-file-system";
import { apiClient } from "@/lib/apiClient";

/**
 * Bordro pusulası onay/itiraz ekranlarının (PayrollScreen, PayslipDetailScreen) servisi. Tek kaynak TK-030
 * "Bordro Pusulam" (`PayslipsController.cs`, `api/v1/payroll/payslips/my`, bkz. `PayrollService.ts`): liste,
 * "Okudum, onaylıyorum" (PayslipDelivery kanıtı, kanal "mobile") ve PDF; itiraz aynı grubun `my/{id}/object` ucunda.
 * `web/src/services/payslip-ack.service.ts` aynı uçları çağırır.
 */
const PAYSLIP_BASE = "/api/v1/payroll/payslips/my";

/** Bordro yalnız TRY. */
const PAYROLL_CURRENCY = "TRY";

/** `GET payroll/payslips/my` satırı (C# `MyPayslipDto`; null alanlar JSON'da yok - `WhenWritingNull`). */
interface MyPayslipResponse {
  payslipId: string;
  year: number;
  month: number;
  grossAmount: number;
  netAmount: number;
  createdAt: string;
  acknowledgedAt?: string | null;
  objectionStatus?: PayslipObjectionStatus | null;
  objectionReason?: string | null;
}

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

function toMyPayslip(row: MyPayslipResponse): MyPayslip {
  return {
    payslipId: row.payslipId,
    year: row.year,
    month: row.month,
    grossPay: row.grossAmount,
    netPay: row.netAmount,
    currency: PAYROLL_CURRENCY,
    // Liste yalnız çalışana açılmış (ödenmiş/kilitli dönem) pusulaları döndürür.
    publishedAt: row.createdAt,
    acknowledgedAt: row.acknowledgedAt ?? null,
    objectionStatus: row.objectionStatus ?? null,
    objectionReason: row.objectionReason ?? null
  };
}

export const payslipService = {
  async getMyPayslips(): Promise<MyPayslip[]> {
    const { data } = await apiClient.get<MyPayslipResponse[]>(PAYSLIP_BASE);
    return data.map(toMyPayslip);
  },

  /** "Okudum, onaylıyorum" (204, idempotent); güncel satırı (sunucu acknowledgedAt) listeden yeniden okur. */
  async acknowledge(payslipId: string): Promise<MyPayslip | null> {
    await apiClient.post(`${PAYSLIP_BASE}/${payslipId}/acknowledge`, { channel: "mobile" });
    const list = await payslipService.getMyPayslips();
    return list.find((item) => item.payslipId === payslipId) ?? null;
  },

  async object(payslipId: string, reason: string): Promise<void> {
    const trimmed = validateObjectionReason(reason);
    if (!trimmed) throw new Error("Itiraz gerekcesi zorunludur.");
    await apiClient.post(`${PAYSLIP_BASE}/${payslipId}/object`, { reason: trimmed });
  },

  /**
   * TK-030: downloads the pusula PDF (Bearer-protected, so through `apiClient`) to a cache file and
   * returns its `file://` URI for `expo-sharing`. Payroll PII - always pair with `deleteCachedDocument`.
   */
  async downloadDocument(payslipId: string, fileName: string): Promise<string> {
    const response = await apiClient.get<ArrayBuffer>(`${PAYSLIP_BASE}/${payslipId}/document`, {
      responseType: "arraybuffer"
    });
    const file = new File(Paths.cache, fileName);
    if (file.exists) file.delete();
    file.create();
    file.write(new Uint8Array(response.data));
    return file.uri;
  },

  /** Best-effort cache cleanup for a PDF written by `downloadDocument` - never throws. */
  deleteCachedDocument(fileUri: string): void {
    try {
      const file = new File(fileUri);
      if (file.exists) file.delete();
    } catch {
      // The OS reclaims cache space anyway; not worth surfacing to the user.
    }
  }
};
