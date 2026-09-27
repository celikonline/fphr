import { apiClient } from "@/lib/apiClient";
import {
  isDocumentFileSizeAllowed,
  MAX_DOCUMENT_FILE_SIZE_BYTES,
  uploadMyDocument,
} from "@/services/DocumentService";

jest.mock("@/lib/apiClient");

function buildPickedFile(overrides: Partial<Parameters<typeof uploadMyDocument>[0]> = {}) {
  return {
    uri: "file:///tmp/rapor.pdf",
    name: "rapor.pdf",
    mimeType: "application/pdf",
    size: 1024,
    ...overrides,
  };
}

describe("uploadMyDocument", () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    jest.clearAllMocks();
  });

  afterEach(() => {
    global.fetch = originalFetch;
  });

  it("runs initiate -> read local file -> PUT to storage -> confirm, in that order", async () => {
    (apiClient.post as jest.Mock)
      .mockResolvedValueOnce({
        data: {
          documentId: "doc-1",
          presignedUrl: "http://localhost:19000/documents/doc-1?signature=abc",
          requestId: "request-1",
          expiresAt: "2026-09-27T12:00:00Z",
        },
      })
      .mockResolvedValueOnce({
        data: { documentId: "doc-1", title: "rapor.pdf", uploadedAt: "2026-09-27T12:00:00Z" },
      });

    const blob = new Blob(["%PDF-1.4"], { type: "application/pdf" });
    const readLocalFile = jest.fn().mockResolvedValue({ blob: () => Promise.resolve(blob) });
    const putToStorage = jest.fn().mockResolvedValue({
      ok: true,
      status: 200,
      headers: { get: (name: string) => (name.toLowerCase() === "etag" ? '"server-etag"' : null) },
    });
    const calls: string[] = [];
    global.fetch = jest.fn(
      (input: Parameters<typeof fetch>[0], init?: Parameters<typeof fetch>[1]) => {
        if (!init) {
          calls.push("read-local-file");
          return readLocalFile(input);
        }
        calls.push("put-to-storage");
        return putToStorage(input, init);
      }
    ) as unknown as typeof fetch;

    const documentId = await uploadMyDocument(buildPickedFile());

    expect(documentId).toBe("doc-1");
    expect(calls).toEqual(["read-local-file", "put-to-storage"]);
    expect(apiClient.post).toHaveBeenNthCalledWith(1, "/api/v1/documents/me/initiate-upload", {
      fileName: "rapor.pdf",
      contentType: "application/pdf",
      fileSizeBytes: 1024,
      bucketName: "documents",
    });
    expect(apiClient.post).toHaveBeenNthCalledWith(2, "/api/v1/documents/me/doc-1/confirm-upload", {
      eTag: '"server-etag"',
      actualFileSizeBytes: 1024,
      expiryDate: null,
    });
  });

  it("PUTs to the presigned URL with no Authorization header (it is not our API)", async () => {
    (apiClient.post as jest.Mock)
      .mockResolvedValueOnce({
        data: {
          documentId: "doc-1",
          presignedUrl: "http://localhost:19000/x",
          requestId: "request-1",
          expiresAt: "x",
        },
      })
      .mockResolvedValueOnce({ data: {} });

    const blob = new Blob(["x"]);
    let putHeaders: NonNullable<Parameters<typeof fetch>[1]>["headers"];
    global.fetch = jest.fn(
      (input: Parameters<typeof fetch>[0], init?: Parameters<typeof fetch>[1]) => {
        if (!init) return Promise.resolve({ blob: () => Promise.resolve(blob) } as Response);
        putHeaders = init.headers;
        return Promise.resolve({
          ok: true,
          status: 200,
          headers: { get: () => null },
        } as unknown as Response);
      }
    ) as unknown as typeof fetch;

    await uploadMyDocument(buildPickedFile());

    expect(putHeaders).toEqual({ "Content-Type": "application/pdf" });
  });

  it("falls back to the initiate response's requestId when the storage response has no ETag header", async () => {
    (apiClient.post as jest.Mock)
      .mockResolvedValueOnce({
        data: {
          documentId: "doc-1",
          presignedUrl: "http://localhost:19000/x",
          requestId: "request-fallback",
          expiresAt: "x",
        },
      })
      .mockResolvedValueOnce({ data: {} });

    global.fetch = jest.fn(
      (_input: Parameters<typeof fetch>[0], init?: Parameters<typeof fetch>[1]) => {
        if (!init)
          return Promise.resolve({ blob: () => Promise.resolve(new Blob(["x"])) } as Response);
        return Promise.resolve({
          ok: true,
          status: 200,
          headers: { get: () => null },
        } as unknown as Response);
      }
    ) as unknown as typeof fetch;

    await uploadMyDocument(buildPickedFile());

    expect(apiClient.post).toHaveBeenNthCalledWith(2, "/api/v1/documents/me/doc-1/confirm-upload", {
      eTag: "request-fallback",
      actualFileSizeBytes: 1024,
      expiryDate: null,
    });
  });

  it("throws and never calls confirm-upload when the storage PUT fails", async () => {
    (apiClient.post as jest.Mock).mockResolvedValueOnce({
      data: {
        documentId: "doc-1",
        presignedUrl: "http://localhost:19000/x",
        requestId: "request-1",
        expiresAt: "x",
      },
    });

    global.fetch = jest.fn(
      (_input: Parameters<typeof fetch>[0], init?: Parameters<typeof fetch>[1]) => {
        if (!init)
          return Promise.resolve({ blob: () => Promise.resolve(new Blob(["x"])) } as Response);
        return Promise.resolve({
          ok: false,
          status: 500,
          headers: { get: () => null },
        } as unknown as Response);
      }
    ) as unknown as typeof fetch;

    await expect(uploadMyDocument(buildPickedFile())).rejects.toThrow("500");
    expect(apiClient.post).toHaveBeenCalledTimes(1);
  });
});

describe("isDocumentFileSizeAllowed", () => {
  it("allows a normal-sized file", () => {
    expect(isDocumentFileSizeAllowed(1024)).toBe(true);
  });

  it("allows exactly the server limit (100 MB)", () => {
    expect(isDocumentFileSizeAllowed(MAX_DOCUMENT_FILE_SIZE_BYTES)).toBe(true);
  });

  it("rejects a file over the server limit", () => {
    expect(isDocumentFileSizeAllowed(MAX_DOCUMENT_FILE_SIZE_BYTES + 1)).toBe(false);
  });

  it("rejects a zero-byte file", () => {
    expect(isDocumentFileSizeAllowed(0)).toBe(false);
  });
});
