import fs from "fs";
import path from "path";
import { NAMESPACES, SUPPORTED_LANGUAGES } from "@/lib/i18n";

/**
 * FG-001: every language ships the same namespaces and keys as Turkish (the source language), with
 * the same interpolation variables - same approach as web's `i18n.locales.test.ts`.
 */
const LOCALES_DIR = path.join(__dirname, "..", "..", "locales");
const BASE = "tr";
const TARGETS = SUPPORTED_LANGUAGES.filter((language) => language !== BASE);

type Json = Record<string, unknown>;

function namespacesOf(language: string): string[] {
  return fs
    .readdirSync(path.join(LOCALES_DIR, language))
    .filter((file) => file.endsWith(".json"))
    .map((file) => file.replace(/\.json$/, ""))
    .sort();
}

function read(language: string, namespace: string): Json {
  return JSON.parse(
    fs.readFileSync(path.join(LOCALES_DIR, language, `${namespace}.json`), "utf8")
  ) as Json;
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

const namespaces = namespacesOf(BASE);

describe("locale files", () => {
  it("registers exactly the namespaces that exist on disk", () => {
    expect([...NAMESPACES].sort()).toEqual(namespaces);
  });

  it("ships the same namespaces for every language", () => {
    expect(namespaces.length).toBeGreaterThan(0);
    for (const language of TARGETS) {
      expect(namespacesOf(language)).toEqual(namespaces);
    }
  });

  for (const namespace of namespaces) {
    describe(namespace, () => {
      const base = Object.fromEntries(flatten(read(BASE, namespace)));

      it(`${BASE} has no empty values`, () => {
        const empty = Object.entries(base)
          .filter(([, value]) => value === "" || (Array.isArray(value) && value.length === 0))
          .map(([key]) => key);
        expect(empty).toEqual([]);
      });

      for (const language of TARGETS) {
        const target = Object.fromEntries(flatten(read(language, namespace)));

        it(`${language} has the same keys as ${BASE}`, () => {
          expect(Object.keys(target).sort()).toEqual(Object.keys(base).sort());
        });

        it(`${language} keeps the same interpolation variables as ${BASE}`, () => {
          const mismatches = Object.keys(base).filter((key) => {
            const from = base[key];
            const to = target[key];
            if (typeof from !== "string" || typeof to !== "string") return false;
            // Singular plural forms may legitimately drop {{count}}.
            if (key.endsWith("_one")) return false;
            return interpolations(from) !== interpolations(to);
          });
          expect(mismatches).toEqual([]);
        });

        it(`${language} keeps list values the same length as ${BASE}`, () => {
          const mismatches = Object.keys(base).filter((key) => {
            const from = base[key];
            const to = target[key];
            return Array.isArray(from) && (!Array.isArray(to) || to.length !== from.length);
          });
          expect(mismatches).toEqual([]);
        });

        it(`${language} has no empty values`, () => {
          const empty = Object.entries(target)
            .filter(([, value]) => value === "")
            .map(([key]) => key);
          expect(empty).toEqual([]);
        });
      }
    });
  }
});
