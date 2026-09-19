# Vize belgesi talebi ve süreci — handoff

**Kart**: senseik-001-vize-belgesi (roadmap Faz 1b #31)
**Dal**: `a/visa-document-request`
**Durum**: `review` — dikey dilim uçtan uca kodlandı; Docker'sız doğrulandı (aşağıya bak), entegrasyon çalıştırılmadı.
**Sahip**: PC-A (Documents) — Workflow'a yalnızca `Workflow.Contracts` üzerinden dokunuldu.

## Ne çalışıyor (kodda var)

Akış: çalışan talep eder → tenant'ın tanımlı onaycısına Workflow görevi düşer → onaycı gelen kutusundan karar verir →
`ApprovalCompleted` olayıyla ayarlardaki şablon belgeler çalışanın klasörüne kopyalanır, talep `Completed` olur
(`ApprovalRejected` → `Rejected`).

| Katman | İçerik |
|---|---|
| Domain | `VisaDocumentRequest` (Pending → AwaitingApproval → Approved → Completed / Rejected), `VisaDocumentProcess`, `VisaSettings` (tenant başına tek satır: `ApproverEmployeeId`, `TemplateDocumentIds`), `VisaTypes` (tourist/business/work/student/transit), `Document.CreateDeliveredCopy` (`EmployeeId`, `VisaDocumentRequestId`) |
| Application | `SubmitVisaDocumentRequestHandler` — istekte bulunan **her zaman oturumdaki çalışan** (gövdede `employeeId` yok), onaycı yoksa `visa_document_request.approver_not_configured` ile talep oluşmaz, pasif/olmayan süreç reddedilir, seyahat tarihleri UTC gün olarak saklanır; tek adımlı zorunlu `ApprovalStepInput` ile `IWorkflowService.StartAsync("VisaDocument", …)`. `VisaApprovalCompletedHandler`/`VisaApprovalRejectedHandler` (`IIntegrationEventHandler<ApprovalCompleted/Rejected>`, RequestType filtreli, durum korumalı = idempotent; teslim başarısızsa istisna fırlatıp outbox'a yeniden denetir ve talebe dokunmaz). Sorgular: kendi taleplerim, tek talep (sahiplik; başkasınınki NotFound), teslim edilen belgeler (presigned indirme). Süreç CRUD + aktif/pasif + ilk erişimde 4 varsayılan süreç seed'i. Ayarlar get/update (en çok 20 şablon). |
| Infrastructure | EF konfigürasyonları, repository'ler, `VisaSettingsProvider`, `VisaDocumentDeliveryService` (önce hepsini kopyalar, sonra satırları ekler — yarım kalan teslimde tracker kirlenmez), `IFileStorage.CopyObjectAsync` + `MinioAdapter` implementasyonu. DI: `AddDocumentsCoreServices` (API ve Worker ortak), olay işleyicileri `AddDocumentsIntegrationEventHandlers` (Worker). |
| API | `POST/GET visa-requests`, `GET visa-requests/{id}`, `GET visa-requests/{id}/documents`, `GET visa-processes` (aktifler), `GET/POST/PUT visa-processes/manage…`, `GET/PUT visa-settings` |
| İzinler | `documents.my.visa_requests` (`*.my.*` deseniyle Employee dahil tüm self-service rollere otomatik), `documents.visa.manage` (Owner/Admin `*`, HR `documents.*`). Türkçe görünen ad + `SharedResource{,.en}.resx`. `SystemRoleDefinitions` **değişmedi** (desen yetiyor). |
| Web | `/app/requests/visa` (`web/src/pages/visa-requests.tsx`: liste, form, teslim edilen belgeler), Talep hub'ında Vize kartı aktif, `/app/settings/rules/visa-processes` (`settings/visa-processes.tsx`: onaycı, şablon belge seçimi + yükleme, süreç CRUD), nav girdisi `documents.visa.manage` ile korunuyor, `web/src/services/visa.service.ts`, tr/en/de `documents:visa.*` ve `workflow:requestType.VisaDocument` |
| Migration | `20260919124043_AddVisaSettingsAndDocumentDeliveryFields` (Documents): `visa_settings` tablosu, `documents.employee_id`, `documents.visa_document_request_id` + indeksler. `has-pending-model-changes` temiz. `visa_document_requests/processes` tabloları dev'deki `AddSharedCompanyFiles` migration'ında zaten oluşuyor. |

## Doğrulama (dürüst durum)

Docker kapalıydı; **Testcontainers/entegrasyon testleri ve gerçek MinIO/Postgres/Workflow uçtan uca çalıştırılmadı.**

Çalıştırılıp geçenler:
- `dotnet build`: `Sense.Modules.Documents.Api`, `Sense.Api`, `Sense.Migrator`, `Sense.Worker` — 0 hata.
- `dotnet test tests/Modules/Sense.Modules.Documents.Tests` — 165/165 (mock tabanlı; handler, validator, teslim servisi, domain).
- `dotnet test tests/Sense.Tests.Architecture` — 56/56 (izin görünen adları Türkçe olmalı kuralı dahil). `Sense.Shared.Infrastructure.Tests` 34/34 (hata kodu resx eşitliği dahil).
- `dotnet ef migrations has-pending-model-changes` (Documents) — model değişikliği yok.
- Web: `tsc --noEmit` temiz, `eslint src` 0 hata (employee-list'te önceden var olan 1 uyarı), `vitest run` 554/555 — tek başarısız `i18n.locales.test.ts > attendance.json de placeholder` — bu dalın dokunmadığı dosya (attendance.json), dev'den gelen bir açık.
- `Sense.Tests.Contract` 2 test başarısız (`SharedContractStructureTests`: `IRequest<T>` üyesiz, `IntegrationEvents` tipi yok) — Shared.Contracts yapısıyla ilgili, bu dalın değiştirdiği dosyalara dokunmuyor; dev üzerinde ayrıca doğrulanmadı.

Çalıştırılmayanlar / bilinmeyenler (merge öncesi CI'da bakılmalı):
- Worker'da `ApprovalCompleted` → `VisaApprovalCompletedHandler` gerçek zincirinin (Workflow outbox → EventBus → handler) çalışması; handler DI'ı derleniyor ama Worker hiç başlatılmadı.
- `MinioAdapter.CopyObjectAsync` gerçek MinIO'ya karşı.
- `visa_settings` jsonb Guid listesi dönüşümünün gerçek Postgres'te gidiş-dönüşü.
- Web sayfalarının tarayıcıda elle denenmesi (yalnız jsdom testi + tip/lint).

## Bilinçli kapsam dışı / kalanlar

1. Ret gerekçesi: `ApprovalRejected` olayı gerekçe taşımıyor; `RejectionReason` boş kalır, web "Onaylayıcı talebi reddetti" gösterir. İyileştirme: olaya yorum eklemek (Workflow sahibiyle contract-first) veya Worker'a `IApprovalRequestLookup` kaydı.
2. Onaycının gelen kutusunda vizeye özel özet yok (`IApprovalRequestSummaryProvider` yalnız Leave/Overtime); yalnız "Vize belgesi" tür etiketi görünür.
3. İK/yönetici için tüm vize taleplerini listeleme yok; yalnız çalışanın kendi talepleri (yetki modeli: handler'larda permission sorgusu yok, sahiplik kontrolü var).
4. Şablon seti tenant geneli tek liste; süreç başına ayrı belge seti yok. Ülke → süreç otomatik eşleme yok (çalışan süreci elle seçer, isteğe bağlı).
5. Bildirim: Workflow'un genel talep e-postaları çalışır; vizeye özel metin/bildirim yok.
6. Documents için denetim kaydı (senseik-049) bağlanmadı; `IAuditLogged` işaretli ama kaynak eşlemesi yok.
7. Onaycı = talep eden çalışan durumu ayrıca ele alınmadı (Workflow'un kendi kuralına kalır).
8. Süreç adı tekilliği yalnız uygulama katmanında (büyük/küçük harfsiz); veritabanında unique index yok.

## Sonraki adım

Merge öncesi: CI'da `dotnet test` (Testcontainers dahil) + bir tenant'ta elle akış: ayarlardan onaycı + şablon belge seç → çalışan olarak talep aç → onaycı olarak Gelen Kutusu'ndan onayla → talebin `Completed` olduğunu ve belgelerin indirilebildiğini doğrula.
