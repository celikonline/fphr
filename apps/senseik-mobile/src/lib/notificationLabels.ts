import i18n from "@/lib/i18n";

/**
 * N1/B20: display labels (active language) for notification preference rows - mirrors
 * `web/public/locales/*\/notifications.json` (`template.*`, `category.*`); keys under
 * `notifications:templates`, `notifications:categories`, `notifications:channels`. Template keys
 * contain dots ("request.created"), which i18next reads as nesting - the JSON mirrors that shape.
 *
 * `leave.added_on_behalf`, `onboarding.task_assigned` and `onboarding.task_completed` are NOT in
 * the web JSON either (checked directly - same gap exists there today); their labels are this
 * app's own text, written in the same "when X happens to me" style as the rest of the catalog, not
 * copied from a source that doesn't have them yet.
 */
function lookup(group: string, key: string): string | null {
  const fullKey = `notifications:${group}.${key}`;
  return i18n.exists(fullKey) ? i18n.t(fullKey) : null;
}

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
  return lookup("templates", templateKey) ?? humanizeTemplateKey(templateKey);
}

export function notificationCategoryLabel(category: string): string {
  return lookup("categories", category) ?? category;
}

export function notificationChannelLabel(channel: string): string {
  return lookup("channels", channel) ?? channel;
}
