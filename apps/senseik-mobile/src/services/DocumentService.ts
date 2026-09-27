import { File, UploadType } from "expo-file-system";
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

/** Case-insensitive header lookup - `UploadResult.headers` keys' casing isn't guaranteed across platforms/servers. */
function findHeader(headers: Record<string, string>, name: string): string | undefined {
  const key = Object.keys(headers).find((k) => k.toLowerCase() === name.toLowerCase());
  return key ? headers[key] : undefined;
}

/**
 * Uploads a file through the Documents module's presigned-URL flow (initiate -> PUT bytes directly
 * to storage -> confirm) and returns the new Document's id, same three steps as the web's
 * `uploadDocumentFile`.
 *
 * Step 2 uses `expo-file-system`'s `File#upload` (already a dependency, `~57.0.7` - no new native
 * module, no dev-client rebuild) instead of `fetch(file.uri).blob()`: RN's `fetch().blob()` on
 * Android copies the whole file into React Native's Blob store and reads it back through base64
 * encoding to hand it to the JS layer, which is slow and memory-heavy for large files (observed
 * live as an Expo `Response.blob()` performance warning on a report PDF - B11). `File#upload`
 * streams the file straight from disk to the presigned URL natively; the bytes never pass through
 * JS as a Blob/ArrayBuffer/base64 string.
 *
 * The alternative the same warning suggests, `expo-blob`, is a *different* native module (not
 * currently a dependency) that would still need `expo install` + a dev-client rebuild before this
 * could ship; `expo-file-system`'s `File` API is already linked into the compiled dev client (it's
 * been a dependency since the app's initial scaffold) and needs no rebuild at all, and its
 * `File#upload` never touches JS-side Blob/base64 either - strictly the better fit here.
 *
 * `File#upload` never carries the Bearer token or our `X-*` headers (unlike `apiClient`/axios) -
 * the presigned URL points at object storage, not our API, so those must never be attached; only
 * the explicit `Content-Type` header below is sent.
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

  const uploadResult = await new File(file.uri).upload(initiated.presignedUrl, {
    httpMethod: "PUT",
    uploadType: UploadType.BINARY_CONTENT,
    headers: { "Content-Type": contentType },
  });
  if (uploadResult.status < 200 || uploadResult.status >= 300) {
    // Never log the presigned URL (it carries a signature) or the response body.
    throw new Error(`Document upload to storage failed with status ${uploadResult.status}`);
  }
  const eTag = findHeader(uploadResult.headers, "etag") ?? initiated.requestId;

  await apiClient.post(`${DOCUMENTS_BASE}/me/${initiated.documentId}/confirm-upload`, {
    eTag,
    actualFileSizeBytes: file.size,
    expiryDate: null,
  });

  return initiated.documentId;
}
