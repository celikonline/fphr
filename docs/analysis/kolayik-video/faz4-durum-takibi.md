# Faz 4 Durum Takibi

Kaynak: [11.7-yol-haritasi.md](./11.7-yol-haritasi.md) Faz 4 satırları (kalem #62–#74, + #41/#44/#45'in Faz 4'e düşen tüketici tarafı); ayrıntı [11.3.09-dosya-belge-kvkk-vize.md](./11.3.09-dosya-belge-kvkk-vize.md) (D1 kalemi), [11.3.08-harcama-avans-odemeler-zimmet.md](./11.3.08-harcama-avans-odemeler-zimmet.md) (Ex0 kalemi).

**Son güncelleme:** 2026-09-16
**Nasıl güncellenir:** Kalem durumu değişince satırı koda göre güncelle, tarihi değiştir. Durumlar: ❌ Başlanmadı · 🟡 Kısmi · ✅ Bitti.

**Not (2026-09-16):** Bu oturumda çalışma ağacında eşzamanlı başka oturum(lar)ın da yazdığı çok sayıda commit edilmemiş değişiklik bulundu (yeni `src/Modules/Attendance`, `Payroll`, `Recruitment`, `Analytics`, Employee E4/E5a/Terminations çalışması, Leave `LegislationParameter` genişletmesi, web navigasyon/ana sayfa değişiklikleri, yeni `faz3-durum-takibi.md`/`faz7-durum-takibi.md`) — bu dosya yalnızca aşağıdaki D1/D2 kalemlerini (Documents modülü) izler, diğer eşzamanlı işler kendi durum dosyalarında izlenmeli.

## Faz 4 kalemleri

| # | Kalem | Paket | Durum | Kanıt | Eksik / Not |
|---|---|---|---|---|---|
| 66–68 | Zorunlu belge listesi seed'i (5 yaşam döngüsü aşaması), koşullu zorunlu belge kuralları (`DocumentRequirementRule`), sistem kayıtlarının özlük dosyasına belge olarak düşmesi | D1+D2+D3+D4 | 🟡 Kısmi | Bkz. aşağıdaki "D1"–"D4" bölümleri | Sistem kayıtlarının otomatik belge üretimi (#68 — izin formu/FM onayı/imzalı pusula PDF/Word render + `SourceRef`) yapılmadı; taksonomi + koşullu değerlendirme + manuel karşılama + web arayüzü (Ayarlar, çalışan kartı, rapor) + HR'ın ve çalışanın kendi belgesini yükleyip gereksinime bağlaması var |
| 62 | Harcama: çoklu fiş + vergi oranı + mobil OCR | Ex0 | 🟡 Kısmi | Bkz. aşağıdaki "Ex0" bölümü | Çoklu fiş + vergi hesaplama var; OCR (`IReceiptOcr`), dosya yükleme entegrasyonu (yalnız `ReceiptFileId` string alanı var), web ekranı yok |
| 63 | Avans talebi: para birimi, taksit | Ex0 | 🟡 Kısmi | Bkz. "Ex0" | Alanların hepsi var (tutar/para birimi/tarih/taksit); web ekranı yok |
| 64 | "Kendi hesabım > Ödemeler" birleşik ekran | Ex0 | 🟡 Kısmi | Bkz. "Ex0"/"Ex1" | Self-servis backend projeksiyonu (`GetMyPaymentsOverviewQuery`) + web ekranı (`payments.tsx`) var, yalnız Expense şemasını okuyor (tasarım kararına uygun). Ex1'de eklendi: HR/Finance tarafı — onaylı taleplerin "ödendi" olarak işaretlenmesi (`POST .../pay`) + tenant-wide worklist ekranı (`expense-payments-admin.tsx`). FM ödemesi/PayrollAdjustment satırları hâlâ yok (Leave/Payroll entegrasyonu ayrı paket, bkz. hrteam Faz 7 görev tanımı) |
| 65 | Zimmet şablonu (e-posta bilgilendirme, kimlik no, kategori aktarımda otomatik oluşur, seri no, veriliş/iade tarihi, açıklama) | As0 | 🟡 Kısmi | Bkz. aşağıdaki "As0" bölümü | Kategori (seed: Bilgisayar/Telefon/Araç/Diğer), seri no, veriliş/iade tarihi, açıklama, ata/iade/emekli et var; e-posta bilgilendirme, "aktarımda otomatik oluşur" (DataTransfer şablonu), kategori yönetim ekranı (Ayarlar) yapılmadı |
| 69 | Yardım Merkezi + Desteğe bağlan (Announcements > Support) | — | ❌ Başlanmadı | — | `Announcements` modülü henüz yok |
| 70 | Raporlar menüsü + "Kendin oluştur" + asenkron e-posta | — | ❌ Başlanmadı | — | — |
| 71 | Etkinlik takvimi widget'ı | — | ❌ Başlanmadı | — | Announcements'a bağlı |
| 72 | Ön koşul eğitim + mentör atama | — | ❌ Başlanmadı | — | — |
| 73 | Takdir/teşekkür + yatay pozisyon değişikliği akış öğesi | — | ❌ Başlanmadı | — | — |
| 74 | Onboarding modülü karar noktası | — | ❌ Başlanmadı | — | Faz 4 sonu kararı bekliyor |

## D1 — Documents: belge tipi taksonomisi + koşullu gereksinim değerlendirici (yalnız backend)

**Dal:** `eb6c4eb` (feat(documents): add document type definitions and employee document requirement rules) — commit edildi.

**Kapsam:** `DocumentTypeDefinition` (5 aşamalı taksonomi: Onboarding/Conditional/DuringEmployment/Offboarding/Recommended; tenant başına kopyalanan, düzenlenebilir sistem seed'i — 43 satır, `LeaveType` ile aynı desen), `DocumentRequirementCondition` (12 koşul türü: yaş<18, engellilik, yabancı uyruk, teşvik, engelli vergi indirimi, eski hükümlü/terör mağduru, periyodik sağlık kontrolü, ehliyet gereksinimi, açık zimmet, ağır/tehlikeli iş, FM onayı, vardiyalı çalışma), saf domain servisi `DocumentRequirementEvaluator.ResolveAtHire` (yalnız Onboarding + eşleşen Conditional; DuringEmployment/Offboarding/Recommended işe alımda tetiklenmiyor — olay/sihirbaz güdümlü, kapsam dışı), `EmployeeDocumentRequirement` (çalışan × belge tipi; Missing/Uploaded/Waived, muafiyet nedeni/kim/ne zaman, `Document`'e bağlantı).

**Tetikleyici:** `EmployeeHired` (Employee.Contracts) tüketicisi `EmployeeHiredDocumentRequirementHandler` — kiracının kataloğunu tembel (lazy) seed'ler, `Employee.Contracts.IEmployeeDocumentEligibilityLookup` ile yalnız türetilmiş booleanları (`IsUnder18`, `HasDisability` — ham `BirthDate`/`DisabilityDegree` asla modül sınırını geçmiyor) okuyup geçerli gereksinimleri materialize ediyor; `DueDate = HireDate + 30 gün` (sabit; kiracı bazlı `Documents.MissingDocument.GraceDays` ayarı yapılmadı). Manuel `ReevaluateEmployeeDocumentRequirementsCommand` aynı mantığı mevcut bir çalışan için yeniden çalıştırıyor (HireDate'e erişimi yok, bugünün tarihini taban alıyor — küçük bir tutarsızlık, not düşüldü).

