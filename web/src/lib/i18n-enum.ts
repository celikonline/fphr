/**
 * Resolves a backend enum value to its localized label without ever leaking a raw i18next key
 * (`ns:path.X`) into the UI.
 *
 * Root cause this works around: some backend DTOs call `.ToString()` on a C# enum before putting it
 * on the response (e.g. `Sense.Modules.Recruitment.Application/Candidates/CandidateQueries.cs`), which
 * serializes it PascalCase (`"Manual"`) instead of leaving the field typed as the enum itself, which
 * would let the global `JsonStringEnumConverter(CamelCase)` produce `"manual"`. Locale files key enum
 * labels in camelCase, so a naive `t(\`${key}.${raw}\`)` on the PascalCase value misses and i18next
 * returns the untranslated key string. This tries the raw value, then its camelCase (lowerFirst)
 * variant, and only as a last resort humanizes the raw value — it never returns the `ns:path.X` shape.
 *
 * @param t translation function (namespaced so `key` resolves against the right bundle)
 * @param key the i18n key prefix that owns the enum's labels, e.g. `"recruitment:candidates.sources"`
 * @param raw the raw enum value from the API, e.g. `"Manual"` or `"manual"`
 */
export function enumLabel(t: (key: string, options?: Record<string, unknown>) => string, key: string, raw: string | null | undefined): string {
  if (!raw) return "";

  const sentinel = `__i18n_enum_missing__${key}.${raw}__`;

  const direct = t(`${key}.${raw}`, { defaultValue: sentinel });
  if (direct !== sentinel) return direct;

  const camel = lowerFirst(raw);
  if (camel !== raw) {
    const viaCamel = t(`${key}.${camel}`, { defaultValue: sentinel });
    if (viaCamel !== sentinel) return viaCamel;
  }

  return humanize(raw);
}

function lowerFirst(value: string): string {
  return value.length === 0 ? value : value[0].toLowerCase() + value.slice(1);
}

/** Last-resort fallback: "JobSearch" / "job_search" -> "Job Search". Never produces an i18n key shape. */
function humanize(value: string): string {
  const spaced = value
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[_-]+/g, " ")
    .trim();
  if (spaced.length === 0) return value;
  return spaced
    .split(" ")
    .map((word) => (word.length === 0 ? word : word[0].toUpperCase() + word.slice(1)))
    .join(" ");
}
