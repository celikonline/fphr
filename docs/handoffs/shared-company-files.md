# senseik-028 — Ortak Şirket Dosya Paylaşımı — UYGULAMA NOTU

## Durum

GATE 1 ve GATE 2 (yerel commit) onayı zaten verilmişti (talimat gereği durmadan uygulandı). Branch `a/shared-company-files`. **PUSH yapılmadı.**

## Ne yapıldı

Sıfırdan yeni bir özellik: tenant-geneli (veya belirli rollere kısıtlı) ortak dosya deposu. Mevcut `Document` (43 sistem tanımlı özlük belge tipi, her zaman bir çalışana bağlı) ile karıştırılmadı — ayrı bir aggregate (`SharedCompanyFile`) olarak Documents modülünün altında yeni bir alt-alan (`SharedFiles`) şeklinde eklendi.

### Kullanılan depolama altyapısı

Yeni bir storage entegrasyonu YAZILMADI. Documents modülünün zaten kullandığı `Sense.Shared.Contracts.Storage.IFileStorage` (MinIO tabanlı `MinioAdapter`, presigned URL akışı: initiate → istemci doğrudan MinIO'ya PUT → confirm) doğrudan tekrar kullanıldı. Sadece mantıksal bucket adı farklı: `SharedCompanyFile.BucketName = "shared-files"` (Document'ın `"documents"` bucket'ından ayrı, tenant-namespaced aynı şekilde).

### Backend

- **Domain** (`src/Modules/Documents/Sense.Modules.Documents.Domain/SharedFiles/`):
  - `SharedCompanyFile` aggregate: `FileName, FileKey, ContentType, Size, UploadedBy, UploadedAt, ETag, Status (Pending/Active/Deleted), Visibility (Everyone/RestrictedToRoles), AllowedRoles`. `Create/ConfirmUpload/Delete/IsVisibleTo(callerRoles)` — Document'ın presigned-URL yaşam döngüsüyle bire bir aynı desen.
  - `SharedFileQuota` — saf değer tipi (`Calculate(usedBytes, quotaBytes)`, `WouldExceed(additionalBytes)`), DB'siz test edilebilir.
  - `SharedFilesDomainErrors` / `SharedFilesLimits` (dosya başına 100 MB, kota sabiti 100 MiB — kart örneğindeki "104.86 MB" ile eşleşiyor).
- **Application** (`.../Application/SharedFiles/`): `InitiateSharedFileUploadCommand` (kota kontrolü burada — `SumActiveSizeByTenantAsync` + `SharedFileQuota.WouldExceed`), `ConfirmSharedFileUploadCommand`, `DeleteSharedFileCommand`, `ListSharedFilesQuery` (görünürlük filtresi — caller'ın rollerine göre bellekte filtrelenir, bkz. aşağıdaki kapsam notu), `GetSharedFileQuery`, `GetSharedFileQuotaQuery`.
- **Infrastructure**: `SharedCompanyFileConfiguration` (EF, `AllowedRoles` virgülle ayrılmış string olarak saklanıyor — rol kodları virgül içermiyor), `SharedCompanyFileRepository`, `DocumentsDbContext`'e `DbSet<SharedCompanyFile>` eklendi.
- **API**: `SharedCompanyFilesController` — `api/v1/documents/shared-files` (liste, tekil+indirme URL'i, kota, initiate-upload, confirm-upload, sil).
- **Migration**: gerçek `dotnet ef migrations add AddSharedCompanyFiles` (elle yazılmadı) — `shared_company_files` tablosu eklendi. **Yan etki**: bu migration ayrıca `visa_document_processes`/`visa_document_requests` tablolarını da oluşturdu — bu tablolar senseik-001 (vize belgesi) kartının EF konfigürasyonu `DocumentsDbContext.OnModelCreating`'e daha önce eklenmiş ama hiç migration'ı üretilmemişti (bkz. `docs/handoffs/visa-document-request.md`: "Migration üretilmedi — tablolar DB'de yok"). `dotnet ef migrations add` her zaman canlı modeli son snapshot'a göre diff'lediği için bu boşluk bu modülde üretilecek herhangi bir migration'a yapışacaktı; snapshot'ı elle "yalan söyler" hale getirmemek için (visa tabloları modelde var ama migration geçmişinde yok gibi görünsün) EF'in üretimine dokunulmadı — migration dosyasında bunu açıklayan bir XML doc yorumu var. senseik-001'in application/API/web katmanlarına dokunulmadı, hâlâ bloke durumda.

### Permission kararı — önemli sapma

Görev talimatı örnek olarak `documents.sharedfile.manage` / `documents.sharedfile.view` öneriyordu, ama repoda tarama yapınca `SystemRoleDefinitions.cs`'de Employee ve Intern rollerinin include listesinde **zaten** `"documents.company.view"` anahtarı vardı (katalogda karşılığı yoktu — muhtemelen bu özellik için önceden ayrılmış bir yer tutucu). Bu anahtar aynen kullanıldı (`DocumentsPermissions.CompanyFilesView`), yönetim izni için de eşleşen `documents.company.manage` eklendi. Sonuç: **`SystemRoleDefinitions.cs`'de hiçbir değişiklik gerekmedi** — Owner/Admin/HR/HRSpecialist zaten `documents.*` joker deseniyle her ikisini de alıyor, Manager/PayrollSpecialist/Recruiter/Auditor `documents.*.view` deseniyle sadece view'i alıyor, Employee/Intern açıkça `documents.company.view`'a sahipti (görüntüleyebilir, yükleyemez/silemez — kartın "sadece HR/Admin yükleyebilir" kabul kriteriyle birebir örtüşüyor).

### Web

- `web/src/services/shared-files.service.ts` — `documents.service.ts`'teki `uploadDocumentFile` presigned-URL akışının aynısı.
- `web/src/pages/shared-files.tsx` — dosya listesi (ad/tür/boyut/yükleme tarihi/görünürlük rozet), kota Progress bar'ı, yükleme diyaloğu (Herkese açık / Role kısıtlı + `MultiSelect` rol seçici, `listRoles()` ile), silme (`ConfirmDialog`).
- `web/src/App.tsx` — `/app/shared-files` route'u (lazy).
- `web/src/config/navigation.ts` — **SICAK DOSYA**: `NAV_ITEMS` dizisinin sonuna yeni `sharedFiles` girdisi eklendi (mevcut sıralama bozulmadı), ikon import listesine de sona `FolderOpen` eklendi.
- i18n: `tr/en/de` — `navigation.json` (`nav.sharedFiles`) ve `documents.json` (`sharedFiles.*` bölümü, tüm 3 dilde anahtar/interpolasyon paritesi `i18n.locales.test.ts` ile doğrulandı).

### Demo seed

`src/Sense.Api/SeedData/DataSeeder.SharedFiles.cs` (yeni partial dosya, `DataSeeder.cs`'e tek satır çağrı eklendi): 3 herkese açık örnek dosya + 1 role kısıtlı örnek (`Bordro Hesaplama Şablonu.xlsx`, sadece HR/Admin) — gerçek dosya yüklemeden, sadece metadata (Document'ın `SeedSampleDocumentsAsync`'i ile aynı desen).

## MVP kapsamı dışı bırakılanlar

- **OrgUnit'e göre görünürlük kısıtlaması**: kart hem "role" hem "OrgUnit" diyor; sadece rol bazlı kısıtlama uygulandı. `ICurrentUser`'da OrgUnit bilgisi yok (sadece `EmployeeId`), Organization modülüyle çapraz sorgu gerektirirdi — zaman kısıtı nedeniyle ertelendi. `SharedFileVisibility` enum'u genişletilebilir (`RestrictedToOrgUnit` eklenebilir) ama şu an sadece `Everyone`/`RestrictedToRoles` var.
- **Gerçek per-tenant yapılandırılabilir kota**: `TenantSettings`'te sayısal ayar altyapısı yok (sadece boolean `Access.*` ayarları var, bkz. `AccessSettingKeys`). Kota `SharedFilesLimits.DefaultQuotaBytes` sabiti olarak eklendi (100 MiB, tüm tenant'lar için aynı). Gerçek per-tenant ayarlanabilir kota ayrı bir kart.
- **Dosya önizleme / versiyon geçmişi**: yok, kapsamda değildi.

## Testler (hepsi Docker SDK konteynerinde doğrulandı)

- `dotnet build Sense.slnx` — 0 hata.
- `dotnet test tests/Modules/Sense.Modules.Documents.Tests` — 116/116 yeşil (38'i yeni: `SharedCompanyFileTests` 17, `SharedFileQuotaTests` 7, `SharedFileCommandsTests` 8, `SharedFileQueriesTests` 6).
- `dotnet test tests/Sense.Tests.Architecture` — 52/52 yeşil (modül sınırı kuralları bozulmadı).
- `dotnet test tests/Shared/Sense.Shared.Infrastructure.Tests` (`ErrorResourceParityTests`) — 2/2 yeşil (TR/EN resx anahtar paritesi, yeni hata kodları için).
- `dotnet test tests/Sense.Tests.Contract` — 5/7 yeşil, **2 ilgisiz pre-existing hata** (`PortInterfaces_ArePublicAndAccessible`, `IntegrationEventContracts_ArePublicAndAccessible` — `Sense.Shared.Contracts.Messaging.IRequest\`1` ve genel `IntegrationEvents` tipiyle ilgili, Documents/SharedFiles'a hiç değinmiyor; ilgili test dosyasının son commit mesajı zaten "test kırmızı" diyor — bu kartın dokunmadığı, önceden bilinen kırmızı testler).
- Web: `pnpm exec tsc --noEmit` temiz, `pnpm exec eslint src` temiz (1 ilgisiz pre-existing uyarı, `employee-list.tsx`), `pnpm exec vite build` başarılı (`shared-files` chunk üretildi), `pnpm exec vitest run src/i18n.locales.test.ts` — 162/163 yeşil, **1 ilgisiz pre-existing hata** (`de/attendance.json` `teamTimesheet.week/month` çevrilmemiş placeholder — bu kartın dokunmadığı dosya).

## Değişen/eklenen dosyalar (özet)

Backend: `SharedCompanyFile.cs`, `SharedFileQuota.cs`, `SharedFilesDomainErrors.cs` (Domain); `SharedFileDtos.cs`, `SharedFileCommands.cs`, `SharedFileQueries.cs`, `SharedFilesErrors.cs` (Application); `SharedCompanyFilesConfiguration.cs`, `SharedCompanyFileRepository.cs`, migration çifti (Application/Infrastructure); `SharedCompanyFilesController.cs` (Api); `DocumentsPermissions.cs`, `Repositories.cs`, `DocumentsDbContext.cs`, `DocumentsModule.cs` güncellemeleri; `SharedResource{,.en}.resx` yeni anahtarlar; `DataSeeder.SharedFiles.cs` (yeni) + `DataSeeder.cs` tek satır çağrı; test dosyaları `tests/Modules/Sense.Modules.Documents.Tests/SharedFiles/*`.

Web: `shared-files.service.ts`, `shared-files.tsx` (yeni); `App.tsx`, `navigation.ts` güncellemeleri; `tr/en/de` `navigation.json`+`documents.json` güncellemeleri.
