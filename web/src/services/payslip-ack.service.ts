/**
 * Bordro pusulası onay/itiraz. Tek kaynak TK-030 "Bordro Pusulam" (`PayslipsController.cs`, `v1/payroll/payslips/my`):
 * liste + "Okudum, onaylıyorum" (PayslipDelivery kanıtı); itiraz (`PayslipObjection` onay tipi) aynı grubun
 * `my/{id}/object` ucunda. API yalnız ödenmiş/kilitli dönemlerin pusulasını listeler.
 */
import { apiClient } from "@senseik/api-client";

const BASE = "/v1/payroll/payslips/my";

export type PayslipObjectionStatus = "pending" | "accepted" | "rejected";

/** `GET payroll/payslips/my` satırı (C# `MyPayslipDto`, null alanlar gönderilmez). */
interface MyPayslipResponse {
  payslipId: string;
  year: number;
  month: number;
  grossAmount: number;
  netAmount: number;
  hasDocument?: boolean;
  createdAt: string;
  viewedAt?: string | null;
  acknowledgedAt?: string | null;
  objectionStatus?: PayslipObjectionStatus | null;
  objectionReason?: string | null;
  objectedAt?: string | null;
  objectionDecidedAt?: string | null;
}

export interface MyPayslip {
  payslipId: string;
  year: number;
  month: number;
  grossPay: number;
  netPay: number;
  currency?: string | null;
  /** Set once the payslip is opened to the employee; missing = not published. */
  publishedAt?: string | null;
  acknowledgedAt?: string | null;
  objectionStatus?: PayslipObjectionStatus | null;
  objectionReason?: string | null;
  objectionResolutionNote?: string | null;
}

/** Bordro yalnız TRY (BankPaymentFileBuilder ile aynı varsayım). */
const PAYROLL_CURRENCY = "TRY";

function toMyPayslip(row: MyPayslipResponse): MyPayslip {
  return {
    payslipId: row.payslipId,
    year: row.year,
    month: row.month,
    grossPay: row.grossAmount,
    netPay: row.netAmount,
    currency: PAYROLL_CURRENCY,
    // Listede yalnız çalışana açılmış (ödenmiş/kilitli dönem) pusulalar döner.
    publishedAt: row.createdAt,
    acknowledgedAt: row.acknowledgedAt ?? null,
    objectionStatus: row.objectionStatus ?? null,
    objectionReason: row.objectionReason ?? null,
  };
}

export async function listMyPayslips(): Promise<MyPayslip[]> {
  const { data } = await apiClient.get<MyPayslipResponse[]>(BASE);
  return data.map(toMyPayslip);
}

export async function acknowledgePayslip(payslipId: string): Promise<void> {
  await apiClient.post(`${BASE}/${payslipId}/acknowledge`, { channel: "web" });
}

export async function objectToPayslip(payslipId: string, reason: string): Promise<void> {
  await apiClient.post(`${BASE}/${payslipId}/object`, { reason });
}

/** Yalnız yayınlanmış, onaysız ve bekleyen itirazı olmayan bordroda aktif (sonuçlanan itirazdan sonra yeniden mümkün). */
export function canAcknowledge(payslip: MyPayslip): boolean {
  return (
    Boolean(payslip.publishedAt) && !payslip.acknowledgedAt && payslip.objectionStatus !== "pending"
  );
}

/** Backend sürüm başına tek itiraza izin verir: yayınlanmış, onaysız ve itirazı hiç olmayan bordroda aktif. */
export function canObject(payslip: MyPayslip): boolean {
  return Boolean(payslip.publishedAt) && !payslip.acknowledgedAt && !payslip.objectionStatus;
}
