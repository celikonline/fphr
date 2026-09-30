import { buildProfileGroups } from "@/screens/app/profile/profileGroups";
import type { MyProfile } from "@/services/ProfileService";

const BASE_PROFILE: MyProfile = {
  displayName: "Ayşe Yılmaz",
  titleName: "Yazılım Mühendisi",
  departmentName: "Mühendislik",
  hireDate: "2020-01-15",
  workEmail: "ayse.yilmaz@ornek.com",
  mobilePhone: "+90 555 000 00 00",
  workPhone: null,
};

describe("buildProfileGroups (mobil yeniden tasarım Task 6)", () => {
  it("builds contact and job groups when every field is present", () => {
    const groups = buildProfileGroups(BASE_PROFILE);

    expect(groups.map((group) => group.key)).toEqual(["contact", "job"]);
    expect(groups.find((group) => group.key === "contact")?.fields).toEqual([
      { label: "İş e-postası", value: "ayse.yilmaz@ornek.com" },
      { label: "Telefon", value: "+90 555 000 00 00" },
    ]);
    expect(groups.find((group) => group.key === "job")?.fields).toEqual([
      { label: "Unvan", value: "Yazılım Mühendisi" },
      { label: "Birim", value: "Mühendislik" },
      { label: "İşe giriş tarihi", value: "15 Oca 2020" },
    ]);
  });

  it("always omits the personal group - MyProfile has no personal-data field today", () => {
    const groups = buildProfileGroups(BASE_PROFILE);
    expect(groups.some((group) => group.key === "personal")).toBe(false);
  });

  it("omits a group entirely when none of its fields are populated", () => {
    const groups = buildProfileGroups({
      ...BASE_PROFILE,
      workEmail: null,
      mobilePhone: null,
      workPhone: null,
    });

    expect(groups.map((group) => group.key)).toEqual(["job"]);
  });

  it("falls back to workPhone when mobilePhone is absent", () => {
    const groups = buildProfileGroups({
      ...BASE_PROFILE,
      mobilePhone: null,
      workPhone: "+90 212 000 00 00",
    });

    expect(groups.find((group) => group.key === "contact")?.fields).toContainEqual({
      label: "Telefon",
      value: "+90 212 000 00 00",
    });
  });

  it("returns no groups at all when every optional field is empty", () => {
    const groups = buildProfileGroups({
      displayName: "Ayşe Yılmaz",
      titleName: null,
      departmentName: null,
      hireDate: null,
      workEmail: null,
      mobilePhone: null,
      workPhone: null,
    });

    expect(groups).toEqual([]);
  });
});
