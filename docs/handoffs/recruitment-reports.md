# senseik-059 — ATS raporları (Faz 6 / R5, roadmap #94)

**Durum: uygulandı, branch `a/recruitment-reports` (review). Docker'lı testler henüz koşmadı (aşağıya bkz.).**

## Ne var

Recruitment modülü, Raporlar menüsüne üç `IReportProvider` katkıda bulunur
(`src/Modules/Recruitment/Sense.Modules.Recruitment.Infrastructure/Reporting/`, `RecruitmentModule.AddModule` içinde kayıtlı,
Infrastructure csproj `Reporting.Contracts`'a referans veriyor). Hepsi `ReportCategories.Administrative`, Excel çıktısı
(`IReportExporter`), tüm sorgular `TenantId` ile filtreli.

| Anahtar | İzin (sunucuda `RequiredPermission`) | Parametre | İçerik |
|---|---|---|---|
| `recruitment.overview` | `recruitment.applications.read` | isteğe bağlı başvuru tarihi aralığı | İlan başına güncel aşamalara göre başvuru sayısı (Yeni başvuru/Ön eleme/Mülakat/Teklif/İşe alındı/Reddedildi) + toplam |
| `recruitment.daily_counts` | `recruitment.applications.read` | **zorunlu** tarih aralığı | Aralıktaki her gün için yeni başvuru + yeni eklenen aday (sıfır günler dahil) |
| `recruitment.candidates_export` | `recruitment.candidates.read` | isteğe bağlı oluşturulma tarihi aralığı | Ad, soyad, e-posta, telefon, kaynak, etiketler, başvurulan ilanlar (+güncel aşama), oluşturulma tarihi |

İzin denetimi Reporting modülünde yapılıyor: katalog listesi (`ReportQueries`) yetkisiz raporu gizler, `ReportCommands` üretimde yine
`provider.RequiredPermission`'ı doğrular; sağlayıcılar kendi başlarına izin kontrolü yapmaz (Employee/Leave/Attendance
sağlayıcılarıyla aynı desen). Aday dışa aktarımı kişisel veri içerdiği için ayrı ve daha dar bir izne (`candidates.read`) bağlı.
Recruitment'ta birim (org unit) kapsamı yok; kapsam kiracıdır.

Metinler: `SharedResource{,.en}.resx` (`report.col.*`, `report.sheet.recruitment*`, `report.enum.CandidateSource.*`,
`report.enum.ApplicationStage.*`); web: `web/public/locales/{tr,en,de}/reports.json` →
`catalog.recruitment.{overview,daily_counts,candidates_export}` (`reports.tsx` anahtarı `reports:catalog.<key>.label/description` olarak çözer).

## Bilinçli olarak yapılmayanlar (roadmap #94'ün geri kalanı)

- `JobApplication` yalnızca **güncel** aşamayı tutuyor; aşama geçiş zamanı yok. Bu yüzden Kolay İK'nın "aşama geçmişi" aday
  dışa aktarımı varyantı, ortalama aşama geçiş / iletişime geçilme süresi ve roadmap #95 (TimeToFill, aşama bazlı ortalama süre) yok.
- Görüşme/görüşmeci mülakat dağılımı, geri bildirim/öneri dağılımı, arşiv sebepleri, İş sahibi/Görüşmeci çoklu filtresi:
  ilgili domain (görüşme, arşiv sebebi) modellenmedi.
- `RecruitmentMetricSnapshot` (günlük anlık görüntü) ve web'de rapora özel grafik/ekran yok — raporlar mevcut Raporlar
  sayfasının kataloğundan Excel olarak alınır.

## Testler

- Docker'sız: `RecruitmentReportProviderMetadataTests` (5) — anahtarlar, izinler (`candidates.read` vs `applications.read`),
  kategori, tarih aralığı zorunluluğu.
- PostgreSQL (Testcontainers) gerektiren: `RecruitmentReportProviderTests` (6; `Category=Integration`) — günlük sayılar
  (sıfır gün dahil), kiracı izolasyonu (3 rapor), aday satırı içeriği, tarih aralığı filtresi, overview aşama sayıları.
  **Bu oturumda Docker kapalıydı, bu testler çalıştırılmadı** — Docker açıkken
  `dotnet test tests/Modules/Sense.Modules.Recruitment.Tests --filter "FullyQualifiedName~RecruitmentReportProviderTests"`
  çalıştırılmalı.
- Aynı branch'te `RecruitmentIntegrationTests` içinde aday kimliğinin olay kimliğiyle eşleştirilmesi düzeltmesi (provisioning
  testi aday bulamayıp erken dönüyordu) ayrı commit'te; o test de Docker gerektirir ve çalıştırılmadı.

## Doğrulama

`dotnet build` (Sense.Api, Recruitment.Tests) yeşil; Recruitment.Tests `Category!=Integration` 56/56; Tests.Architecture 56/56;
Shared.Infrastructure.Tests 34/34; web `tsc --noEmit` temiz, `eslint src` 0 hata (1 önceden var olan uyarı). Migration yok
(model değişmedi).
