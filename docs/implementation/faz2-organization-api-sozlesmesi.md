# Faz 2 — Organization/Employee API sözleşmesi (Şirket profili, TB-10)

Bu doküman Faz 2 kapsamında eklenen **şirket profili** uçlarını belgeler. Var olan
`GET/POST /organization/companies` ve `PUT /organization/companies/{id}/holiday-calendar`
uçları (Faz 0'dan beri var, 3 çağıranı değişmedi) burada tekrar edilmez; bkz.
`Sense.Modules.Organization.Api.Controllers.CompaniesController`.

## Kapsam ve tasarım kararı

- `Company` aggregate'i zaten vardı (`OrgUnitId`, `HolidayCalendarId`, `JointEmploymentGroupCode`).
  Bu faz, tüzel/kayıt bilgileri + çalışma saatleri/günleri + logo referansı alanlarını **ekledi**
  (hepsi nullable, additive — bkz. `Company.UpdateProfile`/`SetLogo`).
- `Company.Name` diye bir alan **yok** — isim her zaman bağlı olduğu `OrgUnit.Name`'den gelir;
  profil güncellenirken `OrgUnit.Rename(name)` de çağrılır.
- "Tenant'ın birincil şirketi" (`PrimaryCompanyResolver`): kök (`ParentId == null`) OrgUnit'e bağlı
  `Company`, yoksa en eski oluşturulan. Bugün tenant başına tek kök var (Faz 0 seed), bu yüzden tek
  sonuç döner. Çoklu-şirket (JointEmploymentGroupCode) senaryosu bu resolver'ı genişletmeyi gerektirir
  — bilinçli olarak bu fazın kapsamı dışında bırakıldı.
- Yeni izin `organization.company_profile.manage`; `organization.*` jokerine sahip roller
  (HR, Owner/Admin `*`) otomatik kapsar, `SystemRoleDefinitions`'a ayrı ekleme gerekmedi.

## Uçlar

### `GET /api/v{version}/organization/companies/profile`
- Yetki: `organization.company_profile.manage`
- Yanıt: `CompanyProfileDto`
- 404: tenant'ın hiç Company/OrgUnit kökü yoksa (`organization.company.not_found`).

### `PUT /api/v{version}/organization/companies/profile`
- Yetki: `organization.company_profile.manage`
- Gövde: `UpdateCompanyProfileRequest`
- Davranış: birincil şirketin `OrgUnit.Name`'ini yeniden adlandırır + `Company` üzerindeki tüzel/kayıt
  alanlarını ve çalışma saatleri/günlerini tek seferde günceller. Kısmi güncelleme yoktur — istemci
  formun tamamını gönderir (boş/`null` alanlar temizlenir).
- Doğrulama (`UpdateCompanyProfileValidator`): `Name` zorunlu; `RegistrationNumber`/`TaxNumber`/
  `Address`/`City`/`Country` uzunluk sınırlı (`OrganizationLimits`); `OperatingHoursStart/End`
  `HH:mm` biçiminde veya `null`; `WorkingDays` yalnızca `mon,tue,wed,thu,fri,sat,sun` kodlarından
  oluşabilir.
- Yanıt: güncel `CompanyProfileDto`.

### `PUT /api/v{version}/organization/companies/profile/logo`
- Yetki: `organization.company_profile.manage`
- Gövde: `SetCompanyLogoRequest { LogoDocumentId, LogoUrl }` — `LogoDocumentId: null` logoyu temizler.
- Not: `LogoUrl`, Documents modülünün presigned-URL akışından (`uploadDocumentFile`) dönen **geçici**
  bir URL'dir (blob/presigned, kalıcı değil). Kalıcı görüntüleme URL'i için Documents.Contracts'a bir
  lookup eklenmesi gerekiyor — bilinçli olarak bu fazın kapsamı dışında bırakıldı, gelecek iş.

### DTO: `CompanyProfileDto`
```
{
  id: string (guid)
  orgUnitId: string (guid)
  name: string
  registrationNumber: string | null
  taxNumber: string | null
  address: string | null
  city: string | null
  country: string | null
  operatingHoursStart: string | null   // "HH:mm"
  operatingHoursEnd: string | null     // "HH:mm"
  workingDays: string[]                // subset of mon,tue,wed,thu,fri,sat,sun
  logoDocumentId: string (guid) | null
  logoUrl: string | null
  updatedAt: string (ISO 8601)
}
```
Var olan `CompanyDto` ("Şirket ekle" / tatil takvimi ataması akışı) değişmedi ve bu DTO ile
karıştırılmamalıdır — ikisi de aynı `Company` aggregate'ini farklı alt kümelerle yansıtır.

## Migration

`20260916140500_AddCompanyProfileFields` — `Company` tablosuna yeni nullable kolonlar ekler,
geriye dönük kırıcı değildir. **Not (tur 2):** bu migration araç erişimi olmadığı için elle yazıldı
(bkz. ADR-0001, `senseik-takim/kararlar/`); araç erişimi açılınca `dotnet ef migrations add` çıktısıyla
değiştirilmesi planlanıyor — üretim projeleri derleniyor olması migration SQL'inin doğruluğunu
kanıtlamaz.

## Web

`web/src/services/company.service.ts`: `getCompanyProfile`/`updateCompanyProfile`/`setCompanyLogo`
gerçek `apiClient` çağrıları; eski localStorage mock'u kaldırıldı. `web/src/pages/company-profile.tsx`
sayfa yapısı değişmedi (talimat şartı) — servis katmanı arkadan değişti.

## Bilinen açık işler (bu fazın kapsamı dışında bırakıldı, not düşüldü)
- Migration'ın tooling ile yeniden üretilmesi (araç erişimi bekliyor).
- Logo için kalıcı görüntüleme URL'i (Documents.Contracts lookup).
- Çoklu şirket (`JointEmploymentGroupCode`) senaryosunda `PrimaryCompanyResolver`'ın genişletilmesi.
