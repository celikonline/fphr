import { describe, expect, it } from "vitest";

const BASE = "tr";
const TARGETS = ["en", "de"] as const;

type Json = Record<string, unknown>;

// Keyed by "<locale>/<namespace>.json"; Vite resolves the glob at build time.
const bundles = Object.fromEntries(
  Object.entries(
    import.meta.glob<Json>("../public/locales/*/*.json", { eager: true, import: "default" }),
  ).map(([file, value]) => [file.split("/").slice(-2).join("/"), value]),
);

function read(locale: string, namespace: string): Json {
  const bundle = bundles[`${locale}/${namespace}`];
  if (!bundle) throw new Error(`Missing locale bundle: ${locale}/${namespace}`);
  return bundle;
}

function flatten(value: Json, prefix = ""): Array<[string, unknown]> {
  return Object.entries(value).flatMap(([key, child]) => {
    const full = prefix ? `${prefix}.${key}` : key;
    return child !== null && typeof child === "object" && !Array.isArray(child)
      ? flatten(child as Json, full)
      : [[full, child] as [string, unknown]];
  });
}

const interpolations = (text: string) =>
  [...text.matchAll(/\{\{(\w+)\}\}/g)]
    .map((match) => match[1])
    .sort()
    .join(",");

const namespacesOf = (locale: string) =>
  Object.keys(bundles)
    .filter((key) => key.startsWith(`${locale}/`))
    .map((key) => key.slice(locale.length + 1))
    .sort();

const namespaces = namespacesOf(BASE);

describe("locale files", () => {
  it("ships the same namespaces for every locale", () => {
    expect(namespaces.length).toBeGreaterThan(0);
    for (const locale of TARGETS) {
      expect(namespacesOf(locale)).toEqual(namespaces);
    }
  });

  for (const namespace of namespaces) {
    describe(namespace, () => {
      const base = Object.fromEntries(flatten(read(BASE, namespace)));

      for (const locale of TARGETS) {
        it(`${locale} has the same keys as ${BASE}`, () => {
          const target = Object.fromEntries(flatten(read(locale, namespace)));
          expect(Object.keys(target).sort()).toEqual(Object.keys(base).sort());
        });

        it(`${locale} keeps the same interpolation variables as ${BASE}`, () => {
          const target = Object.fromEntries(flatten(read(locale, namespace)));
          const mismatches = Object.keys(base).filter((key) => {
            const from = base[key];
            const to = target[key];
            if (typeof from !== "string" || typeof to !== "string") return false;
            // Singular plural forms legitimately drop {{count}} in en/de.
            if (key.endsWith("_one")) return false;
            return interpolations(from) !== interpolations(to);
          });
          expect(mismatches).toEqual([]);
        });

        it(`${locale} carries no untranslated placeholder markers`, () => {
          const untranslated = flatten(read(locale, namespace))
            .filter(([, value]) => typeof value === "string" && /^\[[A-Z]{2}\]/.test(value))
            .map(([key]) => key);
          expect(untranslated).toEqual([]);
        });
      }
    });
  }
});
