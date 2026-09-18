# senseik-016 — Engelli/Eski Hükümlü Kota Raporu ve Uyarı (F-04) — UYGULAMA PLANI

**Durum: BLOCKED (kullanıcı kararı, 2026-09-18) — 4857 m.30/5763 mevzuat kapsamı netleşene kadar implementasyona başlanmayacak. Hiçbir dosya değiştirilmedi/oluşturulmadı.**

## ⚠️ En Kritik Bulgu — Mevzuat Riski

4857 sayılı Kanun m.30, **5763 sayılı Kanun (2008)** ile değişikliğe uğramış ve eski hükümlü/terör mağduru çalıştırma zorunluluğunu **özel sektör işverenleri için kaldırmıştır**; bu zorunluluk günümüzde sadece **kamu işyerlerinde** geçerlidir. Kartın "eski hükümlü/terör mağduru sayısal takip" istemi **sadece istatistiki takip** olmalı, "eksik istihdam/ceza riski" hesabına **dahil edilmemeli** — aksi halde ürün özel sektör kiracılarına olmayan bir yasal yükümlülük üzerinden yanlış idari para cezası uyarısı üretir (tam olarak F-04'ün önlemek istediği riskin tersi). **İmplementasyona geçmeden önce bu ayrım ürün/hukuk sorumlusuyla yazılı teyit edilmeli.**

## Mevcut Kod Durumu (kart metninden farklı noktalar)

- **Sendika üyeliği ZATEN VAR** — `EmployeeUnionMembership.cs`, `Employee.Union.cs`, migration `20260916001301`, izin `employee.union.view`, KVKK md.6 özel kategori olarak şifreli. Kartın bu maddesi kapanmış, tekrar iş yapılmayacak.
- **Engelli sayısı için alan var** (`DisabilityDegree` enum, `Employee.cs:158`), sadece agregat COUNT metodu yok.
- **Eski hükümlü/terör mağduru için gerçek veri YOK** — `DocumentRequirementMaterializationService.cs:71`'de `IsExConvictOrTerrorVictim: false` hardcoded.
- **SGDP/emeklilik durumu yok** — sadece "Emeklilik" bir ayrılış nedeni (aktif çalışan için alan yok).
- **Lokasyon bazlı headcount yok** — Employee'de doğrudan `LocationId` yok, sadece `OrgUnit.LocationId` dolaylı.

## Mimari Desen (mevcut, tekrar kullanılacak)

- `IReportProvider` (Reporting.Contracts) — her modül kendi raporunu Contracts üzerinden katkı verir; kota raporu **Employee.Infrastructure**'da yeni bir provider olarak yaşamalı, Reporting'e değil.
- **"Eşik + advisory uyarı" deseni 2 kez uygulanmış**: `JobSecurityEvaluator.cs`, `CollectiveDismissalEvaluator.cs` — ikisi de tenant-wide `CountActiveAsync()` kullanıyor ve "işyeri bazında olması gerekirdi ama yok" sınırlamasını yorumda belirtiyor. Kota raporu **aynı V1 basitleştirmesini** (tenant-wide) yapmalı, aynı riski not etmeli.
- Aylık uyarı için hazır şablon: `DocumentExpiryReminderJobHandler` (Hangfire, per-tenant recurring) → `IIntegrationEventOutbox.Enqueue` → Notifications `DocumentsEventHandlers.cs` → `NotificationCatalog.cs` şablonu.

## Plan

1. **Girdi:** Toplam çalışan (var, `CountActiveAsync`), engelli sayısı (alan var, `CountActiveByDisabilityAsync` eklenmeli), eski hükümlü/terör mağduru (**yeni alan**: `Employee.IsExConvictOrTerrorVictim`, bool, KVKK md.6 adli veri — en az `employee.sensitive.view` kadar sıkı izin).
2. **Yeni domain servisi:** `QuotaComplianceEvaluator.cs` (Employee.Application) — `JobSecurityEvaluator` deseni. Yeni legislation parametreleri: `Quota.MinHeadcount` (fallback 50), `Quota.DisabledPercent` (fallback 3).
3. **Yeni rapor sağlayıcısı:** `DisabilityQuotaReportProvider.cs` (Employee.Infrastructure) — yeni permission `employee.quota.view` (sadece company-scope roller, Analytics'teki "yanlış/scope'lanmamış sayı gösterme yerine gösterme" ilkesiyle).
4. **Dashboard entegrasyonu:** Analytics `WorkforceKpiFeatures.cs`'e 7. KPI — mevcut KPI tile pipeline'ına oturur, yeni FE bileşeni gerekmez.
5. **Aylık bildirim:** `DisabilityQuotaReminderJobHandler` (yeni, `DocumentExpiryReminderJobHandler` şablonu) → cron `"0 6 1 * *"` → `DisabilityQuotaShortfallDue` event → Notifications (Email+InApp, `IsMandatory` kararı PO ile netleştirilmeli).

## Riskler

1. **Mevzuat kaynağı doğrulaması** (yukarıda, en kritik).
2. **Yuvarlama kuralı netliği** (%3'ün ceiling/floor/round'u — resmi tebliğ referans alınmalı, varsayımla kodlanmamalı).
3. **Headcount kapsamı** (işyeri vs tenant) — V1 tenant-wide basitleştirmesi yanlış sayı üretebilir, raporda açıkça belirtilmeli.
4. **Geçmiş dönem hesabı yok** — rapor "an itibariyle" hesaplar, geçmiş ay için gerçek değeri vermez.
5. **Yeni hassas alanın KVKK riski** — adli sicil bilgisi disability'den bile hassas, şifreli saklama değerlendirilmeli (union'daki AES-GCM deseni gibi).
6. **Bildirim "IsMandatory" kararı** — ceza riski taşıyan bir konu için kapatılabilir mi, PO netleştirmeli.

## Test Planı

`QuotaComplianceEvaluatorTests.cs` (yeni) — 50 eşiği geçiş, engelli sayısı senaryoları (yuvarlama), legislation override, **eski hükümlü sayısının shortfall hesabına karışmadığını doğrulayan regresyon testi** (riskin kod seviyesinde sabitlenmesi). Rapor sağlayıcı testi, izin/scope testi, job handler testi.

## Kritik Dosyalar
- `src/Modules/Employee/Sense.Modules.Employee.Application/Employees/CollectiveDismissalEvaluator.cs` (şablon)
- `src/Modules/Employee/Sense.Modules.Employee.Domain/Employees/Employee.cs`, `EmployeeProfileEnums.cs`
- `src/Modules/Documents/Sense.Modules.Documents.Application/DocumentTypes/DocumentRequirementMaterializationService.cs`
- `src/Modules/Employee/Sense.Modules.Employee.Infrastructure/Reporting/EmployeeListReportProvider.cs` (şablon)
- `src/Modules/Documents/Sense.Modules.Documents.Infrastructure/BackgroundJobs/DocumentExpiryReminderJobHandler.cs`
- `src/Shared/Sense.Shared.Contracts/Legislation/ILegislationParameterReader.cs`
- `src/Modules/Analytics/Sense.Modules.Analytics.Application/Kpis/WorkforceKpiFeatures.cs`

---
*Kaynak: 2. Tur Test Bulgu Dokümanı § F-04. Agent board: `docs/agent-board/cards/senseik-016-kota-raporu.json`.*
