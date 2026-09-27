import { apiClient } from "@/lib/apiClient";

/**
 * Documents module (self-service upload only): mirrors `DocumentsController.cs`'s `/documents/me`
 * group (`POST me/initiate-upload`, `POST me/{id}/confirm-upload`) - the same three-step presigned
 * URL flow `web/src/services/documents.service.ts`'s `uploadDocumentFile(file, "documents", "me")`
 * uses. Only self-service upload (to attach to the caller's own leave request, senseik-051 D2/B-mobil-004);
 * document listing/download/delete and the HR-scoped (`documents.files.create`) endpoints stay web-only.
 */
const DOCUMENTS_BASE = "/api/v1/documents";

/** Mirrors `DocumentsDomainErrors.MaxFileSize` (100 MB) - checked client-side to fail fast with a Turkish message instead of a raw server rejection. */
export const MAX_DOCUMENT_FILE_SIZE_BYTES = 100 * 1024 * 1024;

export function isDocumentFileSizeAllowed(sizeBytes: number): boolean {
  return sizeBytes > 0 && sizeBytes <= MAX_DOCUMENT_FILE_SIZE_BYTES;
}

/** What a picked camera photo or file (expo-image-picker / expo-document-picker asset) needs to carry for upload. */
export interface PickedDocumentFile {
  uri: string;
  name: string;
  mimeType: string;
  size: number;
}

/** `InitiateUploadResponse`. */
interface InitiateUploadResult {
  documentId: string;
  presignedUrl: string;
  requestId: string;
  expiresAt: string;
}

/**
 * Uploads a file through the Documents module's presigned-URL flow (initiate -> PUT bytes directly
 * to storage -> confirm) and returns the new Document's id, same three steps as the web's
 * `uploadDocumentFile`. Step 2 deliberately uses plain RN `fetch`, not `apiClient` or axios: the
 * presigned URL points at object storage, not our API, so it must never carry the Bearer token or
 * our `X-*` headers (apiClient's interceptors would attach both); `fetch` is also what Expo's own
 * docs use for local `file://` uploads and - unlike axios's XHR adapter in React Native - reliably
 * supports a `Blob` request body.
 */
export async function uploadMyDocument(
  file: PickedDocumentFile,
  bucketName = "documents"
): Promise<string> {
  const contentType = file.mimeType || "application/octet-stream";
  const { data: initiated } = await apiClient.post<InitiateUploadResult>(
    `${DOCUMENTS_BASE}/me/initiate-upload`,
    {
      fileName: file.name,
      contentType,
      fileSizeBytes: file.size,
      bucketName,
    }
  );

  const fileBlob = await (await fetch(file.uri)).blob();
  const uploadResponse = await fetch(initiated.presignedUrl, {
    method: "PUT",
    headers: { "Content-Type": contentType },
    body: fileBlob,
  });
  if (!uploadResponse.ok) {
    // Never log the presigned URL (it carries a signature) or any response body.
    throw new Error(`Document upload to storage failed with status ${uploadResponse.status}`);
  }
  const eTag = uploadResponse.headers.get("etag") ?? initiated.requestId;

  await apiClient.post(`${DOCUMENTS_BASE}/me/${initiated.documentId}/confirm-upload`, {
    eTag,
    actualFileSizeBytes: file.size,
    expiryDate: null,
  });

  return initiated.documentId;
}
