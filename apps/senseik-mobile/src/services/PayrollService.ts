import { File, Paths } from "expo-file-system";
import { apiClient } from "@/lib/apiClient";

/**
 * TK-030: "Bordro Pusulam" - mirrors `PayslipsController.cs`'s `payroll/payslips/my` group
 * (`GET my`, `GET my/{id}/document`, `POST my/{id}/acknowledge`). Only the caller's own payslips, and
 * only for periods already Paid/Locked (server-enforced - `PayslipVisibility.IsVisibleToEmployee`);
 * the HR-facing employee/{id}/payslips flow is out of scope.
 */
const PAYROLL_BASE = "/api/v1/payroll/payslips";

/**
 * `MyPayslipDto`. `viewedAt`/`acknowledgedAt` are omitted by the API (`WhenWritingNull`) until the
 * employee has downloaded / acknowledged this payslip - missing means null, never "unknown".
 */
export interface MyPayslip {
  payslipId: string;
  year: number;
  month: number;
  grossAmount: number;
  netAmount: number;
  hasDocument: boolean;
  createdAt: string;
  viewedAt: string | null;
  acknowledgedAt: string | null;
}

/** Raw API shape: `viewedAt`/`acknowledgedAt` are absent from the JSON entirely when null (`WhenWritingNull`). */
type MyPayslipResponse = Omit<MyPayslip, "viewedAt" | "acknowledgedAt"> & {
  viewedAt?: string;
  acknowledgedAt?: string;
};

export class PayrollService {
  async getMyPayslips(): Promise<MyPayslip[]> {
    const response = await apiClient.get<MyPayslipResponse[]>(`${PAYROLL_BASE}/my`);
    return response.data.map((payslip) => ({
      ...payslip,
      viewedAt: payslip.viewedAt ?? null,
      acknowledgedAt: payslip.acknowledgedAt ?? null,
    }));
  }

  /**
   * "Okudum, onaylıyorum" (TK-030 kabul kriteri 2). Idempotent - a second call for an already
   * acknowledged payslip still returns 204 and does not change the original `acknowledgedAt`.
   * Always sent as the "mobile" channel, matching `PayslipDeliveryChannel`.
   */
  async acknowledgePayslip(payslipId: string): Promise<void> {
    await apiClient.post(`${PAYROLL_BASE}/my/${payslipId}/acknowledge`, { channel: "mobile" });
  }

  /**
   * Downloads one payslip's PDF to a local cache file and returns its `file://` URI, ready to hand
   * to `expo-sharing`'s `shareAsync` (see `PayrollScreen.tsx`). The document endpoint is
   * Bearer-protected (not a presigned storage URL like the Leave/Documents attachments), so it must
   * go through `apiClient` - which attaches the token via its request interceptor - rather than
   * `expo-file-system`'s own `downloadFileAsync`/`createDownloadTask`, which would need the header
   * passed manually and doesn't share `apiClient`'s token-refresh interceptor.
   *
   * `payslipId` must be one the caller already got back from `getMyPayslips()` - this method never
   * accepts or constructs an id from anywhere else, and the server itself re-checks ownership on
   * every call (any other employee's id, or a real id under a not-yet-visible period, gets the same
   * 404 `payroll.payslip.not_found` back).
   *
   * The PDF bytes are written straight to disk and never logged; the returned file is payroll PII
   * left on local storage until the caller removes it - always pair this with
   * `deleteCachedPayslipDocument` once the user is done viewing it.
   */
  async downloadMyPayslipDocument(payslipId: string, fileName: string): Promise<string> {
    const response = await apiClient.get<ArrayBuffer>(`${PAYROLL_BASE}/my/${payslipId}/document`, {
      responseType: "arraybuffer",
    });
    const file = new File(Paths.cache, fileName);
    if (file.exists) {
      file.delete();
    }
    file.create();
    file.write(new Uint8Array(response.data));
    return file.uri;
  }

  /** Best-effort cache cleanup for a payslip PDF written by `downloadMyPayslipDocument` - never throws. */
  deleteCachedPayslipDocument(fileUri: string): void {
    try {
      const file = new File(fileUri);
      if (file.exists) {
        file.delete();
      }
    } catch {
      // Cache cleanup failing isn't worth surfacing to the user - the OS will reclaim cache space anyway.
    }
  }
}

export const payrollService = new PayrollService();
