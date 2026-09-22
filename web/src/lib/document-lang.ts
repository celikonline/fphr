/** Mirrors the active i18n language onto `<html lang>` so screen readers and browser translation follow the UI language. */
export function applyDocumentLang(language: string | undefined, doc: Document = document): void {
  const primary = language?.split("-")[0];
  if (primary) doc.documentElement.lang = primary;
}
