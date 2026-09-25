/** Bordro pusulası onay/itiraz (payroll.my.payslip, `PayslipObjection` onay tipi). Skeleton DTO. */
import { apiClient } from "@senseik/api-client";

const BASE = "/v1/payroll/my/payslips";

export interface PayslipAcknowledgement {
  payslipId: string;
  acknowledgedAt?: string | null;
  objectionStatus?: string | null;
}

export async function acknowledgePayslip(payslipId: string): Promise<void> {
  await apiClient.post(`${BASE}/${payslipId}/acknowledge`);
}

export async function objectToPayslip(payslipId: string, reason: string): Promise<string> {
  const { data } = await apiClient.post<{ id: string }>(`${BASE}/${payslipId}/objection`, { reason });
  return data.id;
}
