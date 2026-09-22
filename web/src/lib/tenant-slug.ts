/** Tenant slug rules — mirror BE `Tenant.IsValidSlug` (IdentityLimits: 3-40 chars, [a-z0-9-], no leading/trailing "-"). */
export const TENANT_SLUG_MIN_LENGTH = 3;
export const TENANT_SLUG_MAX_LENGTH = 40;

const SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

const TURKISH_ASCII: Readonly<Record<string, string>> = {
  ç: "c",
  ğ: "g",
  ı: "i",
  ö: "o",
  ş: "s",
  ü: "u",
};

export function isValidTenantSlug(slug: string): boolean {
  return (
    slug.length >= TENANT_SLUG_MIN_LENGTH &&
    slug.length <= TENANT_SLUG_MAX_LENGTH &&
    SLUG_PATTERN.test(slug)
  );
}

/** Suggests a slug from a company name: Turkish letters folded to ASCII, everything else non-alphanumeric becomes "-". */
export function suggestTenantSlug(companyName: string): string {
  const folded = companyName
    .toLocaleLowerCase("tr-TR")
    .replace(/[çğıöşü]/g, (char) => TURKISH_ASCII[char] ?? char)
    .normalize("NFKD")
    .replace(/[̀-ͯ]/g, "");

  return folded
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, TENANT_SLUG_MAX_LENGTH)
    .replace(/-+$/g, "");
}
