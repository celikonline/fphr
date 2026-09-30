import { describe, expect, it } from "vitest";
import tr from "../public/locales/tr/notifications.json";
import en from "../public/locales/en/notifications.json";
import de from "../public/locales/de/notifications.json";

// Backend NotificationCatalog.All template keys (kept fixed on purpose: adding a key there must add a name here).
const CATALOG_KEYS = [
  "request.created",
  "request.approved",
  "request.rejected",
  "approval.task_assigned",
  "leave.added_on_behalf",
  "documents.expiry_reminder",
  "ohs.sgk_notification_due",
  "employee.hired",
  "employee.welcome",
  "employee.terminated",
  "employee.work_certificate",
  "employee.birthday",
  "announcement.published",
  "payroll.payslip_ready",
  "onboarding.task_assigned",
  "onboarding.task_completed",
  "attendance.missing_punch_reminder",
  "payroll.payslip_acknowledgement_reminder",
  "payroll.payslip_document_uploaded",
  "hr.message",
  "announcement.event_invitation",
  "workflow.delegation_assigned",
  "payroll.payslip_objection_accepted",
];
const CATEGORIES = ["account", "request", "approval", "reminder", "employee", "announcement", "payroll", "onboarding"];

const pick = (root: unknown, path: string): unknown =>
  path.split(".").reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], root);

describe.each([
  ["tr", tr],
  ["en", en],
  ["de", de],
])("notifications.json (%s)", (_locale, bundle) => {
  it.each(CATALOG_KEYS)("template %s has a display name", (key) => {
    const value = pick((bundle as { template: unknown }).template, key);
    expect(typeof value).toBe("string");
    expect((value as string).length).toBeGreaterThan(3);
  });

  it.each(CATEGORIES)("category %s has a label", (category) => {
    expect(typeof (bundle as { category: Record<string, unknown> }).category[category]).toBe("string");
  });
});
