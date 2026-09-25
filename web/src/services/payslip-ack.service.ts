/** Bordro pusulası onay/itiraz (payroll.my.payslip, `PayslipObjection` onay tipi). `v1/payroll/me/payslips`. */
import { apiClient } from "@senseik/api-client";

const BASE = "/v1/payroll/me/payslips";

export type PayslipObjectionStatus = "pending" | "accepted" | "rejected";

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

export async function listMyPayslips(): Promise<MyPayslip[]> {
  const { data } = await apiClient.get<MyPayslip[]>(BASE);
  return data;
}

export async function acknowledgePayslip(payslipId: string): Promise<void> {
  await apiClient.post(`${BASE}/${payslipId}/acknowledge`);
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
