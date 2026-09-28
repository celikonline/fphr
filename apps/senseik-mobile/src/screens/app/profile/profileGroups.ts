import type { MyProfile } from "@/services/ProfileService";
import { formatDate } from "@/lib/formatters";
import i18n from "@/lib/i18n";

export type ProfileField = { label: string; value: string };

export type ProfileGroup = {
  key: "personal" | "contact" | "job";
  label: string;
  fields: ProfileField[];
};

function formatHireDate(value: string): string {
  const date = new Date(`${value}T00:00:00`);
  if (Number.isNaN(date.getTime())) return value;
  return formatDate(date, { day: "2-digit", month: "short", year: "numeric" });
}

/**
 * Groups `getMyProfile()`'s fields into the segmented-control tabs the profile screen shows
 * (Kişisel / İletişim / İş). A group with no populated field for this employee is left out of the
 * result entirely - the screen never renders an empty tab (mobil yeniden tasarım Task 6 decision).
 * `MyProfile` today has no personal-data field (KVKK data minimization, see `ProfileService.ts`), so
 * "Kişisel" is always omitted - this stays generic so a future field slots in without a screen change.
 */
export function buildProfileGroups(profile: MyProfile): ProfileGroup[] {
  const label = (key: string) => i18n.t(`profile:fields.${key}`);
  const personal: ProfileField[] = [];

  const contact: ProfileField[] = [];
  if (profile.workEmail) contact.push({ label: label("workEmail"), value: profile.workEmail });
  const phone = profile.mobilePhone ?? profile.workPhone;
  if (phone) contact.push({ label: label("phone"), value: phone });

  const job: ProfileField[] = [];
  if (profile.titleName) job.push({ label: label("title"), value: profile.titleName });
  if (profile.departmentName)
    job.push({ label: label("department"), value: profile.departmentName });
  if (profile.hireDate)
    job.push({ label: label("hireDate"), value: formatHireDate(profile.hireDate) });

  const groups: ProfileGroup[] = [
    { key: "personal", label: i18n.t("profile:groups.personal"), fields: personal },
    { key: "contact", label: i18n.t("profile:groups.contact"), fields: contact },
    { key: "job", label: i18n.t("profile:groups.job"), fields: job },
  ];
  return groups.filter((group) => group.fields.length > 0);
}
