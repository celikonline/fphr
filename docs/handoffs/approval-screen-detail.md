# senseik-047 — Onay Ekranı Detay Bilgisi + Onay Kutusu Keşfedilebilirliği — UYGULAMA NOTU

**Durum: Uygulandı (GATE 1/GATE 2 onaylandı). `b/approval-screen-detail` dalında commit edildi; push yapılmadı.**

## Kök Neden

`ApprovalRequest.SubjectId` Workflow modülüne göre opak bir GUID'dir (ilgili modülün kendi kaydına referans - `LeaveRequest.Id`/`OvertimeRequest.Id`). Workflow bilinçli olarak bu id'yi hiç çözümlemiyordu (modüller arası sınır), bu yüzden hem `/app/inbox` listesinde hem "Karar ver" modalında kullanıcıya ham UUID gösteriliyordu (D-01, YÜKSEK - yönetici kör onay veriyor). Ayrıca onay kutusu sadece Uygulamalar sayfası + ana sayfa bileşeninden erişilebiliyordu, sol menüde yoktu (D-03, ORTA).

## Çözüm — Backend

- **`Sense.Modules.Workflow.Contracts/IApprovalRequestSummaryProvider.cs`** (yeni): `IApprovalRequestSummaryProvider` portu + `ApprovalRequestSummary`/`OvertimeApprovalSummary`/`LeaveApprovalSummary` DTO'ları. Desen: Workflow.Contracts kendi sözleşmesini tanımlıyor, üreten modül (bugün Leave) bunu implemente edip DI'a `IApprovalRequestSummaryProvider` olarak kaydediyor — Workflow.Application hiçbir zaman Leave'e referans vermiyor, sadece `IEnumerable<IApprovalRequestSummaryProvider>` enjekte edip `RequestType`'a göre eşleştiriyor. Modüller arası konuşma kuralına (`*.Contracts` üzerinden) tam uyumlu; yeni bir talep türü (Advance/Expense/Requisition) eklendiğinde yalnızca o modül kendi provider'ını kaydeder, Workflow'a dokunulmaz.
- **`ListInboxHandler`** (`ApprovalRequestQueries.cs`): `IEnumerable<IApprovalRequestSummaryProvider>` enjekte edildi; her `ApprovalRequest.RequestType` için eşleşen provider bulunup `GetSummaryAsync(tenantId, SubjectId)` çağrılıyor, sonuç `InboxTaskDto.Summary`'ye yazılıyor (eşleşen provider yoksa `null` - Advance/Expense/Requisition bugün için).
- **`LeaveApprovalRequestSummaryProvider` / `OvertimeApprovalRequestSummaryProvider`** (`Leave.Infrastructure/Lookups/ApprovalRequestSummaryProviders.cs`, `LeaveServiceCollectionExtensions.AddLeaveCoreServices`'e kayıtlı): `LeaveDbContext` üzerinden read-only (`AsNoTracking`) sorgu - Leave için tarih aralığı+tür+gün sayısı, Overtime için tarih+saat+süre+gerekçe. `LeaveTypeCode`, web'in `leave:code.*` çeviri anahtarlarıyla eşleşsin diye elle camelCase'e çevriliyor (`ToCamelCase` - bu alan Workflow.Contracts sınırını plain `string` olarak geçtiği için paylaşılan `JsonStringEnumConverter(CamelCase)` onu hiç görmüyor, ayrıntı dosyanın XML yorumunda).
- **Test**: `tests/Modules/Sense.Modules.Workflow.Tests/ListInboxHandlerTests.cs` - iki yeni test: eşleşen provider varken `Summary` doluyor (ve eşleşmeyen provider hiç çağrılmıyor), eşleşen provider yokken `Summary` null kalıyor. Mevcut `SubmittedAt` testiyle birlikte 3/3 yeşil; tüm modül testleri (Testcontainers dahil) 31/31 yeşil.

## Çözüm — Frontend

- **`web/src/services/workflow.service.ts`**: `InboxTaskDto.summary?: ApprovalRequestSummaryDto` + `OvertimeApprovalSummaryDto`/`LeaveApprovalSummaryDto` tipleri eklendi.
- **`web/src/pages/inbox.tsx`**:
  - Onay kutusu kartı (`ApprovalCard`): `summaryFields()` yardımcı fonksiyonu, `task.summary`'den talep türüne özel alanları üretiyor (fazla mesai: talep tarihi, başlangıç saati, süre - `useMinutes()` ile "2 sa" formatı -, gerekçe; izin: talep tarihi, tarih aralığı, izin türü - `useLeaveName()`/`leave:code.*` ile aynı çözümleme, `leaveTypeName` yoksa koda düşüyor -, gün sayısı - `useDays()`). Gönderim tarihinin (`submittedAt`) yanına talebin kendi tarihi (`summary.requestDate`) ayrı bir alan olarak ekleniyor.
  - **"Karar ver" modalı (`DecideDialog`)**: kartla AYNI `fields` prop'u alıyor ve başlık altındaki `· {task.subjectId}` satırı tamamen kaldırıldı, yerine detay alanları küçük bir `Card` içinde listeleniyor - kartın kendisi kadar önemliydi çünkü asıl "kör onay" hatası burada, karar butonlarının hemen üstündeydi.
  - `ApprovalCard`'ın kendi `isRawIdentifier` filtresi zaten ham UUID'yi `subtitle`'da gizliyordu; modal ise filtre kullanmıyordu, bu yüzden ayrıca düzeltilmesi gerekti.
- **i18n**: `workflow.json` (tr/en/de) - `inbox.requestDate/startTime/duration/reason/dateRange/leaveType/days` eklendi.
- **Sol menü**: `web/src/config/navigation.ts` - `NAV_ITEMS`'in **sonuna** (talimat gereği, başka ajanlar aynı dosyaya ekleme yapıyor olabilir) `workflowInbox` girişi eklendi (`/app/inbox`, `workflow.inbox.view` izni). `navigation.json` (tr/en/de) - `nav.workflowInbox` eklendi (var olan `apps.workflowInbox` ile aynı metin).
  - Bekleyen sayı rozeti: `web/src/hooks/use-workflow-inbox-count.ts` (yeni) - `listInbox` sorgusunu `["workflow","inbox"]` anahtarıyla paylaşarak (inbox sayfasıyla aynı cache) sayıyı döndürüyor, izinsiz kullanıcıda sorguyu hiç çalıştırmıyor.
  - `web/src/layouts/app-layout.tsx` - `WorkflowInboxNavItem` (yeni, `NotificationsNavItem`'ın `Indicator` desenini tekrar kullanıyor) - `workflowInbox` anahtarlı öğe için sidebar simgesinin üstünde kırmızı sayı rozeti.
- **Test**: `web/src/pages/inbox.test.tsx` (yeni) - `WorkflowInboxPage`'i gerçek `ApprovalCard` render zinciriyle mount edip fazla mesai ve izin fixture'larının detaylarının (gerekçe metni, süre, izin türü adı, gün sayısı) göründüğünü, ham `subjectId`'nin hiç ekranda OLMADIĞINI doğruluyor.

## Doğrulama

- `docker ... dotnet build Sense.slnx`: 0 hata (2104 önceden var olan uyarı, bu değişiklikle ilgisiz).
- `docker ... dotnet test tests/Modules/Sense.Modules.Workflow.Tests/...` (Testcontainers/docker.sock ile): **31/31 yeşil**.
- `docker node:24-alpine ... pnpm exec vitest run`: **384/385 yeşil** - tek kalan hata (`i18n.locales.test.ts > attendance.json > de carries no untranslated placeholder markers`) bu oturumdan ÖNCE var olan, `attendance.json`'a hiç dokunulmadığı için ilgisiz bir bulgu (`teamTimesheet.week`/`teamTimesheet.month` de.json'da çevrilmemiş kalmış).
- `pnpm exec tsc --noEmit`: temiz.
- `pnpm exec eslint` (değişen dosyalar): temiz.

## Kapsam Dışı / Sonraki Adım

- Advance/Expense/Requisition için `IApprovalRequestSummaryProvider` implementasyonu yok - bu talep türleri bugün özet olmadan (yalnızca `requestType` etiketiyle) gösteriliyor, regresyon değil (öncesinde de bu türler için detay yoktu). İlgili modül provider'ını eklediğinde Workflow tarafında hiçbir değişiklik gerekmez.
- Leave.Infrastructure'daki iki yeni provider için ayrı bir entegrasyon testi eklenmedi (kart "backend'de onay detay projeksiyonu testi (Workflow modülü)" istiyordu - bu, `ListInboxHandlerTests` ile karşılanıyor). İstenirse `Sense.Modules.Leave.Tests`'e Testcontainers tabanlı bir okuyucu testi eklenebilir.
