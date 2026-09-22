# senseik-015 — İSG Modülü — UYGULAMA PLANI

## Implementasyon Notu (bu tur)

**Durum: Faz 0 + Faz 1 tamamlandı (branch `a/occupational-safety`, bu doküman güncellendiğinde henüz push edilmedi). Faz 2-6 bu turun kapsamı dışında bırakıldı, backlog'da.**

### Yapılanlar

- **Faz 0 — İskelet**: `src/Modules/OccupationalSafety` (Domain/Application/Contracts/Infrastructure/Api), Assets modülü şablon alındı. `OhsDbContext` (şema `ohs`), ilk EF migration (`InitialOhs`, gerçek `dotnet ef migrations add` ile üretildi — elle yazılmadı). `ModuleCatalog.cs`, `Sense.Migrator/Program.cs`, `Sense.Worker/Program.cs`, `Sense.slnx`, `Sense.Api/Sense.Api.csproj`, `Sense.Migrator.csproj`, `Sense.Worker.csproj` — hepsine append-only (listenin sonuna) eklendi. `ohs.hazard_classifications.{view,manage}` + `ohs.risk_assessments.{view,manage}` izin seti (`OhsPermissions.cs`); `SystemRoleDefinitions.cs`'te HR ve HRSpecialist rollerinin Include listelerine `"ohs.*"` append edildi (append-only, mevcut satırlar değişmedi). NetArchTest (`Sense.Tests.Architecture`, 54/54 yeşil) yeni modülü otomatik kapsadı.
- **Faz 1 — Tehlike sınıfı + Risk değerlendirmesi**: `HazardClass` enum (Low/Medium/High), `WorkplaceHazardClassification` aggregate (Location bazlı, tarih-aralıklı — Employee'nin `SalaryRecord` deseni: sadece `EffectiveFrom`, "en son başlamış kayıt" = güncel; `EffectiveTo` yok). `RiskAssessment` aggregate: `RenewalPeriodMonths` oluşturma anında `HazardClassAtAssessment`'tan türetilir (statik tablo `RiskAssessmentRenewalPeriods`: Az tehlikeli→72 ay, Tehlikeli→48 ay, Çok tehlikeli→24 ay — yasal azami süreler; `ILegislationParameterReader`'a dokunulmadı, plan §6'nın önerisi buydu). `RecordRiskAssessmentCommand` hazard class'ı komuttan almıyor — konumun o tarihteki güncel sınıflandırmasından otomatik snapshot alıyor (`RiskAssessmentHazardClassificationMissing` hatası sınıflandırma yoksa).
  Web: `settings/ohs-hazard-classification.tsx` (güncel sınıflandırma listesi + yeniden sınıflandırma dialogu), `settings/ohs-risk-assessments.tsx` (RD listesi + kayıt dialogu) — `asset-categories.tsx` deseni, `ohs.service.ts`, navigasyon/route/i18n (tr/en/de `ohs.json` + `navigation.json` ek anahtarları) kablolandı.
  Demo seed: `DataSeeder.DemoOhs.cs` — 3 demo lokasyonun (İstanbul HQ/Ankara/İzmir) her biri için bir tehlike sınıfı + bir risk değerlendirmesi (Low/Medium/High'ın üçü de temsil ediliyor), kartın merge_gate'i ("tehlike sınıfı ve periyodlar yüklü") karşılanıyor.

### Doğrulama

- `dotnet build Sense.slnx`: 0 hata (Docker SDK konteyneri).
- `Sense.Modules.OccupationalSafety.Tests`: 18/18 yeşil (domain: RD periyot hesaplama, hazard classification validasyonu; handler testleri: reclassify/duplicate-date, RD kaydı hazard-class snapshot'ı).
- `Sense.Tests.Architecture`: 54/54 yeşil (Contracts-only erişim, permission key konvansiyonu, tek DbContext/modül kuralları yeni modülü otomatik kapsadı).
- `Sense.Tests.Integration` (filtreli, `DemoOhsSeedTests`): 7/7 yeşil — demo seed smoke testi, DB gerektirmeden `DataSeeder.CreateHazardClassificationOrThrow`/`CreateRiskAssessmentOrThrow` static yardımcılarını doğruluyor.
- Web: `tsc --noEmit` temiz, `eslint src` temiz (1 önceden var olan, bu turla ilgisiz uyarı hariç), i18n locale testi (`ohs.json` için tr/en/de anahtar/interpolasyon paritesi) yeşil.

### Kalan Fazlar (backlog, bu turda dokunulmadı)

- **Faz 2 — Kurul + görevlendirme** — ✅ bitti (2026-09-22): `SafetyAssignment` (uzman/hekim/DSP, lokasyon bazlı, OSGB adı, belge sınıfı, sözleşmeli aylık dakika, başlangıç/bitiş) ve `OhsCommitteeMember` (6331 md.22 rolleri) aggregate'leri, migration `AddOhsServices`. `OhsServiceNorms` statik tablo: çalışan başına aylık asgari dakika (uzman 10/20/40, hekim 5/10/15, DSP yalnız çok tehlikelide 10), belge sınıfı uygunluğu (A her sınıf, B az tehlikeli+tehlikeli, C yalnız az tehlikeli), kurul eşiği 50 çalışan, toplantı sıklığı 3/2/1 ay, çalışan temsilcisi sayısı (6331 md.20). `ListLocationServiceSummariesQuery` her sınıflandırılmış lokasyon için çalışan sayısını (`IEmployeeLookup.CountActiveByOrgUnitAsync` + `IOrgUnitLookup.GetEffectiveLocationMapAsync`) normlarla karşılaştırıp eksik kodları döndürür. API `api/v1/ohs/services`, izinler `ohs.services.{view,manage}`, web `/app/ohs` → "İSG hizmetleri" sekmesi. Sınır: kurul toplantı tutanakları ve temsilci seçim kaydı yok; temsilci sayısı yalnız kurul zorunluysa denetlenir.
- **Faz 3 — Kaza/ramak kala + Documents entegrasyonu** — ✅ bitti (2026-09-22; kalanlar f915b52a: `WorkAccidentRecorded` → Documents `WorkAccidentReport` gereksinimi SGK son gününe vadeli, günlük `OhsSgkNotificationReminder` job'ı → `ohs.sgk_notification_due` bildirimi). Çekirdek (1928ec2f): `OhsIncident` (iş kazası/ramak kala), SGK son günü = kazadan sonraki 3. iş günü (`SgkNotificationDeadline`, `IHolidayCalendarLookup`), bildirilmemiş kaza kapatılamaz, düzeltici önlemle kapatma, self-servis ramak kala bildirimi (`ohs.my.incident_reports`), `/app/ohs` ekranı. Kalan: Documents `WorkAccidentReport` gereksinimi (integration event), SGK bildirim hatırlatma job'ı. Özgün plan: `AccidentRecord` (kaza/ramak kala kaydı), `SgkNotificationDeadline` = EventDate + 3 iş günü (`IHolidayCalendarLookup` ile), `AccidentRecord.Create` → integration event → Documents'te `WorkAccidentReport` requirement materialize.
- **Faz 4 — KKD zimmeti**: Assets modülüne additive genişleme (`AssetCategorySeed`'e "ppe" satırı, `AssetCategory.RequiresPeriodicReplacement`/`ReplacementIntervalDays`) — yeni aggregate açılmayacak. ⚠️ Backfill riski: `AssetCategoryProvisioningService.EnsureSeededAsync` tenant başına bir kez çalışıyor, zaten seed'i tamamlanmış tenantlarda "ppe" otomatik görünmeyecek.
- **Faz 5 — Belge motoru entegrasyonu** — ✅ bitti (2026-09-22, 1c8e652c): `IOhsProfileLookup` (çalışan → birim → etkin lokasyon → güncel sınıf); çok tehlikeli işyerinde `IsVeryHazardousJob` ve `RequiresPeriodicHealthCheck` true. Sınır: tehlike sınıfı değişince mevcut çalışanlar otomatik yeniden materialize edilmez. Özgün plan: `DocumentRequirementMaterializationService.cs`'teki `IsVeryHazardousJob`/`RequiresPeriodicHealthCheck` hardcoded `false`'ların düzeltilmesi + `RequiresPeriodicHealthCheck`'in bugün yanlışlıkla `IsUndergroundWorker`'a eşlenen placeholder hatasının giderilmesi (davranış değişikliği — re-materialization notu gerekli, sessiz geçilmemeli). `IOhsProfileLookup` gibi yeni bir cross-module lookup port'u gerekecek.
- **Faz 6 — Stretch**: legislation-parameter taşıma (statik tablo yerine `ILegislationParameterReader`'a geçiş ihtiyacı doğarsa), Analytics/Notifications entegrasyonu, mobil.

---

**Durum: GATE 1 onayı bekliyor. Hiçbir dosya değiştirilmedi/oluşturulmadı.** *(orijinal planın başlangıç notu — yukarıdaki "Implementasyon Notu" bu turun gerçek durumunu yansıtır.)*

## Karar: Yeni Modül + Assets'e Küçük Genişleme

**`src/Modules/OccupationalSafety`** (yeni, şema `ohs`) + Assets modülüne additive KKD genişlemesi.

Gerekçe: İSG kapsamı (tehlike sınıfı geçmişi, RD takvimi, kurul, hekim/uzman görevlendirme, kaza/ramak kala) kendi yaşam döngüsüne sahip 4-5 aggregate barındırıyor — Organization'ın (statik referans verisi) veya Assets'in (basit zimmet döngüsü) doğal uzantısı değil, bağımsız bounded context. KKD ise klasik bir zimmet senaryosu — mevcut olgun `Assets` modülünü genişletmek doğru, yeni aggregate açmak duplikasyon olur.

## Keşif: Altyapı Zaten Hazır

- `Documents.Domain.DocumentTypes.EmployeeDocumentContext`'te `IsVeryHazardousJob`/`RequiresPeriodicHealthCheck` sinyalleri **zaten tanımlı** ama `DocumentRequirementMaterializationService`'te hardcoded `false` — belge motoru İSG'yi bekliyor.
- `DocumentRequirementCondition.RequiresPeriodicHealthCheck` bugün **yanlışlıkla `IsUndergroundWorker`'a eşleniyor** (placeholder hatası) — düzeltme davranış değişikliği yaratacak, dikkatli ele alınmalı.
- `Employee.LocationId`, `IsUndergroundWorker` zaten var; `Organization.Location` tehlike sınıfı taşımıyor.
- `IHolidayCalendarLookup.IsHolidayAsync` — "3 iş günü" SGK bildirimi hesaplaması için hazır yapı taşı.

## Entity Tasarımı (özet)

- `HazardClass` (Low/Medium/High)
- `WorkplaceHazardClassification` — Location bazlı, tarih-aralıklı (aylık güncelleme = zaman içinde değişen değer, mutasyon değil ekleme)
- `RiskAssessment` — Location bazlı, `RenewalPeriodMonths` hazard class'tan türetilir (Az tehlikeli ≤6y, Tehlikeli ≤4y, Çok tehlikeli ≤2y)
- `OhsCommitteeMember` (6331 md.22), `SafetyAssignmentPeriod` (hekim/uzman görevlendirme)
- `AccidentRecord` — kaza/ramak kala, `SgkNotificationDeadline` = EventDate + 3 iş günü (`IHolidayCalendarLookup` ile hesaplanır)

## KKD Zimmeti (Assets Genişlemesi)

Yeni aggregate AÇILMAZ. `AssetCategorySeed`'e "ppe" satırı (append-only) + `AssetCategory`'ye `RequiresPeriodicReplacement`/`ReplacementIntervalDays` (generic alanlar). ⚠️ **Risk:** `AssetCategoryProvisioningService.EnsureSeededAsync` tenant başına bir kez çalışıyor — zaten seed'i tamamlanmış tenantlarda "ppe" otomatik görünmeyecek, backfill migration/script gerekli.

## Belge Motoru Entegrasyonu

- `PersonnelInfoForm`: yeni `IOhsProfileLookup.IsVeryHazardousJobAsync` → `HazardClass==High` kontrolü.
- `PeriodicHealthCheck`: gerçek sinyal hazard≥Medium olmalı; `IsUndergroundWorker` OR yeni sinyal (mevcut davranış korunur, üstüne eklenir) — **davranış değişikliği**, changelog/re-materialization notu gerekli.
- `AccidentRecord.Create` → integration event → Documents `WorkAccidentReport` requirement'ı otomatik materialize eder (mevcut `AssetEventDocumentRequirementHandlers` deseni).

## Aşamalandırma (MVP → tam kapsam, her biri ayrı PR)

0. İskelet (modül projeleri, ModuleCatalog kaydı, NetArchTest) — *gate: build yeşil*
1. Tehlike sınıfı + Risk değerlendirmesi (kartın merge_gate'i) — *gate: demo seed'de yüklü*
2. Kurul + görevlendirme
3. Kaza/ramak kala + Documents entegrasyonu
4. KKD zimmeti (Assets genişlemesi + backfill)
5. Belge motoru entegrasyonu (hardcoded false'ların düzeltilmesi)
6. Stretch: legislation-parameter taşıma, Analytics, Notifications, mobil

## Test Planı

Domain birim testleri (özellikle SGK deadline hesaplaması, hafta sonu/tatil kesişimi), NetArchTest (Contracts-only erişim + permission key konvansiyonu), `DocumentRequirementMaterializationService` regresyon ("OHS profili yoksa false, exception yok" + "HazardClass.High → true"), entegrasyon (AccidentRecorded→Documents), TenantIsolation, demo seed smoke testi.

## Riskler

1. **Yasal doğruluk** — tehlike sınıfı listesi, RD periyotları, hekim/uzman dakika normları, "3 iş günü" ayrımı kanun/tebliğ metniyle birebir doğrulanmalı, tahminle yazılmamalı.
2. **Placeholder düzeltmesi davranış değişikliği yaratır** — sessiz geçmemeli, açık not + re-materialization gerekli.
3. **Assets backfill riski** — mevcut tenantlarda "ppe" otomatik oluşmaz.
4. **Sıcak dosya çakışması** — `ModuleCatalog.cs`, `navigation.ts`, `App.tsx` başka aktif kartlarca da değiştiriliyor olabilir; append-only disiplini şart.
5. **Documents motorunu zorlama riski** — hazard-sınıfına göre değişken periyot Documents'in tek-sabit modeline sıkıştırılmamalı, OHS kendi hatırlatma işinde tutmalı.
6. **Kapsam riski** — 5 bağımsız alt-alan tek karta yığılmış, yukarıdaki faz bölünmesi zorunlu.

## Kritik Dosyalar
- `src/Modules/Documents/Sense.Modules.Documents.Application/DocumentTypes/DocumentRequirementMaterializationService.cs`
- `src/Modules/Documents/Sense.Modules.Documents.Domain/DocumentTypes/DocumentRequirementEvaluator.cs`
- `src/Modules/Assets/Sense.Modules.Assets.Application/Categories/AssetCategoryProvisioningService.cs`
- `src/Modules/Organization/Sense.Modules.Organization.Domain/Locations/Location.cs`
- `src/Modules/Employee/Sense.Modules.Employee.Contracts/IEmployeeLookup.cs`
- `src/Sense.Api/ModuleCatalog.cs`, `web/src/config/navigation.ts`

---
*Kaynak: 2. Tur Test Bulgu Dokümanı § G-03. Agent board: `docs/agent-board/cards/senseik-015-isg-modul.json`.*
