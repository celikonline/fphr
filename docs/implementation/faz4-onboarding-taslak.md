# Faz 4 — Onboarding/offboarding görev listesi taslağı (K-4)

**Durum:** K-4 kararı çıktı (ADR-0012, Kapı A onaylandı) → `src/Modules/Onboarding/Sense.Modules.Onboarding.{Domain,Application,Contracts}`
kodda mevcut (Domain/Application/Contracts + fake-repo birim testleri, `tests/Modules/Sense.Modules.Onboarding.Tests`).
**Kapı A alan seti, aşağıdaki ilk taslaktan daha dar** - PO'nun TALIMAT'ta verdiği somut sözleşme şuydu (taslaktaki
`Code`/`Name`/`ResponsibleRole` enum/`RequiresApproval` yerine): `OnboardingTaskTemplate(Stage, Title ≤200,
ResponsibleRoleCode string, DueDateOffsetDays int, IsMandatory, IsActive)` + `EmployeeOnboardingTask(EmployeeId,
TemplateId, Stage, DueDate, Status Pending|Completed|Waived, CompletedBy/At, WaivedReason+WaivedBy+WaivedAt)`.
`RequiresApproval`/Workflow bağı, `Code`/ad-hoc görev, çoklu sorumlu rolü ilk sürüm dışı bırakıldı - aşağıdaki
şema/uç önerileri hâlâ **taslak** (Kapı B'de netleşecek), yalnız domain agregatları koda geçti.

## Bugün ne var, ne yok

Bugün "işe giriş/çıkış görev listesi" diye adlandırılabilecek tek somut parça **Documents** modülünün koşullu
belge motoru (`DocumentTypeDefinition` + `EmployeeDocumentRequirement`, bkz. `DocumentRequirementEvaluator.cs`,
`DocumentRequirementMaterializationService.cs`): işe alımda hangi belgelerin toplanması gerektiğini şablon +
per-çalışan materialize edilmiş kayıt olarak tutuyor, `EmployeeHired`/`EmployeeProfileChanged` event'lerini
tüketiyor. Bunun **dışında** genel amaçlı "görev" kavramı (belge dışı: IT hesabı açma, ekipman kurulumu,
oryantasyon eğitimi, bina/kart erişimi, ilk gün toplantısı, çıkışta hesap kapatma vb.) hiçbir modülde yok.
`Assets` çıkışta açık zimmet denetimi yapıyor (bu turda eklendi) ama bu da dar kapsamlı, genel görev listesi değil.

## Önerilen şekil: yeni modül `Onboarding` (şema `onboarding`)

**Öneri:** Documents'a veya Employee'ye eklemek yerine **ayrı bir modül** (`src/Modules/Onboarding/*`, kendi
şema/DbContext/migration seti - CLAUDE.md'nin "one schema + DbContext per module" kuralına uyar).

**Gerekçe (neden Documents/Employee değil):**
- Documents'ın bounded context'i dar ve nettir: "dosya + kanıt toplama" (upload/onayla/sil, tip bazlı gereklilik).
  Belge dışı görevleri (IT hesabı, ekipman, oryantasyon) oraya eklemek modülün sorumluluğunu bulanıklaştırır ve
  `DocumentTypeDefinition`'ı "her türlü onboarding adımı" için aşırı yüklenmiş bir kavrama dönüştürür.
- Employee zaten `Termination` alt-alanına sahip ve büyük bir modül; genel görev takibini oraya eklemek modülü
  daha da şişirir, ve Employee'nin kendi domain kuralı (`Terminate()`) ile "görev tamamlandı mı" durumunu
  karıştırma riski taşır (bu turda tam da bunun tersini yaptık: Assets'in zimmet denetimini Employee'nin
  `Terminate()`'ine sokmadık, ayrı tuttuk - TALIMAT tur 1 md.1 uyarısı).
- Yeni modül, Documents'ın koşullu-belge motoruyla **aynı mimari kalıbı** (şablon + per-çalışan materialize edilmiş
  kayıt, event tüketimi ile tetiklenme) tekrar kullanır - yeni bir desen icat edilmiyor, kanıtlanmış deseni
  genişletiyoruz.

**Alternatif (reddedildi ama not düşülsün):** Documents modülünü "Documents + Checklists" olarak genişletmek daha
az proje/DbContext eklemek anlamına gelir (daha az altyapı yükü) ama iki farklı sorumluluğu (dosya saklama vs.
genel görev takibi) tek şemada karıştırır - modüler monolitin "her modül tek bir şeyin sahibi" ilkesine aykırı.
PO bu değişimi tercih ederse (altyapı yükünü azaltmak için) gerekçeyi burada okuyup karar verebilir.

## Görev şablonu + çalışan görev listesi agregatı

Documents'taki `DocumentTypeDefinition`/`EmployeeDocumentRequirement` ayrımının birebir karşılığı:

```csharp
// Onboarding.Domain — şablon (İK ayarlarından yönetilir, document-types.tsx deseninin karşılığı)
public sealed class OnboardingTaskTemplate : TenantAggregateRoot<Guid>
{
    string Code, Name;
    OnboardingTaskStage Stage;          // Onboarding | Offboarding (Documents'taki DocumentTypeStage ile aynı fikir, ama Conditional/DuringEmployment/Recommended şimdilik kapsam dışı - basit tut)
    OnboardingTaskResponsibleRole ResponsibleRole; // Employee | Manager | Hr | It (kim tamamlayacak/görecek)
    int SortOrder;
    bool IsRequired;
    bool IsActive;
    int DueDateOffsetDays;              // HireDate/TerminationDate'ten kaç gün sonra vadesi dolar
    bool RequiresApproval;              // true ise tamamlanma Workflow üzerinden onaya girer (aşağıya bkz.)
}

// Onboarding.Domain — per-çalışan materialize edilmiş görev
public sealed class EmployeeOnboardingTask : TenantAggregateRoot<Guid>
{
    Guid EmployeeId;
    Guid? TaskTemplateId;               // null = ad-hoc görev (İK elle ekledi, şablonsuz - Documents'ta karşılığı yok, yeni ihtiyaç)
    string Title;                       // TaskTemplateId varsa şablondan kopyalanır, ad-hoc'ta doğrudan girilir
    OnboardingTaskStatus Status;        // Pending | InProgress | Completed | Waived
    Guid? AssignedToUserId;
    DateOnly? DueDate;
    DateTime? CompletedAt;
    Guid? CompletedBy;
    string? WaivedReason;
}
```

`DocumentRequirementMaterializationService`'in karşılığı: `EmployeeHired` geldiğinde `Stage=Onboarding` olan
aktif şablonları çalışana materialize eden bir `OnboardingTaskMaterializationService`; `EmployeeTerminated`
geldiğinde `Stage=Offboarding` olanlar için aynısı.

## Tetikleyiciler

- **`EmployeeHired`** (Employee.Contracts, zaten var, Documents da tüketiyor) → Onboarding şablonlarını materialize et.
- **`EmployeeTerminated`** (Employee.Contracts, zaten var, Assets da bu turda tüketti) → Offboarding şablonlarını materialize et.
- **Diğer modüllerin tamamlama sinyalleri (opsiyonel, 2. dilim):** bazı görevler başka modüllerin event'iyle
  otomatik tamamlanabilir - örn. "Zimmet teslim formu imzalandı" görevi Assets'in `AssetReturned` event'ini
  (bugün yok, gerekirse Assets'e eklenir) tükettiğinde otomatik `Completed` olur. Bu opsiyonel, kapsam dışı
  bırakılabilir - İK elle "tamamlandı" işaretlemesi de yeterli bir ilk sürüm.

## Uçlar (öneri, S-x sözleşme numarası PO onayından sonra atanır)

| Uç | Kim | Amaç |
|---|---|---|
| `GET/POST /api/v1/onboarding/task-templates` | İK/admin | Şablon listele/oluştur (`document-types.tsx` deseni) |
| `PUT/DELETE .../task-templates/{id}` | İK/admin | Şablon güncelle/deaktive et |
| `GET /api/v1/onboarding/employees/{employeeId}/tasks` | İK/yönetici | Çalışan kartı sekmesi (`card-tabs.tsx` slotu) |
| `POST /api/v1/onboarding/employees/{employeeId}/tasks` | İK | Ad-hoc görev ekle (şablonsuz) |
| `POST .../tasks/{id}/complete` | Atanan kişi/İK | Görevi tamamla |
| `POST .../tasks/{id}/waive` | İK | Vazgeç (gerekçeli, Documents'ın `Waive` deseniyle aynı) |
| `GET /api/v1/onboarding/me/tasks` | Çalışan (self-servis) | Kendi görevlerini gör (`documents.my.files` izin deseninin karşılığı, üç segment) |
| `GET /api/v1/onboarding/pending` | İK paneli | Tüm çalışanlarda bekleyen/geciken görevler (missing-documents-report.tsx'in karşılığı) |

## Tablo listesi

- `onboarding.task_templates` — yukarıdaki alanlar + `TenantId`, denetim kolonları (`IAuditLogged` deseni, Document'ın kullandığı).
- `onboarding.employee_tasks` — yukarıdaki alanlar + `TenantId`; indeksler: `(TenantId, EmployeeId)`, `(TenantId, Status, DueDate)` (pending/geciken raporu için, `documents.documents`'ın `(TenantId, ExpiryDate)` indeksiyle aynı gerekçe).
- Ayrı bir audit tablosu **icat edilmiyor** — mevcut `IAuditLogged` + audit interceptor altyapısı (Faz 1) kullanılır.

## Workflow ile ilişki

Çoğu onboarding/offboarding görevi bir onay akışı değil, bir "yapıldı/yapılmadı" işaretlemesidir - bunun için
Workflow'un onay motorunu **zorunlu kılmıyoruz**. Ama `RequiresApproval=true` işaretli görevler (örn: "yöneticinin
ilk hafta değerlendirmesi") tamamlanmak istendiğinde `IWorkflowService.StartAsync("onboarding.task", taskId, ...)`
çağrılır (Faz 1'in mevcut portu, yeniden yazılmaz); onay sonucu Workflow'un yayınladığı
`ApprovalRequestApproved`/`ApprovalRequestRejected` event'i (zaten var) tüketilip görev `Completed`/`Pending`'e
döner. İlk sürümde `RequiresApproval` hiçbir şablonda `true` olmayabilir - alan sadece ileriye dönük genişleme
noktası olarak eklenir, zorunlu kullanım yok.

## G-4-15 — Yasal görev örnekleri (seed önerisi, her biri "doğrulanmalı" etiketiyle)

Kapı B'de `OnboardingTaskTemplate` seed verisine girecek örnekler - mevzuat referansları güncelliği için mutlaka
bir uzman/hukuk danışmanı tarafından **doğrulanmalı**, burada yalnız yön göstermek için yazıldı:

| Şablon | Stage | `DueDateOffsetDays` | Gerekçe | Doğrulanmalı mı |
|---|---|---|---|---|
| SGK işe giriş bildirgesi | Onboarding | **-1** (referans = HireDate, bildirge işe başlamadan **önce** SGK'ya ulaşmalı) | 5510 sayılı Kanun md.8 - sigortalılığın işe başlamadan önce bildirilmesi zorunlu | Evet - istisnai haller (aynı gün bildirim vb.) mevzuatta ayrıca düzenleniyor, tek bir sabit ofset her sektöre uymayabilir |
| SGK işten ayrılış bildirgesi | Offboarding | **+10** (referans = TerminationDate) | 5510 sayılı Kanun - işten ayrılışın 10 gün içinde bildirilmesi | Evet - süre bazı istisnai fesih türlerinde değişebilir |
| Deneme süresi değerlendirmesi | Onboarding | **+60** (referans = HireDate, 4857 sayılı İş Kanunu md.15 azami 2 ay) | Deneme süresi dolmadan önce yöneticinin değerlendirme yapması için hatırlatma | Evet - toplu iş sözleşmesiyle azami 4 aya çıkabilir (md.15), tenant bazlı ayarlanabilir olması gerekebilir |

Not: `DueDateOffsetDays` negatifse "referans tarihinden önce" (Kapı A'daki domain kuralı - bkz. `OnboardingTaskTemplate.DueDateOffsetDays`
XML doc'u), pozitifse "sonra". Yukarıdaki üç örnek de bu tek işaretli-tam-sayı modeliyle ifade edilebiliyor;
ayrı bir "before/after" enum'una gerek kalmadı.

## Bu turda yapılmayan

Kod, migration, DI kaydı, modül iskeleti — hepsi PO'nun K-4 kararından sonra. Karar "yeni modül" yönünde
çıkarsa ilk dilim önerisi: (1) `OnboardingTaskTemplate` + CRUD uçları (İK ayarları), (2) materialize servisi +
`EmployeeHired`/`EmployeeTerminated` tüketicileri, (3) `me/tasks` self-servis ucu, (4) `pending` İK paneli raporu
— her biri Documents'ın koşullu-belge motorunda izlenen sırayla (önce şablon CRUD, sonra event-driven
materialize, sonra rapor).
