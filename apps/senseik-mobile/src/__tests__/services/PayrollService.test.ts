import { apiClient } from "@/lib/apiClient";
import { payrollService } from "@/services/PayrollService";

jest.mock("@/lib/apiClient");

const mockCreatedFiles: Array<{
  uri: string;
  exists: boolean;
  create: jest.Mock;
  write: jest.Mock;
  delete: jest.Mock;
}> = [];

/** Simulates the real filesystem's persistence across separate `new File(...)` instances for the same uri. */
const mockExistingUris = new Set<string>();

jest.mock("expo-file-system", () => ({
  File: jest.fn().mockImplementation((...args: unknown[]) => {
    const uri = args.length > 1 ? `${args[0]}/${args[1]}` : String(args[0]);
    const instance = {
      uri,
      get exists() {
        return mockExistingUris.has(uri);
      },
      create: jest.fn(() => mockExistingUris.add(uri)),
      write: jest.fn(),
      delete: jest.fn(() => mockExistingUris.delete(uri)),
    };
    mockCreatedFiles.push(instance);
    return instance;
  }),
  Paths: { cache: "file:///cache" },
}));

describe("PayrollService", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockCreatedFiles.length = 0;
    mockExistingUris.clear();
  });

  describe("getMyPayslips", () => {
    it("fetches the caller's own payslips and defaults a missing viewedAt/acknowledgedAt to null", async () => {
      const mockResponse = {
        data: [
          {
            payslipId: "payslip-1",
            year: 2026,
            month: 9,
            grossAmount: 60000,
            netAmount: 45000,
            hasDocument: true,
            createdAt: "2026-09-27T08:00:00Z",
            // viewedAt/acknowledgedAt intentionally absent - the API omits them (WhenWritingNull) when null.
          },
        ],
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await payrollService.getMyPayslips();

      expect(result).toEqual([
        { ...mockResponse.data[0], viewedAt: null, acknowledgedAt: null },
      ]);
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/payroll/payslips/my");
    });

    it("passes through a present viewedAt/acknowledgedAt instead of overwriting it with null", async () => {
      const mockResponse = {
        data: [
          {
            payslipId: "payslip-1",
            year: 2026,
            month: 9,
            grossAmount: 60000,
            netAmount: 45000,
            hasDocument: true,
            createdAt: "2026-09-27T08:00:00Z",
            viewedAt: "2026-09-27T09:00:00Z",
            acknowledgedAt: "2026-09-27T09:05:00Z",
          },
        ],
      };
      (apiClient.get as jest.Mock).mockResolvedValue(mockResponse);

      const result = await payrollService.getMyPayslips();

      expect(result[0].viewedAt).toBe("2026-09-27T09:00:00Z");
      expect(result[0].acknowledgedAt).toBe("2026-09-27T09:05:00Z");
    });

    it("propagates a 403/404 instead of returning fabricated data - the screen must show no payslips on failure", async () => {
      const apiError = { response: { status: 403 } };
      (apiClient.get as jest.Mock).mockRejectedValue(apiError);

      await expect(payrollService.getMyPayslips()).rejects.toEqual(apiError);
    });
  });

  describe("acknowledgePayslip", () => {
    it("posts the mobile channel to the acknowledge endpoint for exactly the given payslipId", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({ status: 204 });

      await payrollService.acknowledgePayslip("payslip-1");

      expect(apiClient.post).toHaveBeenCalledWith(
        "/api/v1/payroll/payslips/my/payslip-1/acknowledge",
        { channel: "mobile" }
      );
    });

    it("propagates a 404 (another employee's payslip or a not-yet-visible period)", async () => {
      const apiError = { response: { status: 404, data: { code: "payroll.payslip.not_found" } } };
      (apiClient.post as jest.Mock).mockRejectedValue(apiError);

      await expect(payrollService.acknowledgePayslip("someone-elses-id")).rejects.toEqual(apiError);
    });
  });

  describe("downloadMyPayslipDocument", () => {
    it("fetches the PDF as raw bytes via apiClient (Bearer-protected, not a presigned URL) using exactly the given payslipId", async () => {
      const bytes = new Uint8Array([0x25, 0x50, 0x44, 0x46]); // "%PDF"
      (apiClient.get as jest.Mock).mockResolvedValue({ data: bytes.buffer });

      const uri = await payrollService.downloadMyPayslipDocument("payslip-1", "bordro-2026-09.pdf");

      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/payroll/payslips/my/payslip-1/document", {
        responseType: "arraybuffer",
      });
      expect(uri).toBe("file:///cache/bordro-2026-09.pdf");
      const file = mockCreatedFiles[0];
      expect(file.create).toHaveBeenCalledTimes(1);
      expect(file.write).toHaveBeenCalledWith(new Uint8Array(bytes.buffer));
    });

    it("never constructs a URL from anything other than the given payslipId (no employeeId/other id accepted)", async () => {
      (apiClient.get as jest.Mock).mockResolvedValue({ data: new ArrayBuffer(0) });

      await payrollService.downloadMyPayslipDocument("only-this-id", "f.pdf");

      const [url] = (apiClient.get as jest.Mock).mock.calls[0];
      expect(url).toBe("/api/v1/payroll/payslips/my/only-this-id/document");
      expect(url).not.toContain("employee");
    });

    it("deletes a pre-existing cache file with the same name before writing (idempotent re-download)", async () => {
      (apiClient.get as jest.Mock).mockResolvedValue({ data: new ArrayBuffer(0) });

      // First call creates the file on the simulated filesystem (see `mockExistingUris`).
      await payrollService.downloadMyPayslipDocument("payslip-1", "bordro-2026-09.pdf");
      // Second call for the same name must see it as already existing and delete it first.
      const uri = await payrollService.downloadMyPayslipDocument("payslip-1", "bordro-2026-09.pdf");

      expect(mockCreatedFiles[1].delete).toHaveBeenCalledTimes(1);
      expect(uri).toBe("file:///cache/bordro-2026-09.pdf");
    });

    it("propagates a 404 (payroll.payslip.not_found - another employee's id or a not-yet-visible period) without writing any file", async () => {
      const apiError = { response: { status: 404, data: { code: "payroll.payslip.not_found" } } };
      (apiClient.get as jest.Mock).mockRejectedValue(apiError);

      await expect(
        payrollService.downloadMyPayslipDocument("someone-elses-id", "f.pdf")
      ).rejects.toEqual(apiError);
      expect(mockCreatedFiles).toHaveLength(0);
    });
  });

  describe("deleteCachedPayslipDocument", () => {
    it("deletes an existing cached file", () => {
      mockExistingUris.add("file:///cache/bordro-2026-09.pdf");

      payrollService.deleteCachedPayslipDocument("file:///cache/bordro-2026-09.pdf");

      expect(mockCreatedFiles[0].delete).toHaveBeenCalledTimes(1);
      expect(mockExistingUris.has("file:///cache/bordro-2026-09.pdf")).toBe(false);
    });

    it("does nothing when the file does not exist", () => {
      payrollService.deleteCachedPayslipDocument("file:///cache/never-existed.pdf");

      expect(mockCreatedFiles[0].delete).not.toHaveBeenCalled();
    });

    it("never throws when deletion itself fails", () => {
      mockExistingUris.add("file:///cache/gone.pdf");
      const File = jest.requireMock("expo-file-system").File as jest.Mock;
      File.mockImplementationOnce((uri: string) => ({
        uri,
        exists: true,
        create: jest.fn(),
        write: jest.fn(),
        delete: jest.fn(() => {
          throw new Error("already gone");
        }),
      }));

      expect(() => payrollService.deleteCachedPayslipDocument("file:///cache/gone.pdf")).not.toThrow();
    });
  });
});
