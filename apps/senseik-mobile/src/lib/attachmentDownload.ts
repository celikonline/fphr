import * as WebBrowser from "expo-web-browser";
import i18n from "@/lib/i18n";

/**
 * senseik-mobile-011/U1: opens a leave request attachment via its presigned `download-url` (15
 * minute expiry). Shared by the approver's inbox card (`ApprovalsScreen`) and the employee's own
 * request list (`LeaveRequestListItem`) - only the `fetchDownloadUrl` call differs (manager-scoped
 * vs `/me`). `fetchDownloadUrl` is called fresh on every tap and its result is never stored in
 * component state or logged - the URL carries a signature, same rule as `DocumentService.ts`'s
 * upload presigned URL.
 *
 * U1: uses `expo-web-browser`'s `openBrowserAsync` (Android Custom Tabs / iOS
 * `SFSafariViewController`) instead of React Native core's `Linking.openURL`. `Linking.openURL`
 * handed the URL to the full external browser app, kicking the user out of SenseİK entirely (and,
 * observed live, could even land on Chrome's first-run/account screen instead of the PDF) -
 * `openBrowserAsync` opens an in-app browser tab that stays layered over the app and still renders
 * PDFs/images fine. This needs `expo-web-browser` compiled into the native dev-client build (added
 * to `package.json`/`app.json`'s plugins via `expo install`); the coordinator is rebuilding the dev
 * client for this.
 */
export async function openLeaveAttachment(
  fetchDownloadUrl: () => Promise<{ url: string }>
): Promise<void> {
  const { url } = await fetchDownloadUrl();
  await WebBrowser.openBrowserAsync(url);
}

/** Generic message (active language) for any attachment-open failure (403/404/network) - no error-specific text. */
export function attachmentOpenErrorMessage(): string {
  return i18n.t("errors:attachmentOpen");
}

/** Double-tap guard: while `openingDocumentId` is set, every attachment row's tap is ignored. */
export function shouldIgnoreAttachmentTap(openingDocumentId: string | null): boolean {
  return openingDocumentId !== null;
}
