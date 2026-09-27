import {
  notificationCategoryLabel,
  notificationChannelLabel,
  notificationTemplateLabel,
} from "@/lib/notificationLabels";

/**
 * B20: the full backend template catalog, kept here as a fixed list (not imported - this is a
 * mobile app with no reference to the .NET source tree) so a test can assert every key the server
 * can actually send has a real Turkish label, not a raw fallback. Source of truth:
 * `src/Modules/Notifications/Sense.Modules.Notifications.Contracts/NotificationKeys.cs`
 * (`NotificationTemplateKeys`) - update this list whenever that file gains a new template key.
 */
const BACKEND_TEMPLATE_KEYS = [
  "request.created",
  "approval.task_assigned",
  "request.approved",
  "request.rejected",
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
  "leave.added_on_behalf",
];

describe("notificationTemplateLabel (N1/B20)", () => {
  it("translates a known template key", () => {
    expect(notificationTemplateLabel("request.approved")).toBe("Talebim onaylandığında");
  });

  it("translates the previously-missing leave/onboarding keys (B20)", () => {
    expect(notificationTemplateLabel("leave.added_on_behalf")).toBe(
      "Adıma bir izin talebi eklendiğinde"
    );
    expect(notificationTemplateLabel("onboarding.task_assigned")).toBe(
      "Bana bir oryantasyon görevi atandığında"
    );
    expect(notificationTemplateLabel("onboarding.task_completed")).toBe(
      "Bir oryantasyon görevi tamamlandığında"
    );
  });

  it("derives a readable title instead of showing a raw key when unmapped", () => {
    expect(notificationTemplateLabel("some.new_key")).toBe("Some New Key");
  });

  it("has a real (non-derived) Turkish label for every key in the backend catalog", () => {
    for (const templateKey of BACKEND_TEMPLATE_KEYS) {
      const label = notificationTemplateLabel(templateKey);
      expect(label).not.toBe(templateKey);
      expect(label.includes(".")).toBe(false);
    }
  });
});

describe("notificationCategoryLabel (N1/B20)", () => {
  it("translates a known category", () => {
    expect(notificationCategoryLabel("approval")).toBe("Onaylar");
  });

  it("translates the onboarding category (B20 - catalog also assigns it to the two onboarding templates)", () => {
    expect(notificationCategoryLabel("onboarding")).toBe("Oryantasyon");
  });

  it("falls back to the raw category when truly unmapped", () => {
    expect(notificationCategoryLabel("something_new")).toBe("something_new");
  });
});

describe("notificationChannelLabel (N1)", () => {
  it("translates known channels", () => {
    expect(notificationChannelLabel("email")).toBe("E-posta");
    expect(notificationChannelLabel("in_app")).toBe("Uygulama içi");
  });
});
