import { useTranslation } from "react-i18next";

export type StoreKind = "ios" | "android";

/** Simplified, single-color glyphs (not the exact trademarked logos) - just enough to read as "App Store" / "Google Play". */
function AppleGlyph() {
  return (
    <svg viewBox="0 0 24 24" width={22} height={22} fill="currentColor" aria-hidden="true">
      <path d="M16.4 1.5c.1 1-.3 2-.9 2.7-.6.7-1.6 1.3-2.6 1.2-.1-1 .4-2 .9-2.6.6-.7 1.6-1.2 2.6-1.3ZM19.9 17c-.4 1-.9 1.9-1.6 2.8-.9 1.2-1.8 2.4-3.2 2.5-1.3 0-1.7-.8-3.2-.8s-2 .8-3.2.8c-1.3 0-2.3-1.3-3.3-2.6-1.8-2.5-3.2-7-1.3-10 .9-1.5 2.6-2.4 4.3-2.5 1.3 0 2.5.9 3.2.9.7 0 2.2-1.1 3.7-.9.6 0 2.4.2 3.6 1.9-.1.1-2.1 1.3-2.1 3.7 0 2.9 2.5 3.9 2.6 4.1Z" />
    </svg>
  );
}

function PlayGlyph() {
  return (
    <svg viewBox="0 0 24 24" width={20} height={20} fill="currentColor" aria-hidden="true">
      <path d="M5 3.5v17c0 .5.5.8 1 .6l14.4-8.5c.5-.3.5-1 0-1.2L6 3c-.5-.3-1 .1-1 .5Z" />
    </svg>
  );
}

interface StoreButtonProps {
  store: StoreKind;
  /** Validated https URL, or null when the store link is not configured yet. */
  url: string | null;
}

/**
 * App Store / Google Play download button. When the url is not set: renders disabled, top line "Yakında",
 * bold line the store name; "İndir" only appears once the store link is configured.
 */
export function StoreButton({ store, url }: StoreButtonProps) {
  const { t } = useTranslation(["mobileApp"]);
  const isIos = store === "ios";
  const storeName = t(isIos ? "mobileApp:store.iosStoreName" : "mobileApp:store.androidStoreName");
  const line1 = url
    ? t(isIos ? "mobileApp:store.iosLine1" : "mobileApp:store.androidLine1")
    : t("mobileApp:store.comingSoon");
  const line2 = url ? t("mobileApp:store.download") : storeName;

  const content = (
    <>
      {isIos ? <AppleGlyph /> : <PlayGlyph />}
      <span className="flex flex-col leading-tight">
        <span className="text-[11px] opacity-80">{line1}</span>
        <span className="text-sm font-semibold">{line2}</span>
      </span>
    </>
  );

  const className =
    "flex items-center gap-2 rounded-xl bg-black px-4 py-2 text-white transition-opacity" +
    (url ? " hover:opacity-90" : " opacity-55");

  if (!url) {
    return (
      <button type="button" disabled aria-disabled="true" className={className}>
        {content}
      </button>
    );
  }

  return (
    <a href={url} target="_blank" rel="noopener noreferrer" className={className}>
      {content}
    </a>
  );
}
