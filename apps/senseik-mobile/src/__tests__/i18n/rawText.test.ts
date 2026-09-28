import fs from "fs";
import path from "path";
import ts from "typescript";

/**
 * FG-001: no hard-coded UI copy outside `src/locales` - every user-facing string goes through
 * `t()`. Scans the app source (not tests, not locale files) for:
 *  - JSX text with letters (`<Text>Kaydet</Text>`),
 *  - string literals of UI props (`label="Kaydet"`, `accessibilityLabel="Geri"`, ...),
 *  - any string/template literal containing Turkish-specific letters,
 *  - a hard-coded "tr-TR" locale outside `lib/formatters.ts` (the only locale-tag mapping).
 * Dev-only `console.*` messages are not UI copy and are skipped.
 *
 * ALLOWLIST: files still carrying raw text on purpose. The risky auth/payroll files are translated
 * in P03 (FG-001b) - that part empties this list.
 */
const ALLOWLIST: readonly string[] = [
  "screens/auth/",
  "components/auth/",
  "screens/app/PayrollScreen.tsx",
  "services/AuthService.ts",
  "hooks/useAuth.ts",
  "store/authStore.ts",
  "lib/storage.ts",
];

/** Product/brand names are not translated. */
const BRAND_TEXT = new Set(["Sense", "HR", "Sense HR"]);

const UI_PROPS = new Set([
  "accessibilityHint",
  "accessibilityLabel",
  "actionLabel",
  "description",
  "label",
  "message",
  "placeholder",
  "subtitle",
  "title",
]);

const TURKISH_LETTERS = /[çğıöşüÇĞİÖŞÜ]/;
const LETTER = /\p{L}/u;
const LOCALE_TAG_FILE = "lib/formatters.ts";

const SRC_DIR = path.join(__dirname, "..", "..");

function sourceFiles(dir: string): string[] {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      return entry.name === "__tests__" || entry.name === "locales" ? [] : sourceFiles(full);
    }
    return /\.tsx?$/.test(entry.name) && !entry.name.endsWith(".d.ts") ? [full] : [];
  });
}

function relative(file: string): string {
  return path.relative(SRC_DIR, file).split(path.sep).join("/");
}

function matchesEntry(rel: string, entry: string): boolean {
  return entry.endsWith("/") ? rel.startsWith(entry) : rel === entry;
}

function isConsoleCall(node: ts.Node, sourceFile: ts.SourceFile): boolean {
  return ts.isCallExpression(node) && /^console\./.test(node.expression.getText(sourceFile));
}

function isStringLike(node: ts.Node): node is ts.StringLiteralLike | ts.TemplateLiteralLikeNode {
  return (
    ts.isStringLiteral(node) ||
    ts.isNoSubstitutionTemplateLiteral(node) ||
    ts.isTemplateHead(node) ||
    ts.isTemplateMiddle(node) ||
    ts.isTemplateTail(node)
  );
}

function jsxPropName(node: ts.Node): string | null {
  const parent = node.parent;
  return parent && ts.isJsxAttribute(parent) ? parent.name.getText() : null;
}

/** Raw UI text findings ("file:line \"text\"") in one source file. */
function rawTextIn(source: string, rel: string): string[] {
  const sourceFile = ts.createSourceFile(
    rel,
    source,
    ts.ScriptTarget.Latest,
    true,
    rel.endsWith(".tsx") ? ts.ScriptKind.TSX : ts.ScriptKind.TS
  );
  const findings: string[] = [];
  const report = (node: ts.Node, text: string) => {
    const { line } = sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile));
    findings.push(`${rel}:${line + 1} ${JSON.stringify(text)}`);
  };

  const visit = (node: ts.Node) => {
    if (ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) return;
    if (isConsoleCall(node, sourceFile)) return;

    if (ts.isJsxText(node)) {
      const text = node.text.trim();
      if (LETTER.test(text) && !BRAND_TEXT.has(text)) report(node, text);
    } else if (isStringLike(node)) {
      const text = node.text;
      const prop = jsxPropName(node);
      if (TURKISH_LETTERS.test(text)) {
        report(node, text);
      } else if (prop && UI_PROPS.has(prop) && LETTER.test(text)) {
        report(node, text);
      } else if (text === "tr-TR" && rel !== LOCALE_TAG_FILE) {
        report(node, text);
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return findings;
}

describe("raw UI text (FG-001)", () => {
  const files = sourceFiles(SRC_DIR).map((file) => ({ file, rel: relative(file) }));

  it("finds the app source files", () => {
    expect(files.length).toBeGreaterThan(50);
  });

  it("has no hard-coded UI text outside the allowlist", () => {
    const findings = files
      .filter(({ rel }) => !ALLOWLIST.some((entry) => matchesEntry(rel, entry)))
      .flatMap(({ file, rel }) => rawTextIn(fs.readFileSync(file, "utf8"), rel));
    expect(findings).toEqual([]);
  });

  it("only allowlists paths that exist", () => {
    const missing = ALLOWLIST.filter((entry) => !files.some(({ rel }) => matchesEntry(rel, entry)));
    expect(missing).toEqual([]);
  });

  it("detects each kind of raw text and skips t() calls and console messages", () => {
    const probe = [
      "const a = <Text>Kaydet</Text>;",
      'const b = <X accessibilityLabel="Back" />;',
      'const c = "Geçersiz";',
      'const d = new Intl.NumberFormat("tr-TR");',
      'console.error("Hata oluştu");',
      'const e = <Text>{t("common:actions.save")}</Text>;',
      "const f = <Text>Sense</Text>;",
    ].join("\n");

    expect(rawTextIn(probe, "probe.tsx")).toEqual([
      'probe.tsx:1 "Kaydet"',
      'probe.tsx:2 "Back"',
      'probe.tsx:3 "Geçersiz"',
      'probe.tsx:4 "tr-TR"',
    ]);
  });
});
