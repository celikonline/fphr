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
| Infrastructure | EF konfigürasyonları, repository'ler, `VisaSettingsProvider`, `VisaDocumentDeliveryService` (önce hepsini kopyalar, sonra satırları ekler — yarım kalan teslimde tracker kirlenmez; hedef anahtar `visa-deliveries/{requestId}/{templateId}{uzantı}` — deterministik, kullanıcı metni içermez, yeniden denemede üzerine yazar), `IFileStorage.CopyObjectAsync` + `MinioAdapter` implementasyonu. DI: beş visa portu + `IDocumentRepository` `AddDocumentsCoreServices`'te (API ve Worker ortak), olay işleyicileri `AddDocumentsIntegrationEventHandlers`'ta (Worker). **Worker artık `AddFileStorage` çağırıyor** (`Sense.Worker/Program.cs`, `appsettings.json` `MinIO:*`; docker-compose worker servisinde `MinIO__*` zaten vardı) — başka ortamlarda Worker'a `MinIO__Endpoint/AccessKey/SecretKey` verilmeli, yoksa Worker açılışta hata verir. |
| API | `POST/GET visa-requests`, `GET visa-requests/{id}`, `GET visa-requests/{id}/documents`, `GET visa-processes` (aktifler), `GET/POST/PUT visa-processes/manage…`, `GET/PUT visa-settings` |
| İzinler | `documents.my.visa_requests` (`*.my.*` deseniyle Employee dahil tüm self-service rollere otomatik), `documents.visa.manage` (Owner/Admin `*`, HR `documents.*`). Türkçe görünen ad + `SharedResource{,.en}.resx`. `SystemRoleDefinitions` **değişmedi** (desen yetiyor). |
| Web | `/app/requests/visa` (`web/src/pages/visa-requests.tsx`: liste, form, teslim edilen belgeler), Talep hub'ında Vize kartı aktif, `/app/settings/rules/visa-processes` (`settings/visa-processes.tsx`: onaycı, şablon belge seçimi + yükleme, süreç CRUD), nav girdisi `documents.visa.manage` ile korunuyor, `web/src/services/visa.service.ts`, tr/en/de `documents:visa.*` ve `workflow:requestType.VisaDocument` |
| Migration | `AddVisaSettingsAndDocumentDeliveryFields` (Documents): `visa_settings` tablosu, `documents.employee_id`, `documents.visa_document_request_id`, `visa_document_processes (tenant_id, name)` unique indeksi. `has-pending-model-changes` temiz. `visa_document_requests/processes` tabloları dev'deki `AddSharedCompanyFiles` migration'ında zaten oluşuyor. |

## Doğrulama (dürüst durum)

Docker kapalıydı; **Testcontainers/entegrasyon testleri ve gerçek MinIO/Postgres/Workflow uçtan uca çalıştırılmadı.**

Çalıştırılıp geçenler:
- `dotnet build`: `Sense.Modules.Documents.Api`, `Sense.Api`, `Sense.Migrator`, `Sense.Worker` — 0 hata.
- `dotnet test tests/Modules/Sense.Modules.Documents.Tests` — 180/180 (mock tabanlı: handler, validator, teslim servisi, domain; ayrıca DI kayıt testi — visa servis/işleyicilerinin tüm ctor bağımlılıkları kayıtlı mı — ve `TemplateDocumentIds` jsonb değişiklik izleme (comparer) testi).
- Bağımsız kod incelemesinde (csharp-reviewer) bulunan ve düzeltilenler: visa portlarının DI'a hiç kaydedilmemiş olması + Worker'da `IFileStorage` yokluğu (her `ApprovalCompleted` teslimatını bozardı), şablon listesinin güncellemede kalıcılaşmaması (comparer snapshot referansı paylaşıyordu), kullanıcı metnini taşıyan depolama anahtarı, şablon olarak çalışan klasörü kopyası seçilebilmesi, kayıt hatasında yetim onay (Workflow `CancelAsync` ile telafi), süreç adı kontrolünün DB locale'ine bağlı olması.
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
8. Süreç adı tekilliği: büyük/küçük harfsiz kontrol uygulama katmanında, veritabanında yalnızca birebir aynı ad için unique index (ilk erişimdeki seed yarışına karşı yedek; yarışta ilk ziyaretçi 500 alıp tekrar deneyebilir).
9. Kalıcı teslim hatası (ör. Active bir şablonun depodaki nesnesi silinmişse): outbox `MaxAttempts` sonunda mesajı ölü işaretler ve talep `AwaitingApproval`'da kalır (Workflow tarafı Approved). Geçici hatalar için doğru davranış; kalıcı hata için ayrıştırma (nesne-yok → şablonu atla) `MinioAdapter`'ın hata tipi ayrımı gerektiriyor, yapılmadı.
10. Onaycı `ApproverEmployeeId` ayarda Employee sözleşmesiyle doğrulanmıyor; silinmiş/pasif bir çalışan seçilirse talepler onaylanamaz.
11. `Sense.Worker` başlatılıp uçtan uca denenmedi; yeni `AddFileStorage` çağrısı Worker'ın açılışında MinIO yapılandırması arıyor.

## Sonraki adım

Merge öncesi: CI'da `dotnet test` (Testcontainers dahil) + bir tenant'ta elle akış: ayarlardan onaycı + şablon belge seç → çalışan olarak talep aç → onaycı olarak Gelen Kutusu'ndan onayla → talebin `Completed` olduğunu ve belgelerin indirilebildiğini doğrula.
