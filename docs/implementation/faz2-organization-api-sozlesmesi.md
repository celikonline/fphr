# Faz 2 — Organization/Employee API sözleşmesi (Şirket profili TB-10 + Maliyet merkezleri S-4)

Bu doküman Faz 2 kapsamında eklenen **şirket profili** (TB-10) ve **maliyet merkezi katalogu** (S-4,
roadmap item #2) uçlarını belgeler. Var olan `GET/POST /organization/companies` ve
`PUT /organization/companies/{id}/holiday-calendar` uçları (Faz 0'dan beri var, 3 çağıranı değişmedi)
burada tekrar edilmez; bkz. `Sense.Modules.Organization.Api.Controllers.CompaniesController`.

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

`20260916140500_AddCompanyProfileFields` — main'de (`caafaf0`), `Company` tablosuna yeni nullable
kolonlar ekler, geriye dönük kırıcı değildir. Bu turda migration + Designer + model snapshot,
`CompanyConfiguration`'daki (`OrganizationDbContext.cs`) `HasMaxLength`/`HasColumnType` değerleriyle
karşılaştırıldı — **sapma yok** (address 500, city 100, country 100, registration_number 50,
tax_number 50, working_days 40, logo_url 1000, operating_hours_* time without time zone,
logo_document_id uuid nullable).

## Maliyet merkezleri (S-4, roadmap Faz 2 item #2)

**Durum (2026-09-17 itibarıyla ✅ tamamlandı, ADR-0013 kapandı):** Domain aggregate (`CostCenter`) +
Application katmanı (komut/sorgu handler'ları) + `CostCentersController` + EF config/repository/lookup/
DI kaydı + `20260916145457_AddCostCenters` migration'ı (insan commit `8b3b34b`) hepsi main'de. Uçlar
gerçek veriye bağlı, "pending model changes" geçici durumu kapandı. Uç şekli aşağıda donmuştur, davranış
migration öncesiyle aynı.

### `GET /api/v{version}/organization/cost-centers?includeInactive=false`
- Yetki: `organization.cost_centers.view`
- Yanıt: `CostCenterDto[]` (paging yok — düz liste, `code`'a göre sıralı)
- `includeInactive` varsayılan `false` (yalnız aktifler).

### `POST /api/v{version}/organization/cost-centers`
- Yetki: `organization.cost_centers.manage`
- Gövde: `{ code: string, name: string }`
- Yanıt: `{ id: string (guid) }` — **tam DTO değil**, yalnız oluşturulan kaydın id'si.
- 409 `organization.cost_center.code_taken`: kod tenant içinde zaten kullanılıyor.
- `code` oluşturma sonrası **değişmez** (rename ucu yalnız `name` alır).

### `PUT /api/v{version}/organization/cost-centers/{id}`
- Yetki: `organization.cost_centers.manage`
- Gövde: `{ name: string }` (yalnız ad; `code` bu uçtan değiştirilemez)
- Yanıt: 204
- 404 `organization.cost_center.not_found`.

### `PUT /api/v{version}/organization/cost-centers/{id}/active`
- Yetki: `organization.cost_centers.manage`
- Gövde: `{ isActive: bool }`
- Yanıt: 204
- 404 `organization.cost_center.not_found`; zaten istenen durumdaysa 422 (`organization.cost_center.already_active`/`already_inactive`) — idempotent bir no-op **değil**, domain kuralı (bkz. `CostCenter.SetActive`, tur 3 testleri).

### DTO: `CostCenterDto`
```
{
  id: string (guid)
  code: string
  name: string
  isActive: bool
}
```

### İzinler
`organization.cost_centers.view` / `organization.cost_centers.manage` — `organization.*` (Owner/Admin/HR)
ve `organization.*.view` (HRSpecialist/Manager/Timekeeper/Finance/Recruiter/PayrollSpecialist/
ExternalAccountant/Auditor'ın zaten sahip olduğu joker) bu iki anahtarı **otomatik** kapsıyor —
`SystemRoleDefinitions.cs`'e ayrı ekleme gerekmedi (yalnız-ekleme kısıtı zaten korunmuş oldu).

### Birim varsayılanı ve çalışan istisnası

Maliyet merkezi kataloğu artık organizasyon birimi varsayılanı ve çalışan istisnasıyla bordroya bağlanır:

- `PUT /organization/units/{id}/cost-center` — `{ costCenterId: guid | null }`; birimin doğrudan maliyet merkezini atar veya temizler. Birimde atama yoksa çözümleme üst hiyerarşide devam eder.
- `GET /organization/cost-centers/employees/{employeeId}/assignment` — çalışanın istisnasını, birimden devraldığı değeri ve efektif değeri döndürür.
- `PUT /organization/cost-centers/employees/{employeeId}/assignment` — `{ costCenterId: guid | null }`; birim değerini çalışana özel merkezle geçersiz kılar. `null`, istisnayı kaldırıp birimden devralmayı geri getirir.

Çözümleme sırası: çalışan istisnası → çalışanın birimi → en yakın üst birimdeki atama. Bordro hesaplanırken çözümlenen merkez `Payslip` üzerinde saklanır; açık dönemlerin değişiklik algılama işi atama değişikliğini bordro girdisi farkı olarak görebilir. Atamalar etkin tarihli değildir; yeni değer bordro yeniden hesaplanırken kullanılır.

## Web

`web/src/services/company.service.ts`: `getCompanyProfile`/`updateCompanyProfile`/`setCompanyLogo`
gerçek `apiClient` çağrıları; eski localStorage mock'u kaldırıldı. `web/src/pages/company-profile.tsx`
sayfa yapısı değişmedi (talimat şartı) — servis katmanı arkadan değişti.

## Birim pasifleştirme — hata kodları (gün açılışı tur 1 BE md.1-2)

`DELETE /api/v{version}/organization/units/{id}` (soft-deactivate, `OrgUnitsController` — mevcut uç,
davranış genişletildi):
- 409 `organization.unit.has_active_children` — alt birimleri aktif olan birim pasifleştirilemez (kod
  değeri düzeltildi: daha önce yanlışlıkla `organization.unit_type.in_use` ile aynıydı, artık ayrı).
- 409 `organization.unit.has_active_employees` — **yeni**: birimde aktif çalışan varsa pasifleştirme
  engellenir (`DeactivateOrgUnitHandler`'a `IEmployeeLookup.AnyActiveInOrgUnitsAsync` eklendi,
  `DeleteOrgUnitTypeHandler` ile aynı kalıp). Kontrol sırası: önce alt birim, sonra aktif çalışan.
- `organization.unit_type.in_use` (seviye silme ucu, `OrgUnitTypesController`) **değişmedi** — web
  `structure/errors.ts` ona hâlâ ayrı bağlı, dokunulmadı.

## S-19 — konum adı portu (Faz 9 kariyer sayfası için, Faz 2 yazar)

`Organization.Contracts.ILocationLookup.GetNamesAsync(tenantId, locationIds, ct)` →
`IReadOnlyDictionary<Guid, string>`. Pasif konumlar dahil edilir (kapanmış bir ilan hâlâ artık pasif bir
konuma referans verebilir ve adını göstermeye devam etmeli); tenant'a ait olmayan veya bulunamayan id'ler
sonuç sözlüğünde **yok** — çağıran her istediği id'nin geri geleceğini varsaymamalı. Mevcut
`ExistsActiveAsync` değişmedi. Implementasyon: `Infrastructure/Lookups/LocationLookup.cs`.

## Bilinen açık işler (bu fazın kapsamı dışında bırakıldı, not düşüldü)
- Logo için kalıcı görüntüleme URL'i (Documents.Contracts lookup).
- Çoklu şirket (`JointEmploymentGroupCode`) senaryosunda `PrimaryCompanyResolver`'ın genişletilmesi.
- Maliyet merkezi — organizasyon birimi varsayılanı ve çalışan istisnası artık var; pozisyon bazlı ve etkin tarihli atamalar henüz yok.
