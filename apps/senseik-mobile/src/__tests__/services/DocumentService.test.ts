import { File, UploadType } from "expo-file-system";
import { apiClient } from "@/lib/apiClient";
import {
  isDocumentFileSizeAllowed,
  MAX_DOCUMENT_FILE_SIZE_BYTES,
  uploadMyDocument,
} from "@/services/DocumentService";

jest.mock("@/lib/apiClient");

const mockUpload = jest.fn();
jest.mock("expo-file-system", () => ({
  // B11: `uploadMyDocument` now uploads via `new File(uri).upload(...)` instead of
  // `fetch(uri).blob()` - mock the `File` constructor to return a fixed `upload` spy so each test
  // can control/assert the upload call without touching the real filesystem.
  File: jest.fn().mockImplementation((uri: string) => ({ uri, upload: mockUpload })),
  UploadType: { BINARY_CONTENT: 0, MULTIPART: 1 },
}));

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
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("runs initiate -> upload the local file -> confirm, in that order, without ever reading it into a JS Blob", async () => {
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
    mockUpload.mockResolvedValue({ status: 200, body: "", headers: { ETag: '"server-etag"' } });

    const documentId = await uploadMyDocument(buildPickedFile());

    expect(documentId).toBe("doc-1");
    expect(File).toHaveBeenCalledWith("file:///tmp/rapor.pdf");
    expect(mockUpload).toHaveBeenCalledWith(
      "http://localhost:19000/documents/doc-1?signature=abc",
      {
        httpMethod: "PUT",
        uploadType: UploadType.BINARY_CONTENT,
        headers: { "Content-Type": "application/pdf" },
      }
    );
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

  it("uploads with no Authorization header (it is not our API) - only the explicit Content-Type", async () => {
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
    mockUpload.mockResolvedValue({ status: 200, body: "", headers: {} });

    await uploadMyDocument(buildPickedFile());

    const uploadOptions = mockUpload.mock.calls[0][1];
    expect(uploadOptions.headers).toEqual({ "Content-Type": "application/pdf" });
  });

  it("reads the ETag header case-insensitively", async () => {
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
    mockUpload.mockResolvedValue({ status: 200, body: "", headers: { etag: '"lowercase-etag"' } });

    await uploadMyDocument(buildPickedFile());

    expect(apiClient.post).toHaveBeenNthCalledWith(2, "/api/v1/documents/me/doc-1/confirm-upload", {
      eTag: '"lowercase-etag"',
      actualFileSizeBytes: 1024,
      expiryDate: null,
    });
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
    mockUpload.mockResolvedValue({ status: 200, body: "", headers: {} });

    await uploadMyDocument(buildPickedFile());

    expect(apiClient.post).toHaveBeenNthCalledWith(2, "/api/v1/documents/me/doc-1/confirm-upload", {
      eTag: "request-fallback",
      actualFileSizeBytes: 1024,
      expiryDate: null,
    });
  });

  it("throws and never calls confirm-upload when the storage upload returns a non-2xx status", async () => {
    (apiClient.post as jest.Mock).mockResolvedValueOnce({
      data: {
        documentId: "doc-1",
        presignedUrl: "http://localhost:19000/x",
        requestId: "request-1",
        expiresAt: "x",
      },
    });
    mockUpload.mockResolvedValue({ status: 500, body: "", headers: {} });

    await expect(uploadMyDocument(buildPickedFile())).rejects.toThrow("500");
    expect(apiClient.post).toHaveBeenCalledTimes(1);
  });

  it("propagates a rejection from the upload itself (e.g. the file could not be read)", async () => {
    (apiClient.post as jest.Mock).mockResolvedValueOnce({
      data: {
        documentId: "doc-1",
        presignedUrl: "http://localhost:19000/x",
        requestId: "request-1",
        expiresAt: "x",
      },
    });
    mockUpload.mockRejectedValue(new Error("Unable to read file"));

    await expect(uploadMyDocument(buildPickedFile())).rejects.toThrow("Unable to read file");
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