**API:** `documents/types` (liste/oluştur/güncelle/aktifleştir/pasifleştir), `documents/employees/{id}/requirements`, `documents/me/requirements`, `documents/requirements/report` (sayfalı, durum filtresi), `documents/requirements/{id}/{waive,clear-waive,link}`, `documents/employees/{id}/requirements/reevaluate`. İzinler: `documents.document_types.{view,manage}`, `documents.requirements.{view,manage}`, `documents.my.requirements` — mevcut joker izin desenleri (`documents.*`, `documents.*.view`, `*.my.*`) tüm rollere zaten ulaşıyor, `SystemRoleDefinitions.cs`'ye dokunulmadı.

**Doğrulama:** `Sense.Modules.Documents.Tests` (yeni proje) 42/42 test geçti (domain: `DocumentTypeDefinition`/`EmployeeDocumentRequirement`/`DocumentRequirementEvaluator`; application: Create/Update/List/Waive/ClearWaive handler'ları + `EmployeeHiredDocumentRequirementHandler` — eşleşen/eşleşmeyen koşul + idempotency). `dotnet build Sense.slnx` bu değişiklikler için 0 hata verdi. Tüm çözümün `dotnet test`'i eşzamanlı diğer oturumların etkisiyle bu oturumda tamamlanamadı — bir dahaki fırsatta tekrar çalıştırılıp diğer modüllerde (Attendance/Payroll/Recruitment/Analytics/Terminations) regresyon olmadığı doğrulanmalı.

**Eksik / Not:**
- ~~Web arayüzü yok~~ → bkz. aşağıdaki D2.
- #68'in "sistem kayıtları belge olarak düşer" kısmı yapılmadı: `IDocumentRenderer` (PDF/Word), `Document.SourceRef`/`SignatureStatus` alanları, Leave/Workflow olay tüketicileri (izin formu, FM onay belgesi vb.) — Faz 1'in "İzin formu PDF ve Word" kalemiyle birlikte ele alınmalı, ayrı paket.
- Test projesi Postgres/Testcontainers entegrasyon testi değil, NSubstitute mock testleri — modülün geri kalanının `[Collection("PostgreSQL")]` deseniyle tutarlı değil; takip gerekiyor.
- Koşullardan 3'ü (`AgeUnder18`, `HasDisability`, `HasOpenAssets`) artık gerçek veriyle değerlendiriliyor (`HasOpenAssets`: Assets modülünün `IAssetLookup.HasOpenAssetsAsync` sözleşmesi üzerinden, bkz. `faz0-durum-takibi.md`/roadmap #65); kalan 9'u (`NonTurkishNational`, `HasIncentive`, `UsesDisabilityTaxReduction`, `ExConvictOrTerrorVictim`, `RequiresPeriodicHealthCheck`, `RequiresDriving`, `VeryHazardousJob`, `HasOvertimeConsent`, `IsShiftWorker`) ilgili modüller (Employee Nationality alanı, Payroll teşvik, Attendance, Leave FM onayı) gelene kadar `false` sabit — kod içinde açıkça not düşüldü. Not: `DocumentRequirementMaterializationService.MaterializeAsync` yalnızca eksik gereksinimleri ekliyor (append-only); bir koşul sonradan `false`'a dönerse (ör. zimmet iade edilirse) daha önce materialize edilmiş `AssetHandoverForm` satırı otomatik silinmiyor/geri alınmıyor — "kaybolma" davranışı bu paket kapsamında yok, yalnız "belirme" var.
- Grace period (30 gün) ve reevaluate komutunun tarih tabanı sabit kodlanmış; tenant ayarı değil.

## D2 — Documents: web arayüzü (Ayarlar > Belge tipleri, çalışan kartı "Dosyalar", Eksik belge raporu)

**Dal:** commit edilmedi (bu oturumda çalışma ağacına yazıldı).

**Kapsam:**
- `Ayarlar > Şirket > Belge tipleri` (`web/src/pages/settings/document-types.tsx`): D1'deki `DocumentTypesController` için tam CRUD (liste/oluştur/düzenle/aktif-pasif), `termination-reasons.tsx` ile aynı desen (liste + modal dialog, sistem/kiracı rozeti, aşama filtresi).
- `Ayarlar > Şirket > Eksik belge raporu` (`web/src/pages/settings/missing-documents-report.tsx`): `GetMissingDocumentReportQuery` için sayfalı, durum filtreli tablo; çalışan adına tıklayınca ilgili çalışanın "Dosyalar" sekmesine gider.
- Çalışan kartı "Dosyalar" sekmesi (`web/src/components/employees/documents-section.tsx`, `card-tabs.tsx`'e eklendi): çalışanın belge gereksinimlerini durum/aşama/son tarih ile listeler; `documents.requirements.view` varsa `employees/{id}/requirements`, yoksa (`documents.my.requirements`) `me/requirements` çağrılır. HR için Muaf tut (nedenli) / Muafiyeti kaldır / Yeniden değerlendir aksiyonları.
- Servis katmanı: `web/src/services/documents.service.ts`.
- Yeni i18n namespace'i `documents` (tr/en gerçek çeviri, de `[DE] ...` placeholder deseni) + `navigation.json`/`employees.json`'a ilgili anahtarlar.
- `SharedResource(.en).resx`'e D1'in belge tipi/gereksinim hata kodları ve 5 izin adı için lokalize metin eklendi (önceden hiçbiri yoktu, hata mesajları kod olarak dönüyordu).

**Doğrulama:** `pnpm exec tsc --noEmit`, `pnpm exec eslint src`, `pnpm exec vite build` (Docker `node:24-alpine`) hatasız; `dotnet build` ile `Sense.Shared.Infrastructure` (resx değişiklikleri) hatasız.

**Eksik / Not:**
- ~~Belge yükleme + gereksinime bağlama~~ → D3'te kapatıldı.
- ~~Çalışanın kendi belgesini kendi yüklemesi~~ → D4'te kapatıldı.
- ~~Rapor sayfasında arama/dışa aktarma yok~~ → dışa aktarma (CSV) D4'te eklendi (bkz. altında); arama (çalışan adına göre) hâlâ yok — `GetMissingDocumentReportQuery`/`ListReportAsync` bir `q` parametresi almıyor, çalışan adı Documents şemasında değil (cross-module `IEmployeeContactLookup`'tan geliyor), DB seviyesinde filtre için ayrı bir iş.

## D3 — Documents: `initiate-upload` kontrat açığı + "Dosyalar" sekmesinden yükleyip gereksinime bağlama

**Dal:** commit edilmedi (bu oturumda çalışma ağacına yazıldı).

**Kapsam:** D2'nin bilinçli bıraktığı kapsam dışı notu kapatıyor.
- Kök neden küçüktü: `Document` aggregate'i zaten `initiate-upload` sırasında gerçek bir Id ile oluşturulup commit ediliyordu (`InitiateUploadHandler.Handle`, `Document.Create` → `documents.Add` → `UnitOfWorkBehaviour` transaction içinde `SaveChangesAsync`); sadece `InitiateUploadResponse` DTO'su bu Id'yi hiç döndürmüyordu. Düzeltme: `InitiateUploadResponse`'a `DocumentId` alanı eklendi, handler `doc.Value.Id`'yi dönüyor (`DocumentDtos.cs`, `DocumentCommands.cs`). Persistence zamanlamasında değişiklik yok, mevcut testler (43/43) etkilenmedi.
- `web/src/services/documents.service.ts`'e paylaşılan `uploadDocumentFile(file, bucketName)` (initiate → presigned URL'e doğrudan PUT → confirm, gerçek `documentId`'yi döndürür) ve `linkDocumentToRequirement(requirementId, documentId)` eklendi.
- `web/src/services/company.service.ts`'teki `uploadCompanyLogo`, `requestId`'yi `documentId` yerine kullanan eski "best-effort" work-around'dan kurtarılıp paylaşılan `uploadDocumentFile`'a taşındı; artık `confirm-upload` gerçek Id ile güvenilir çalışıyor.
- Çalışan kartı "Dosyalar" sekmesine (`documents-section.tsx`) her eksik/muaf gereksinim satırı için "Yükle" aksiyonu eklendi (gizli `<input type=file>` + `uploadDocumentFile` + `linkDocumentToRequirement`); `documents.files.create` **ve** `documents.requirements.manage` izinlerinin ikisi de gerekiyor (Link komutu `.manage` istiyor, ham yükleme `.files.create` istiyor — HR Admin/Owner'ın `documents.*` joker izni ikisini de zaten kapsıyor).

**Doğrulama:** `dotnet build` + `dotnet test tests/Modules/Sense.Modules.Documents.Tests` (43/43 yeşil) `InitiateUploadResponse` alan eklemesiyle; web tarafı `pnpm exec tsc --noEmit`, `pnpm exec eslint src`, `pnpm exec vite build` hatasız.

**Eksik / Not:**
- ~~Yalnız HR/yönetici tarafı kapatıldı, self-service yok~~ → D4'te kapatıldı.
- `initiate-upload`'ın diğer tüketicisi (şirket logosu) de bu düzeltmeden faydalandı ama o ekran ayrı bir bilinen boşluğa (gerçek `CompaniesController` yok, profil localStorage'da tutuluyor) sahip, dokunulmadı.

## D4 — Documents: çalışanın kendi belgesini kendi yükleyip gereksinimini karşılaması (self-service)

**Dal:** commit edilmedi (bu oturumda çalışma ağacına yazıldı).

**Kapsam:** D3'ün notunu kapatıyor — `documents.files.create`/`documents.requirements.manage` `*.my.*` joker deseniyle eşleşmediği için bir çalışan kendi eksik belgesini yükleyemiyordu.
- Yeni izin `documents.my.files.create` (`DocumentsPermissions.cs`) — Employee self-service rolünün mevcut `*.my.*` include deseni bunu otomatik yakalıyor, `SystemRoleDefinitions.cs`'ye dokunulmadı (yalnız deploy'da `SystemRolePermissionSynchronizer` yeniden senkronlar).
- Backend: `InitiateUploadCommand`/`ConfirmUploadCommand`'ın gövdesi `UploadCore.InitiateAsync`/`ConfirmAsync`'e çıkarıldı (davranış aynı, ham yükleme çalışana özel değil); iki ince self-service komutu eklendi — `InitiateMyUploadCommand`/`ConfirmMyUploadCommand` (`documents.my.files.create`), aynı çekirdeği çağırıyor. `LinkDocumentToRequirementHandler`'ın gövdesi de `LinkCore.LinkAsync`'e çıkarıldı; yeni `LinkMyDocumentToRequirementCommand` (`documents.my.requirements`) `ICurrentUser.EmployeeId`'yi hedef gereksinimin `EmployeeId`'siyle karşılaştırıp uyuşmazsa `Error.Forbidden(employee_document_requirement.not_owner)` döndürüyor (Expense modülünün `expense.my.claims`/`NotOwner` deseniyle aynı, bkz. `ExpenseClaimCommands.cs`).
- API: `documents/me/initiate-upload`, `documents/me/{id}/confirm-upload`, `documents/me/requirements/{id}/link` (`DocumentsController.cs`, `EmployeeDocumentRequirementsController.cs`).
- Web: `documents.service.ts`'e `uploadDocumentFile(file, bucketName, scope)` (`scope: "hr" | "me"`) ve `linkMyDocumentToRequirement`; `documents-section.tsx`'te tek bir `uploadMode` ("hr" HR/admin için tüm eksik+muaf satırlarda, "me" düz çalışan için yalnız eksik satırlarda) — aynı "Yükle" aksiyonu artık hem HR hem çalışanın kendi kartında çalışıyor.
- `SharedResource(.en).resx`'e `documents.my.files.create` izin adı ve `employee_document_requirement.not_owner` hata mesajı eklendi.
- Ek: Eksik belge raporu sayfasına CSV dışa aktarma eklendi (`exportMissingDocumentReportToCsv`, Audit Log'daki `exportAuditEventsToCsv` ile aynı desen — yalnız o an yüklü sayfayı indirir, "tüm filtrelenmiş kayıtları indir" değil).

**Doğrulama:** `dotnet test tests/Modules/Sense.Modules.Documents.Tests` 46/46 yeşil (3 yeni test: `Link_MarksRequirementUploaded_AndReturnsDto`, `LinkMine_OwnRequirement_MarksUploaded`, `LinkMine_AnotherEmployeesRequirement_Fails_AndLeavesStatusUnchanged` — sahiplik kontrolü + durum değişmiyor); `dotnet build` Documents API hatasız; web `pnpm exec tsc --noEmit`, `pnpm exec eslint src`, `pnpm exec vite build` hatasız.

**Eksik / Not:**
- Çalışan yalnız `missing` durumundaki kendi gereksinimini yükleyebiliyor; `waived` bir gereksinimi kendi başına yeniden yükleyip değiştirmesi engellendi (bilinçli — HR'ın muafiyet kararını sessizce ezmesin diye), bu yol yalnızca HR tarafında (`canManage`) açık.
- Self-service akış tarayıcıda uçtan uca (gerçek dosya + presigned URL + S3/MinIO) test edilmedi — yalnız birim testleri ve derleme/tip kontrolleri.

## Ex0 — Expense: harcama talebi + avans + ödemeler özeti çekirdeği (yalnız backend)

**Dal:** commit edilmedi (bu oturumda çalışma ağacına yazıldı). Kullanıcı kararıyla Faz 5 (Performance, başka bir oturumun kilitli `worktree-performance-core` dalında zaten işlenmekte olduğu görülünce) yerine seçildi; `paralel-gelistirme-plani.md` §7 Expense'i "Workflow'dan sonra" (bağımlılık artık karşılanmış durumda) olarak işaretliyor.

**Kapsam:** Recruitment'ın 5 projeli şablonu birebir izlendi (`Sense.Modules.Expense.{Domain,Contracts,Application,Infrastructure,Api}`, şema `expense`). `ExpenseCategory` (tenant başına lazy seed: `expense.category.travel`/`food`, `LeaveType`/`DocumentTypeDefinition` ile aynı desen), `ExpenseClaim` (Draft → çoklu `ExpenseItem` ekle/düzenle/çıkar → Submit; `TotalAmount` sunucuda `Σ Amount`, `ExpenseItem.TaxAmount = Amount − Amount/(1+TaxRate/100)` tutar KDV dahil varsayımıyla), `AdvanceRequest` (tek adımda Submit — yalnızca Tutar zorunlu, para birimi/tarih/taksit opsiyonel varsayılanlı, part1 web videosundaki "yalnız tutar" akışıyla tutarlı). İkisi de `Workflow.Contracts.IWorkflowService.StartAsync` ile onay sürecine giriyor (`RequestType` "ExpenseClaim"/"Advance"); `ApprovalCompleted`/`ApprovalRejected` tüketicileri (`ExpenseClaimApprovalCompletedHandler` vb.) durumunu güncelliyor + kendi outbox'ına `Expense.ClaimApproved` vb. yazıyor (henüz tüketeni yok). `GetMyPaymentsOverviewQuery` ("Kendi hesabım > Ödemeler") tasarım dokümanının "yalnız Expense şemasını okur" kararına birebir uyuyor — Submitted+ durumundaki `ExpenseClaim`'leri ve tüm `AdvanceRequest`'leri tek listede birleştiriyor; FM ödemesi/`PayrollAdjustment` satırları (Leave/Payroll entegrasyonu) ayrı paket.

**Kasıtlı sapmalar / kapsam dışı:** Mobil/web fiş OCR (`IReceiptOcr`) yok — Ai modülü henüz yok; `ReceiptFileId` yalnızca string alan, gerçek dosya yükleme akışı (Documents'ın `initiate-upload`/presigned URL deseni) bağlanmadı. Döviz kuru dönüşümü (`IExchangeRateProvider`) yok — Integrations modülü henüz yok; `Currency` ham ISO kod olarak saklanıyor, `AmountInBaseCurrency` alanı yok. Zimmet (Assets modülü) kapsam dışı — kullanıcı kararıyla yalnız Expense seçildi. `Organization.Contracts.ApprovalRequestTypes`'a "ExpenseClaim"/"Advance" eklenmedi (o dosya bu oturumda başka bir eşzamanlı oturum tarafından Recruitment'ın `Requisition` eklemesi için aktif düzenleniyordu, çakışmayı önlemek için dokunulmadı) — `IWorkflowService.StartAsync`'in kendisi bu listeye bağımlı değil (yalnız Organization'ın birim bazlı politika ataması ekranı bu türleri listeleyemiyor; onay hâlâ varsayılan yönetici/HR rol kuyruğu ile çalışıyor). Onay/ödeme izinleri (`expense.claims.approve`'un fiilen neyi kapsadığı, `expense.claim.pay`/`expense.advance.pay`, `expense.adjustments.*`, `expense.bank_payments.*`) tanımlanmadı — hiçbir özellik henüz onlara ihtiyaç duymuyor.

**İzinler:** `expense.my.claims`, `expense.claims.view`, `expense.claims.approve`, `expense.my.advances`, `expense.advances.view`, `expense.my.payments`, `expense.categories.{view,manage}` — tam olarak `{modül}.{kaynak}.{eylem}` üç bölümlü desen (`Sense.Tests.Architecture.OnionArchitectureTests.PermissionKeys_FollowNamingConvention` bunu zorluyor; self-servis anahtarlar Leave'in `leave.my.requests`/Documents'ın `documents.my.requirements` desenini izliyor — `expense.my.claims.create` gibi 4 bölümlü değil). `SystemRoleDefinitions.cs`'te **hiçbir değişiklik gerekmedi**: bu dosya Expense hiç yokken bile HR/Finance'e `expense.*`, Manager'a `expense.claims.view/approve`, PayrollSpecialist/ExternalAccountant'a `expense.adjustments.*`/`expense.bank_payments.*` (bu pakette kullanılmıyor), Employee'ye `*.my.*` (→ tüm `expense.my.*` anahtarları) vermiş — yukarıdaki anahtarların hepsi bu önceden var olan joker desenlerle birebir eşleşiyor. **Bilinen boşluk:** Intern'e özel `expense.my.advances.*` hariç tutması (4 bölümlü joker) 3 bölümlü `expense.my.advances` anahtarıyla eşleşmiyor (`.` ile biten joker, sonrasında bölüm olmayan tam anahtarı yakalamıyor) — Intern rolü bu paketle **avans self-servisinden fiilen hariç tutulmuyor**; düzeltme `SystemRoleDefinitions.cs`'e dokunmayı gerektiriyor (bu oturumda başka bir eşzamanlı oturum tarafından aktif düzenleniyordu, çakışmayı önlemek için ertelendi).

**Doğrulama:** `dotnet build` (Expense projeleri + `Sense.Api`/`Sense.Migrator`/`Sense.Worker` host'ları) 0 hata; `dotnet test tests/Modules/Sense.Modules.Expense.Tests` 14/14 yeşil (12 domain birim testi + 2 Postgres/Testcontainers entegrasyon testi — `ExpenseItem`'ın client-generated Guid id'si için `ValueGeneratedNever()` gerekliliği Workflow'un `ApprovalTaskConfiguration`'ındaki aynı sınıf hatayı önlüyor mu diye özellikle test edildi); `dotnet test tests/Sense.Tests.Architecture -c Release` 43/43 yeşil (`PermissionKeys_FollowNamingConvention` dahil — ilk denemede 4 bölümlü self-servis anahtarlarla kırmızıydı, 3 bölümlü forma indirgenerek düzeltildi, yukarıdaki "İzinler" notuna bkz.). Migration `20260916040045_InitialExpense` üretildi. Web arayüzü yok (ayrı paket — App.tsx/navigation.ts bu oturumda başka bir eşzamanlı oturumun aktif değişikliği altındaydı, çakışma riskini önlemek için dokunulmadı). Not: bu oturumda Docker üzerinden paylaşılan `bin`/`obj` klasörlerine birçok eşzamanlı oturum aynı anda yazdığı için birkaç build/test denemesi geçici dosya kilidi hatalarıyla başarısız oldu (kod hatası değil) — tekrar denendiğinde geçti.

**Eksik / Not:**
- Web ekranları yok: `/app/requests?type=expense|advance`, `/app/payments`, admin kategori yönetimi, çalışan kartı Ödemeler sekmesi/Borç barı — hiçbiri yapılmadı.
- OCR, döviz kuru, FM→Expense köprüsü, zimmet — yukarıda "kasıtlı sapmalar"da listelendi.
- Vergi oranı seçenek listesi (`LegislationParameter Vat.Rates`) yok; `TaxRate` serbest ondalık alan.
- `Organization.Contracts.ApprovalRequestTypes` genişletmesi (Recruitment R1 ile birlikte, contract-first PR) bekliyor.

## Ex1 — Expense: ödeme yürütme (mark-as-paid) + HR/Finance ödeme iş listesi

**Bağlam:** hrteam paralel takım yapısında "Faz 7" (Masraf/avans/bordro entegrasyonu) görev tanımının ilk dilimi; bu depodaki modül kökü Ex0 ile aynıdır (`Sense.Modules.Expense`), ayrı bir modül açılmadı.

**Kapsam:** Ex0'da domain metodu var ama hiçbir uçtan çağrılmıyordu (`ExpenseClaim.MarkPaid`/`AdvanceRequest.MarkPaid`) — bugüne kadar hiçbir talep üretimde "paid" olamıyordu. Bu paket: (1) her iki `MarkPaid` metodunu `(whenUtc, paidByEmployeeId, paymentNote)` alacak şekilde genişletti (kim ödedi + ödeme notu artık kayıtlı — `PaidByEmployeeId`/`PaymentNote` alanları, migration `20260916150000_AddExpensePaymentTracking`, yalnız 4 nullable kolon ekliyor, geri alınabilir); (2) `PayExpenseClaimCommand`/`PayAdvanceRequestCommand` + `POST /expense/claims/{id}/pay` ve `POST /expense/advances/{id}/pay` uçları (izinler `expense.claims.pay`/`expense.advances.pay`, üç bölümlü); (3) HR/Finance tenant-genelinde worklist sorguları `ListExpenseClaimsQuery`/`ListAdvanceRequestsQuery` (opsiyonel durum filtresi) + `GET /expense/claims`/`GET /expense/advances` uçları (izinler `expense.claims.view`/`expense.advances.view`, zaten Ex0'dan beri tanımlıydı); (4) ödendiğinde yeni integration event'ler `ExpenseClaimPaid`/`AdvanceRequestPaid` (henüz tüketeni yok — Payroll köprüsü için ayrılan yer); (5) web: `web/src/pages/expense-payments-admin.tsx` (`/app/expenses/payments`, sekmeli — Harcama Talepleri/Avans Talepleri, durum filtresi + "Öde" diyaloğu), `expense.service.ts`'e `listExpenseClaims`/`listAdvanceRequests`/`payExpenseClaim`/`payAdvanceRequest`, nav girişi + i18n (tr/en gerçek, de `[DE]` placeholder).

**İzinler:** `SystemRoleDefinitions.cs`'e dokunulmadı — HR (`expense.*`) ve Finance (`expense.*`) rolleri yeni `expense.claims.pay`/`expense.advances.pay` anahtarlarını mevcut tam joker'le otomatik alıyor; Manager'ın `expense.claims.view/approve`'u pay'i kapsamıyor (kasıtlı — onay ile ödeme ayrı yetkiler). Kategori yönetim ekranı (backend zaten tam: `ExpenseCategoriesController` create/list/setActive) bu turda yapılmadı — sıradaki iş.

**Doğrulama:** Bu oturumda da (Ex0'ın Web/As1 notlarındaki gibi) sandbox `docker`/`pnpm`/`git pull` komutlarını onaysız engelliyor — `dotnet build`/`test`, `tsc`/`eslint`/`vite build` **çalıştırılamadı**. Değişiklikler elle satır satır gözden geçirildi: yeni migration + `Designer.cs`/`ModelSnapshot.cs` üçlüsü mevcut `InitialExpense` deseniyle birebir (alfabetik property sırası, snake_case kolon adları); `ExpenseClaimTests`/`AdvanceRequestTests`'teki eski `MarkPaid(DateTime)` çağrıları yeni imzaya güncellendi + `MarkPaid_WhenNotApproved_Fails` testleri eklendi (derlemeden geçtiği doğrulanamadı, yalnız gözden geçirildi).

**Eksik / Not:** Kategori yönetim web ekranı (Ayarlar) yok — backend hazır, sıradaki iş. Attendance/Leave/Payroll köprüleri (bordro girdisi, TB-7) bu pakette yok — ayrı iş, Attendance.Contracts'ta gereken sözleşme henüz tanımlı değil. SGK/e-Bildirge, banka ödeme dosyası, dış bordro export'u — PO karar noktası, bu pakette başlanmadı.

## As0 — Assets: zimmet çekirdeği (kategori, ata/iade/emekli et) + Documents'a `IAssetLookup` bağlantısı

**Dal:** commit edilmedi (bu oturumda çalışma ağacına yazıldı).

**Kapsam:** Expense'in 5 projeli şablonu birebir izlendi (`Sense.Modules.Assets.{Domain,Contracts,Application,Infrastructure,Api}`, şema `assets`). `AssetCategory` (tenant başına lazy seed: `computer`/`phone`/`vehicle`/`other` → "Bilgisayar"/"Telefon"/"Araç"/"Diğer"; `ExpenseCategory`'den farklı olarak `Name` düz metin, kaynak anahtarı değil — kiracı doğrudan yeniden adlandırabiliyor). `Asset` (`Unassigned → Assigned → Unassigned/Retired`; tek güncel atama alanı, geçmiş/defter yok — "açık zimmeti var" = `Status == Assigned`; `Assign` zaten Assigned/Retired iken hata, `Return` yalnız Assigned iken, `Retire` Assigned iken hata verir). `AssignAssetCommand`/`ReturnAssetCommand` işlemi tamamlayınca kendi outbox'ına `AssetAssigned`/`AssetReturned` yazıyor (`IIntegrationEventOutbox.Enqueue`, CLAUDE.md deseni — ayrı bir domain-event `Raise()` katmanı yok, Expense'in `ExpenseClaimApprovalHandlers`'ıyla aynı doğrudan enqueue deseni).

**Documents entegrasyonu (bu paketin asıl amacı):** `Sense.Modules.Documents.Application.csproj`'a `Sense.Modules.Assets.Contracts` referansı eklendi (contracts-only, `Modules_TalkOnlyThroughContracts` mimari kuralına uygun). `DocumentRequirementMaterializationService` artık `IAssetLookup.HasOpenAssetsAsync`'i çağırıp `EmployeeDocumentContext.HasOpenAssets`'i gerçek değerle dolduruyor (önceden sabit `false`). Yeni `AssetEventDocumentRequirementHandlers.cs`: `AssetAssignedDocumentRequirementHandler`/`AssetReturnedDocumentRequirementHandler`, ikisi de aynı `DocumentRequirementMaterializationService.MaterializeAsync`'i (Reevaluate komutuyla aynı, "bugün" taban tarihiyle) çağırıp kaydediyor; `DocumentsServiceCollectionExtensions.AddDocumentsIntegrationEventHandlers`'a eklendi (Sense.Worker'da `EmployeeHiredDocumentRequirementHandler`'ın yanına).

**Host bağlama:** `ModuleCatalog.cs`'e `new AssetsModule()`, `Sense.slnx`'e 5 proje + test projesi, `Sense.Migrator/Program.cs`'e `AssetsDbContext` kaydı, `Sense.Worker/Program.cs`'e `AssetsDbContext` + `AddAssetsCoreServices()` + outbox poller (Expense'in hemen ardına, Documents'ın tükettiği olayları üreten tek yönlü bir üretici modül olarak — kendi tükettiği bir olay yok, bu yüzden Expense'teki gibi yalnız "core services" yarısı var, eşleşen bir `AddAssetsIntegrationEventHandlers` yok).

**İzinler:** `assets.assets.{view,manage}`, `assets.categories.{view,manage}`, `assets.my.assets` — üç bölümlü desen. `SystemRoleDefinitions.cs`'te **hiçbir değişiklik gerekmedi**: HR/HRSpecialist include listeleri Assets hiç yokken bile `assets.*` içeriyordu (Expense'in `ExpensePermissions.cs`'indeki notla aynı önceden-öngörülmüş durum), Employee'nin `*.my.*` joker deseni `assets.my.assets`'i otomatik kapsıyor.

**Kasıtlı sapmalar / kapsam dışı (görev talimatına göre bilinçli):** Kategori yönetim ekranı (Ayarlar) yok — kategoriler yalnız lazy-seed. Atama geçmişi/defteri yok — bir zimmet iade edildiğinde `AssignedEmployeeId`/`IssuedAt` temizleniyor, yalnız `ReturnedAt` "son iade" için kalıyor; bu yüzden `GetEmployeeAssetsQuery`/`ListByEmployeeAsync` yalnız **güncel** atamaları döndürüyor, iade edilmiş geçmiş kayıtları employee bazında listeleyemiyor (spesifikasyonun "history isn't tracked" notuyla tutarlı). E-posta bilgilendirme, DataTransfer aktarım şablonu, atama onay akışı yok. `DocumentRequirementMaterializationService.MaterializeAsync`'in append-only doğası nedeniyle `AssetHandoverForm` gereksinimi bir zimmet iade edildiğinde otomatik silinmiyor/geri alınmıyor (yalnız "belirme" var, "kaybolma" yok) — bu Reevaluate komutunun da önceden sahip olduğu bir sınırlama, bu paket onu miras alıyor.

**Web:** Çalışan kartına "Zimmet" sekmesi (`assets-section.tsx`, `card-tabs.tsx`'e eklendi), `web/src/services/assets.service.ts`, yeni `assets` i18n namespace'i (tr/en gerçek çeviri, de `[DE] ...` placeholder).

**Doğrulama:** Bkz. bu görevin son raporu (build/test sonuçları orada).

## As1 — Offboarding ↔ zimmet bağlantısı ("işten çıkarken açık zimmet var" uyarısı)

**Kapsam:** `Assets.Contracts.IAssetLookup`e `CountOpenAssetsAsync(tenantId, employeeId)` eklendi (var olan `HasOpenAssetsAsync`in yanına, yeni port icat edilmedi — `IAssetRepository.CountOpenAssetsAsync` + `AssetLookup` implementasyonu). `Sense.Modules.Employee.Application` projesine `Assets.Contracts` referansı eklendi; `TerminateEmployeeHandler` artık işten çıkarma sırasında açık zimmet sayısını okuyup `TerminateEmployeeResultDto.OpenAssetCount`'a koyuyor — **JobSecurityEvaluator/CollectiveDismissalEvaluator ile aynı "yalnız uyarı, asla engellemez" deseni**; `Employee.Terminate()` domain metoduna dokunulmadı (okuma tarafında/DTO'da çözüldü). Web: "İşten ayrılış" diyaloğu (`lifecycle-actions.tsx`) artık `assets.assets.view` izni olan kullanıcıya diyalog açılır açılmaz turuncu bir uyarı (`Alert`) ve başarılı çıkıştan sonra bir toast gösteriyor ("N adet iade edilmemiş zimmet var, Zimmet sekmesinden iade edin"); i18n tr/en gerçek çeviri, de `[DE] ...` placeholder.

**Kasıtlı sapmalar / kapsam dışı:** Ayrı bir yapılandırılmış "denetim log" satırı yazılmıyor — Faz 1'in audit-log backend'i bu turda henüz inşa ediliyor, ilerleyen bir turda oraya bir not eklenebilir. Bugünkü kalıcı iz: (a) `TerminateEmployeeResultDto`/API yanıtı, (b) Asset kaydının kendisi — iade edilmeden çıkış yapılan bir çalışanın zimmeti `Status == Assigned` olarak, artık işten ayrılmış bir `AssignedEmployeeId`'ye işaret ederek kalmaya devam eder, ta ki biri iade etsin (bu zaten dolaylı bir iz). Zorunlu engelleme (iade edilmeden çıkış tamamlanamasın) yapılmadı — TALIMAT'ta açıkça "engelleyici olmayan" istendi.

**Doğrulama:** Backend: `AssetLookupIntegrationTests.CountOpenAssetsAsync_CountsOnlyThisEmployeesAssignedAssets` (yeni, Postgres/Testcontainers), `TerminationIntegrationTests.Terminate_WithOpenAssets_SucceedsAndReportsTheCount` (yeni, Postgres/Testcontainers) + iki mevcut termination test dosyasına `IAssetLookup` NSubstitute kaydı eklendi (yoksa DI çözümlemesi kırılırdı). Bu turda `dotnet build`/`dotnet test` bu ortamda **çalıştırılamadı** (sandbox, `docker run`/`docker exec`'i onay gerektirmeden engelliyor — bkz. rapor); değişiklikler satır satır elle gözden geçirildi, aynı dosyadaki mevcut kalıplar birebir izlendi.

## Değişiklik geçmişi

- 2026-09-16: Dosya oluşturuldu; D1 (Documents belge tipi taksonomisi + koşullu gereksinim değerlendirici, backend) tamamlandı.
- 2026-09-16: D2 (web arayüzü: Belge tipleri ayarları, çalışan kartı Dosyalar sekmesi, Eksik belge raporu) eklendi.
- 2026-09-16: D3 (initiate-upload kontrat açığı düzeltildi; Dosyalar sekmesinden yükleyip gereksinime bağlama) eklendi.
- 2026-09-16: Ex0 (Expense modül iskeleti + harcama talebi/avans/ödemeler özeti çekirdeği, backend) eklendi.
- 2026-09-16: D4 (çalışanın kendi belgesini kendi yükleyip gereksinimini karşılaması, self-service) eklendi.
- 2026-09-16: As0 (Assets modül iskeleti: zimmet kategori/ata/iade/emekli et + Documents'ın `HasOpenAssets` koşuluna gerçek `IAssetLookup` bağlantısı) eklendi.
- 2026-09-16: As1 (offboarding ↔ zimmet: `IAssetLookup.CountOpenAssetsAsync` + `TerminateEmployeeHandler`'da engelleyici olmayan uyarı, web çıkış diyaloğunda gösterim) eklendi.
- 2026-09-16: Ex1 (hrteam Faz 7 tur 1: ödeme yürütme — `MarkPaid` uçları + HR/Finance ödeme iş listesi web ekranı) eklendi.
