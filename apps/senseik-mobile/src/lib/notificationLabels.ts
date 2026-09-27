/**
 * N1/B20: Turkish display labels for notification preference rows - mirrors
 * `web/public/locales/tr/notifications.json` (`template.*`, `category.*`). The mobile app has no
 * i18n layer (Turkish-only, same as `LeaveService.ts`/`leaveTypeNames.ts`), so these are plain
 * lookup tables instead of i18next keys.
 *
 * `leave.added_on_behalf`, `onboarding.task_assigned` and `onboarding.task_completed` are NOT in
 * the web JSON either (checked directly - same gap exists there today); their labels below are
 * this file's own Turkish text, written in the same "when X happens to me" style as the rest of
 * the catalog, not copied from a source that doesn't have them yet.
 */
const TEMPLATE_LABELS: Record<string, string> = {
  "request.created": "Talebim oluşturulduğunda",
  "request.approved": "Talebim onaylandığında",
  "request.rejected": "Talebim reddedildiğinde",
  "approval.task_assigned": "Onayımı bekleyen bir talep olduğunda",
  "documents.expiry_reminder": "Belge süresi dolmak üzere olduğunda",
  "ohs.sgk_notification_due": "İş kazasının SGK bildirim süresi yaklaştığında ya da geçtiğinde",
  "employee.hired": "Yeni bir çalışan işe başladığında",
  "employee.welcome": "İşe başladığımda hoş geldin e-postası",
  "employee.terminated": "Bir çalışan işten ayrıldığında",
  "employee.work_certificate": "İşten ayrılışta çalışma belgesi",
  "employee.birthday": "Doğum günümde",
  "announcement.published": "Yeni bir duyuru yayınlandığında",
  "payroll.payslip_ready": "Ücret pusulam hazır olduğunda",
  "leave.added_on_behalf": "Adıma bir izin talebi eklendiğinde",
  "onboarding.task_assigned": "Bana bir oryantasyon görevi atandığında",
  "onboarding.task_completed": "Bir oryantasyon görevi tamamlandığında",
};

const CATEGORY_LABELS: Record<string, string> = {
  account: "Hesap",
  request: "Talepler",
  approval: "Onaylar",
  reminder: "Hatırlatmalar",
  employee: "Çalışanlar",
  announcement: "Duyurular",
  payroll: "Bordro",
  onboarding: "Oryantasyon",
};

const CHANNEL_LABELS: Record<string, string> = {
  email: "E-posta",
  in_app: "Uygulama içi",
};

/**
 * B20: last-resort fallback for a `templateKey` with no mapped label (a newly added backend
 * catalog entry this file hasn't caught up with yet) - derives a readable title from the key
 * ("some.new_key" -> "Some New Key") instead of showing the raw dotted/underscored string.
 */
function humanizeTemplateKey(templateKey: string): string {
  return templateKey
    .split(/[._]/)
    .filter((word) => word.length > 0)
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join(" ");
}

export function notificationTemplateLabel(templateKey: string): string {
  return TEMPLATE_LABELS[templateKey] ?? humanizeTemplateKey(templateKey);
}

export function notificationCategoryLabel(category: string): string {
  return CATEGORY_LABELS[category] ?? category;
}

export function notificationChannelLabel(channel: string): string {
  return CHANNEL_LABELS[channel] ?? channel;
}
