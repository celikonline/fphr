import { describe, expect, it } from "vitest";
import trReports from "../public/locales/tr/reports.json";
import enReports from "../public/locales/en/reports.json";
import deReports from "../public/locales/de/reports.json";
import trNotifications from "../public/locales/tr/notifications.json";
import enNotifications from "../public/locales/en/notifications.json";
import deNotifications from "../public/locales/de/notifications.json";

// D-1/D-2 (ik-surec-testi-bulgu-raporu-2026-09-20): these labels used to render as the raw key
// ("leave.overtime.monthly", "documents.expiry_reminder / reminder").
type Tree = { [key: string]: string | Tree };

function lookup(tree: Tree, path: string): string | undefined {
  const value = path.split(".").reduce<string | Tree | undefined>((node, key) => (typeof node === "object" ? node[key] : undefined), tree);
  return typeof value === "string" ? value : undefined;
}

const REPORTS: Record<string, Tree> = { tr: trReports.catalog, en: enReports.catalog, de: deReports.catalog } as Record<string, Tree>;
const REPORT_GROUPS: Record<string, Tree> = { tr: trReports.groups, en: enReports.groups, de: deReports.groups } as Record<string, Tree>;
const REPORT_FIELDS: Record<string, Tree> = { tr: trReports.fields, en: enReports.fields, de: deReports.fields } as Record<string, Tree>;
const NOTIFICATIONS: Record<string, Tree> = { tr: trNotifications, en: enNotifications, de: deNotifications } as Record<string, Tree>;

describe.each(["tr", "en", "de"])("%s locale", (locale) => {
  it.each(["leave.overtime.monthly", "leave.reconciliation.form"])("has a report label and description for %s", (reportKey) => {
    expect(lookup(REPORTS[locale], `${reportKey}.label`)).toBeTruthy();
    expect(lookup(REPORTS[locale], `${reportKey}.description`)).toBeTruthy();
  });

  it("translates custom report groups and fields", () => {
    expect(lookup(REPORT_GROUPS[locale], "employee.general")).toBeTruthy();
    expect(lookup(REPORT_GROUPS[locale], "leave.requests")).toBeTruthy();
    expect(lookup(REPORT_FIELDS[locale], "employee.firstName")).toBeTruthy();
    expect(lookup(REPORT_FIELDS[locale], "leave.request.days")).toBeTruthy();
  });

  it("names the document expiry reminder notification and its category", () => {
    expect(lookup(NOTIFICATIONS[locale], "template.documents.expiry_reminder")).toBeTruthy();
    expect(lookup(NOTIFICATIONS[locale], "category.reminder")).toBeTruthy();
  });
});
