# Faz 10 — İlk Sürüm Yayın Kontrol Listesi (Backend perspektifi)

Kaynak: `senseik-takim/status/faz-10/rapor-be-20260917-1011.md` (tur 5). Soru: bugün `main`'e push edilse
hangi adımlar eksik kalır? Statik kod taramasıyla üretildi (migration klasörleri, `Sense.Worker`/`Sense.Api`/
`Sense.Migrator` modül kayıtları, CI workflow tetikleyicileri, `ReportingPermissions.cs`, `appsettings.json`,
`SystemRoleDefinitions.cs`).

## Durum özeti

| Durum | Maddeler |
|---|---|
| ✅ Hazır, ek iş yok | 7, 8, 11 |
| 🧑 İnsan bekliyor | 1, 5, 6, 10 · 2'nin migration üretim erişimi · 9 (K-10 bağımlılık/ADR kararı) |
| Faz bekliyor | 2 → faz-01 BE (ConsentRecords EF grubu) + faz-04 BE (Onboarding Kapı B) · 4 → faz-01 BE |

## Madde madde

1. **Kapanış script'i / CI tetikleyicileri kapalı** — `pr.yml`/`main.yml` yalnız `workflow_dispatch` ile
   çalışıyor (`on: push/pull_request` satırları yorum). Push sonrası otomatik CI koşmaz. **Kim: 🧑 insan**
   (PO/DevOps kararı — tetikleyicileri açma zamanı geldiğinde tek satırlık yorum kaldırma).
2. **Migration kuyruğu** — `Identity.AddConsentRecords` ve `Onboarding.InitialOnboarding` henüz üretilmedi
   (Onboarding'in hâlâ Infrastructure/DbContext'i yok — Kapı A, DB'siz — bilinçli). `Organization.AddCostCenters`
   ve `Reporting.InitialReporting` **main'de mevcut**, bu ikisi hazır. **Kim: 🧑 insan** (migration üretim
   erişimi) + **faz-01 BE** (ConsentRecords EF grubu) + **faz-04 BE** (Onboarding Kapı B).
3. **Zamanlanmış rapor gönderim job'ı henüz yok** — `Sense.Modules.Reporting` içinde `IReportProvider`,
   `ReportExecution`, `ReportMailer` (on-demand e-posta) var; ama recurring/cron job sınıfı yok ve
   `Sense.Reporting*` **`Sense.Worker`'a hiç kayıtlı değil** (yalnız `Sense.Api`/`Sense.Migrator`'da kayıtlı).
   Faz 10'un kapsamındaki "zamanlanmış rapor gönderim" ilk sürümde eksik kalır — bugünkü hâliyle yalnız
   talep-üzerine (on-demand) rapor çalışır. **Kim: faz-10 BE** (backlog maddesi).
   > **Karar (ADR-0026, PO tur 6): ilk sürüm kapsamı dışı — backlog B-10-05.** Gerekçe: on-demand zincir
   > (`ReportMailer`) çalışıyor, prod SMTP ayarı zaten yok (md.6) → job yazılsa da ilk sürümde e-posta
   > gönderemez; ayrıca `BackgroundJobSeedSynchronizer` (G-1-13) henüz doğrulanmamışken üstüne ikinci bir
   > zamanlanmış-iş altyapısı yığmak risk katlar.
4. **KVKK md.7 saklama/imha job'ı üretimde koşmuyor** — backlog #11 (bu fazın kapsamı dışı, faz-01 BE
   üzerinde çalışıyor: `BackgroundJobSeedSynchronizer`). Bugün push edilirse `CandidateRetentionSweep`/
   `DocumentExpiryReminder` seed edilmemiş kalır. **Kim: faz-01 BE**.
5. **Sentry DSN boş** (`appsettings.json`: `"SentryDsn": ""`) — prod ortam değişkeni set edilmeden hata izleme
   çalışmaz (SDK entegre ama sessiz no-op). **Kim: 🧑 insan** (ortam değişkeni/secret girişi, kod değişikliği
   gerektirmiyor).
6. **SMTP prod ayarı yok** — varsayılan `127.0.0.1:1025` (yerel Mailhog/dev). `ReportMailer` ve bildirim
   e-postaları prod'da gerçek bir SMTP sağlayıcısı/env değişkeni bekliyor. **Kim: 🧑 insan** (ortam değişkeni).
7. **Sağlık ucu (health-check) mevcut** — `Sense.Api/HealthChecks/*` (Minio dahil) kodda var, ek iş yok.
   **Kim: yok (hazır)**.
8. **CI test filtresi doğru** — `pr.yml`/`main.yml` her ikisi de `--filter "Category!=Integration"` (unit) +
   `--filter "Category=Integration"` (integration) adımlarını ayrı ayrı çalıştırıyor; asıl CI (kapanış
   script'inden ayrı) zaten doğru yazılmış (bkz. `faz10-ci-test-filtresi.md`). **Kim: yok (hazır)**.
9. **K-10 bağımlılık açıkları** (Scriban 6.6.0, System.Drawing.Common 5.0.0 — PANO'da bilinen kritik) —
   sürüm yükseltmesi PO kararı gerektirir (yeni bağımlılık/versiyon = bloker konusu, BE tek taraflı yapamaz).
   **Kim: 🧑 insan/PO** (ADR + üst versiyon onayı).
10. **Sürüm etiketi (release tag) yok** — repoda henüz bir sürüm tag'i akışı/`CHANGELOG` süreci gözlenmedi.
    İlk sürüm için `v0.1.0` gibi bir etiketleme adımı CI/CD'ye eklenebilir. **Kim: 🧑 insan** (release süreci
    kararı, kod değişikliği değil).
11. **Reporting izin anahtarları** — `ReportingPermissions` (`reports.catalog.view`, `reports.executions.generate`,
    `reports.my.executions`) wildcard'larla (`reports.*`, `*.my.*`) mevcut sistem rollerine **zaten** kapsanıyor
    (kod içi yorum bunu belgeliyor) — ek senkronizasyon adımı gerekmiyor. **Kim: yok (hazır)**.

## İlgili belgeler
- `docs/implementation/faz10-ci-test-filtresi.md` — CI/kapanış script'inin beklediği test filtresi.
- `docs/implementation/faz10-analytics-kpi-sozlesmesi.md` — Analytics KPI sözleşmeleri (S-15/S-17, S-21).
