import type { MyProfile } from "@/services/ProfileService";

export type ProfileField = { label: string; value: string };

export type ProfileGroup = {
  key: "personal" | "contact" | "job";
  label: string;
  fields: ProfileField[];
};

function formatHireDate(value: string): string {
  const date = new Date(`${value}T00:00:00`);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleDateString("tr-TR", { day: "2-digit", month: "short", year: "numeric" });
}

/**
 * Groups `getMyProfile()`'s fields into the segmented-control tabs the profile screen shows
 * (Kişisel / İletişim / İş). A group with no populated field for this employee is left out of the
 * result entirely - the screen never renders an empty tab (mobil yeniden tasarım Task 6 decision).
 * `MyProfile` today has no personal-data field (KVKK data minimization, see `ProfileService.ts`), so
 * "Kişisel" is always omitted - this stays generic so a future field slots in without a screen change.
 */
export function buildProfileGroups(profile: MyProfile): ProfileGroup[] {
  const personal: ProfileField[] = [];

  const contact: ProfileField[] = [];
  if (profile.workEmail) contact.push({ label: "İş e-postası", value: profile.workEmail });
  const phone = profile.mobilePhone ?? profile.workPhone;
  if (phone) contact.push({ label: "Telefon", value: phone });

  const job: ProfileField[] = [];
  if (profile.titleName) job.push({ label: "Unvan", value: profile.titleName });
  if (profile.departmentName) job.push({ label: "Birim", value: profile.departmentName });
  if (profile.hireDate) job.push({ label: "İşe giriş tarihi", value: formatHireDate(profile.hireDate) });

  const groups: ProfileGroup[] = [
    { key: "personal", label: "Kişisel", fields: personal },
    { key: "contact", label: "İletişim", fields: contact },
    { key: "job", label: "İş", fields: job },
  ];
  return groups.filter((group) => group.fields.length > 0);
}
