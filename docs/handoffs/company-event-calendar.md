# Handoff: Şirket etkinlik takvimi (senseik-027)

Branch: `a/company-event-calendar` · Card: `docs/agent-board/cards/senseik-027-company-event-calendar.json`

## Karar: yeni modül değil, Announcements'a ekleme

`src/Modules/Announcements` zaten "kurumsal duyurular" ve alt-alan olarak "Support" (Yardım Merkezi) barındırıyordu;
`Announcements.Application`/`Announcements.Infrastructure` katmanları `Employee.Contracts` ve `Organization.Contracts`'a
**zaten** proje referansı içeriyordu (kullanılmıyordu) - bu, roadmap'in bu özelliği önceden Announcements'a
planladığının kanıtı (bkz. `docs/analysis/kolayik-video/11.9-ekran-karsilastirma-2026-09-18.md` P-19 kararı:
"Announcements modülünde `CompanyEvent{...}`; `announcements.events.manage` (İK) / `announcements.my.events`
(görüntüle)"). Ayrı bir modül açmak YAGNI olurdu; P-19'daki izin adlarını olduğu gibi kullandım.

## Yapılanlar

### Domain (`Sense.Modules.Announcements.Domain`)
- `CompanyEvent` aggregate: `Title`, `Description?`, `EventDate` (DateOnly), `Type` (`CompanyEventType`),
  `VisibilityOrgUnitId?` (null = tenant geneli), `CreatedByUserId`.
- `CompanyEventType` enum: `CompanyEvent`, `Meeting` (manuel oluşturulabilir), `Birthday`, `LeaveStart` (yalnız
  query-time türetme; `CompanyEvent.Create` bu ikisini reddeder - `CompanyEventDomainErrors.TypeNotManual`).
- `ICompanyEventRepository` (`Repositories.cs`'e eklendi): `Add`, `Remove`, `GetByIdAsync`, `GetInRangeAsync`.

### Application (`CompanyEvents/` klasörü)
- `CreateCompanyEventCommand`/`DeleteCompanyEventCommand` + handler'lar (`announcements.events.manage`).
- `GetCompanyCalendarQuery` (`announcements.my.events`): `[From,To]` aralığında (en fazla 62 gün,
  `CompanyEventLimits.CalendarMaxRangeDays`) üç kaynağı birleştirir:
  1. Manuel `CompanyEvent` kayıtları (görünürlük filtresi: tenant-geneli veya çağıranın/`orgUnitId`
     parametresinin **birebir** eşleştiği birim - alt birim ağacı gezilmez, bkz. "MVP dışı" bölümü).
  2. Doğum günleri: `Employee.Contracts.IEmployeeBirthDateLookup` (zaten mevcuttu, Leave'in 50/18 yaş istisnası için
     eklenmişti) + `IEmployeeDirectory.ListActiveEmployeeIdsAsync` + `IEmployeeContactLookup.GetContactsAsync`
     (isim için). Türetme mantığı saf fonksiyon: `CompanyEventCalendar.DeriveBirthdays` (yıl dönümü/29 Şubat
     kuralları `Employee.Application.BirthdayCalculator` ile aynı).
  3. İzin başlangıçları: **yeni** `Sense.Modules.Leave.Contracts.ILeaveStartDateReader` (bu görev için eklendi) -
     onaylı, hassas olmayan (`LeaveType.IsSensitive` hariç) taleplerin başlangıç tarihleri.
- `CompanyEventCalendar` (saf, I/O'suz): `ValidateRange`, `DeriveBirthdays` - domain birim testleriyle kapsandı.

### Infrastructure
- `CompanyEventConfiguration`, `CompanyEventRepository`, `AnnouncementsDbContext.CompanyEvents` DbSet.
- EF migration: `20260918184012_AddCompanyEvents` (`announcements.company_events` tablosu + 3 index).
- **Yeni cross-module port**: `Leave.Contracts.ILeaveStartDateReader` + `Leave.Infrastructure.Lookups.LeaveStartDateReader`,
  `AddLeaveCoreServices`'e kaydedildi (hem Api hem Worker'da mevcut, ama şu an yalnız Api'de tüketiliyor).

### Api
- `CompanyEventsController`: `GET /api/v1/announcements/events/calendar?from&to&orgUnitId`,
  `POST /api/v1/announcements/events`, `DELETE /api/v1/announcements/events/{id}`.

### İzinler (`AnnouncementsPermissions.cs`)
- `announcements.my.events` (görüntüle) - `"*.my.*"` (`PermissionPatterns.SelfService`) deseniyle zaten her temel
  role (Employee/Intern/HR/Manager/...) ulaşır **Contractor ve ExternalAccountant hariç** (bu iki rol
  `SystemRoleDefinitions.cs`'te `SelfService` spread'ini içermiyor) - bilinçli olarak `SystemRoleDefinitions.cs`'e
  dokunulmadı (P-19'un "role-mapping değişikliği gerekmez" notuyla tutarlı, hot-file riskini azaltmak için).
- `announcements.events.manage` (oluştur/sil) - `"announcements.*"` deseniyle Owner/Admin/HR/HRSpecialist otomatik alır.
- `SharedResource.resx`/`.en.resx`'e her iki anahtar + yeni hata kodları eklendi.

### Demo seed
- `src/Sense.Api/SeedData/DataSeeder.CompanyEvents.cs` (yeni partial dosya, `DataSeeder.cs`'e dokunuş minimal: yeni
  `AnnouncementsDbContext` constructor parametresi + `SeedDemoCompanyEvents(...)` çağrısı + bir `SaveChangesAsync`
  satırı). 3 manuel örnek: tenant-geneli "Yıl Sonu Kutlaması" (CompanyEvent), tenant-geneli "Aylık Genel Toplantı"
  (Meeting), Engineering departmanına kısıtlı "Sprint Planlama" (Meeting). Doğum günü ve izin başlangıcı türleri
  **ayrıca seed edilmedi** - zaten var olan 50 rastgele çalışanın `BirthDate`'i ve `SeedDemoLeaveAsync`'in
  "8 meslektaş için yaklaşan yıllık izin" verisi otomatik olarak takvimde görünür.

### Web (`web/src/pages/company-calendar.tsx`)
- Ay görünümlü, Pazartesi başlangıçlı hücreli grid (haftaya taşan önceki/sonraki ay günleri soluk gösterilir).
- Renk kodlu chip'ler (`company-calendar.module.css`, `data-type` özniteliğiyle): mavi=CompanyEvent,
  turuncu=Meeting, pembe=Birthday, teal=LeaveStart.
- "Etkinlik Oluştur" modalı: başlık, açıklama, tarih (`@/components/ui/date-picker`), tür (CompanyEvent/Meeting
  radio), görünürlük (tüm şirket / belirli birim - `OrgUnitSelect`).
- Manuel etkinliklerde (yalnız `announcements.events.manage` sahibiyse) silme ikonu; türetilmiş etkinliklerde yok
  (`entry.id` null olduğu için buton zaten gizli).
- `web/src/services/company-calendar.service.ts`, yeni i18n namespace `companyCalendar` (tr/en/de).
- `web/src/config/navigation.ts` (SICAK DOSYA): yalnız `APP_LAUNCHER_ITEMS` dizisinin **sonuna** yeni giriş eklendi
  (`/app/company-calendar`, `announcements.my.events` izniyle).
- `web/src/App.tsx`: lazy import + `<Route path="company-calendar" .../>`.

## MVP kapsamı dışı bırakılanlar (bilinçli basitleştirmeler)

1. **Görünürlük yalnız birebir eşleşme**: bir etkinlik Departman X'e kısıtlıysa yalnız `VisibilityOrgUnitId == X`
   olan kullanıcılar görür; X'in alt birimlerindeki çalışanlar görmez (Leave'in kendi takviminde olduğu gibi tam
   bir alt-ağaç gezintisi yok). `Organization.Contracts.IOrgUnitLookup.GetSelfAndDescendantIdsAsync` bunun için
   kullanılabilir ama sorgu başına N+1 çağrı riski taşıyor - gerçek ihtiyaç netleşince eklenebilir.
2. **P-19'un tam şeması uygulanmadı**: kart açıkça "başlık, tarih, tür, görünürlük" istediği ve "ilk sürüm minimal
   olsun" dediği için `StartsAt/EndsAt/AllDay/Location/MeetingUrl` alanları **yok** - yalnız tek bir `EventDate`
   (tüm gün varsayımı). Çok günlü etkinlikler veya saat bazlı toplantılar gerekirse `CompanyEvent`'e alan eklemek
   gerekir (yeni migration).
3. **CRUD'da Update yok**: yalnız Create + Delete + (merged) List var. Bir etkinliğin başlığını/tarihini
   düzenlemek için şimdilik sil-yeniden oluştur gerekiyor.
5. **Ana sayfa widget'ı yok**: `AccessSettingKeys.HomeShowEventCalendar` zaten vardı (roadmap P-21, "Ana sayfa
   Etkinlik Takvimi yer tutucusu, önümüzdeki 14 gün") ama bu karta dahil değildi - `GetCompanyCalendarQuery` aynen
   o widget'ta da kullanılabilir, sadece `[RequiresAccessSetting(AccessSettingKeys.HomeShowEventCalendar)]` eklenip
   home.tsx'e bir kart eklenmesi yeterli olur.
6. **Contractor/ExternalAccountant rolleri** `announcements.my.events` almaz (yukarıda açıklandı) - kasıtlı, ama
   gerçek ihtiyaç çıkarsa `SystemRoleDefinitions.cs`'e tek satır eklemek yeterli.
7. **senseik-024 (Outlook tarzı takvim bileşeni) ile ortaklaştırma yapılmadı**: kartın kendi notu da "YAGNI: gerçek
   ihtiyaç varsa ortaklaştır" diyordu; iki takvim (Leave'in çalışan×gün grid'i, bu ay grid'i) görsel olarak çok
   farklı, ortak bir bileşen şu an zorlama olurdu.

## Testler

- `tests/Modules/Sense.Modules.Announcements.Tests/`:
  - `CompanyEventCalendarTests.cs` - saf türetme mantığı (aralık doğrulama, doğum günü aralık içi/dışı, yıl
    dönümü sarması, 29 Şubat kuralı, çoklu çalışan filtrelemesi).
  - `CompanyEventCommandsTests.cs` - Create (başarı, bilinmeyen OrgUnit, boş başlık, Birthday tipi reddi), Delete
    (bulunamadı, başarılı), permission attribute testi.
  - `CompanyEventQueriesTests.cs` - merge testi (manuel + doğum günü + izin başlangıcı üçünün birleşimi), OrgUnit
    görünürlük filtrelemesi, aralık doğrulama hatası, permission attribute testi.
- Backend: Docker SDK konteyneriyle `dotnet build Sense.slnx` (0 hata) + `dotnet test
  tests/Sense.Tests.Architecture` (52/52 yeşil, permission key naming convention dahil) + `dotnet test
  tests/Modules/Sense.Modules.Announcements.Tests` (40/40 yeşil, önceki + bu görevin testleri).
- Web: `pnpm exec tsc --noEmit` (0 hata) + `pnpm exec eslint src` (0 hata, 0 yeni uyarı) - Docker `node:24-alpine`
  üzerinden (host'ta pnpm bulunamadı, CLAUDE.md'deki Docker fallback kullanıldı).

## Değişen/eklenen dosyalar (özet)

Backend: `src/Modules/Announcements/**` (Domain/Application/Infrastructure/Api, CompanyEvent* dosyaları),
`src/Modules/Leave/Sense.Modules.Leave.Contracts/ILeaveStartDateReader.cs`,
`src/Modules/Leave/Sense.Modules.Leave.Infrastructure/Lookups/LeaveStartDateReader.cs` (+ DI kaydı),
`src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource{,.en}.resx`,
`src/Sense.Api/SeedData/DataSeeder.cs` (+yeni `DataSeeder.CompanyEvents.cs`),
`tests/Modules/Sense.Modules.Announcements.Tests/CompanyEvent*Tests.cs`.

Web: `web/src/pages/company-calendar.tsx` (+ `.module.css`), `web/src/services/company-calendar.service.ts`,
`web/src/config/navigation.ts` (ekleme, sona), `web/src/App.tsx` (route), `web/src/i18n.ts` (namespace),
`web/public/locales/{tr,en,de}/companyCalendar.json` (yeni), `web/public/locales/{tr,en,de}/navigation.json`
(apps.companyCalendar anahtarı).
