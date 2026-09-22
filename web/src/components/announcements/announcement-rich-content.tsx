import { useMemo } from "react";
import DOMPurify from "dompurify";
import type { AnnouncementAttachment } from "@/services/announcements.service";

const ALLOWED_TAGS = [
  "p", "br", "strong", "b", "em", "i", "u", "s", "strike", "blockquote", "h1", "h2", "h3", "h4",
  "ul", "ol", "li", "a", "img", "table", "thead", "tbody", "tr", "td", "th", "span", "code", "pre",
];
const ALLOWED_ATTR = ["href", "src", "alt", "title", "data-attachment-id", "target", "rel", "style"];

/**
 * Renders a duyuru's rich-text content. The server already sanitizes on write (`AnnouncementContentSanitizer`); this
 * re-sanitizes on the client with the same allowlist (defense in depth - React security rule: never trust
 * `dangerouslySetInnerHTML` on a single layer alone) and, crucially, resolves every `<img data-attachment-id>`
 * placeholder to that attachment's current presigned download URL. The stored HTML never carries a permanent image
 * URL (those expire and are tenant/permission-checked at read time) - only the attachment id, resolved here from
 * whatever the caller already fetched (`listAnnouncementAttachments`/`listMyAnnouncementAttachments`).
 */
export function AnnouncementRichContent({ html, attachments = [] }: { html: string; attachments?: AnnouncementAttachment[] }) {
  const resolvedHtml = useMemo(() => resolveAttachmentSources(html, attachments), [html, attachments]);
  const sanitized = useMemo(
    () => DOMPurify.sanitize(resolvedHtml, { ALLOWED_TAGS, ALLOWED_ATTR, ALLOW_DATA_ATTR: true }),
    [resolvedHtml]
  );

  return <div className="announcement-rich-content" dangerouslySetInnerHTML={{ __html: sanitized }} />;
}

function resolveAttachmentSources(html: string, attachments: AnnouncementAttachment[]): string {
  if (!html.includes("data-attachment-id") || attachments.length === 0) {
    return html;
  }

  const byId = new Map(attachments.map((attachment) => [attachment.id, attachment]));
  const doc = new DOMParser().parseFromString(html, "text/html");
  doc.querySelectorAll("img[data-attachment-id]").forEach((img) => {
    const id = img.getAttribute("data-attachment-id");
    const attachment = id ? byId.get(id) : undefined;
    if (attachment) {
      img.setAttribute("src", attachment.downloadUrl);
    } else {
      img.remove();
    }
  });
  return doc.body.innerHTML;
}
