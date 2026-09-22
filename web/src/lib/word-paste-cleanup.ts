/**
 * Best-effort cleanup of HTML pasted from Microsoft Word/Outlook, run through Tiptap's `transformPastedHTML`
 * before ProseMirror's own paste parser runs. Word's clipboard HTML already carries real semantic tags (`<b>`,
 * `<i>`, `<ul>`, `<h1>`...) that ProseMirror parses correctly on its own; what it does NOT clean up is Office's own
 * noise - conditional comments, `mso-*` inline styles, `<o:p>` markers and `class="Mso..."` - which would otherwise
 * survive into the sanitized content as empty spans and stray whitespace. This does not attempt full docx fidelity
 * (that needs a real docx→HTML converter); it only strips what would look broken.
 */
export function cleanPastedWordHtml(html: string): string {
  return html
    // Conditional comments: <!--[if gte mso 9]>...<![endif]-->
    .replace(/<!--\[if [^\]]*\]>[\s\S]*?<!\[endif\]-->/gi, "")
    .replace(/<!--[\s\S]*?-->/g, "")
    // Word's own namespaced empty paragraph markers.
    .replace(/<o:p>\s*<\/o:p>/gi, "")
    .replace(/<\/?o:p>/gi, "")
    // mso-* inline styles (keep the style attribute if something else useful remains).
    .replace(/style="([^"]*)"/gi, (_match, styleValue: string) => {
      const kept = styleValue
        .split(";")
        .map((rule) => rule.trim())
        .filter((rule) => rule.length > 0 && !rule.toLowerCase().startsWith("mso-"))
        .join("; ");
      return kept.length > 0 ? `style="${kept}"` : "";
    })
    // Word's Mso* class names (e.g. class="MsoNormal").
    .replace(/\sclass="Mso[^"]*"/gi, "")
    // Collapse the empty tag soup the steps above can leave behind.
    .replace(/<span[^>]*>\s*<\/span>/gi, "")
    .replace(/<p[^>]*>(\s|&nbsp;)*<\/p>/gi, "");
}
