# senseik-015 — İSG Modülü — UYGULAMA PLANI

**Durum: GATE 1 onayı bekliyor. Hiçbir dosya değiştirilmedi/oluşturulmadı.**

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
