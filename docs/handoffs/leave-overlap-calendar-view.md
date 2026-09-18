# senseik-026 — Çakışan İzinler: Gün-Gün Takvim Görünümü — UYGULAMA NOTU

**Durum: Uygulandı, `b/leave-overlap-calendar-view` branch'inde, yerel commit alındı (push YAPILMADI).**

## Ne değişti

`web/src/components/leave/leave-request-detail.tsx`'deki "Çakışan izinler" paneli (satır ~106-129), düz metin listesinden (isim + tarih aralığı) gün-gün hücreli bir mini takvim/grid'e dönüştürüldü.

### Yeni bileşen: `web/src/components/leave/team-overlap-calendar.tsx`

`TeamOverlapCalendar`: `LeaveTeamOverlap` verisini alıp CSS Grid ile gün-gün render eder.

- **Tarih sütunları:** talebin kendi `[startDate, endDate]` aralığı + her iki yönde 3 gün bağlam (`CONTEXT_DAYS`). Talep kapsamındaki günler kalın/vurgulu, bağlam günleri soluk gösteriliyor.
- **Satırlar:** her çakışan çalışan bir satır; adının altında kendi izin tarih aralığı (mevcut davranıştan korunan bilgi). Her günün hücresinde, çalışanın izni o güne denk geliyorsa renkli bir bar segmenti var (`BAR_COLORS` — Mantine tema renkleri arasında satır bazında döner). Çalışanın kendi aralığı görünür pencereyi taşıyorsa (örn. haftalarca süren bir izin), bar pencereye kırpılıyor — pencere dışı günler render edilmiyor.
- **Genişletme:** varsayılan olarak ilk 5 çalışan gösteriliyor; 5'ten fazlaysa "Tümünü gör (N)" butonu ile tümü açılıyor. Ayrıca backend'in kendi kapağı (`TeamOverlapMaxEmployees=20`) nedeniyle listelenemeyen fazladan kişi sayısı da ayrı bir satırda gösteriliyor (mevcut `overlappingLeaves.more` davranışı korundu, metni "(listelenmedi)" ekiyle netleştirildi).
- **Grup başlığı:** `overlap.orgUnitName` doluysa (bkz. backend değişikliği) başlığın altında org birim adı gösteriliyor (ekran görüntüsündeki "Human Resources" karşılığı).

`web/src/lib/dates.ts`'e iki küçük yardımcı eklendi: `addDaysToDateOnly` (pencere sınırlarını hesaplamak için) ve `enumerateDateOnlyRange` (sütunları üretmek için).

### Backend değişikliği: org birim adı

Kart, tarih aralığının zaten `TeamOverlapEmployeeDto.StartDate/EndDate` içinde geldiğini not ediyordu — bu doğrulandı, **API'de tarih aralığı eksik değildi**, ek alan gerekmedi. Eksik olan tek şey ekran görüntüsündeki grup başlığıydı (departman adı), o da hiç dönmüyordu. Bunun için:

- `src/Modules/Leave/Sense.Modules.Leave.Application/Requests/LeaveRequestPreview.cs`: `TeamOverlapDto`'ya opsiyonel `string? OrgUnitName = null` alanı eklendi (yalnızca detay görünümünde doldurulur; self-servis önizleme (`PreviewMyLeaveRequestHandler`) hâlâ null bırakıyor — orada grup başlığı gösteren bir yer yok).
- `src/Modules/Leave/Sense.Modules.Leave.Application/Requests/LeaveRequestManagementFeatures.cs`: `LeaveRequestDetailBuilder`'a `IOrgUnitLookup orgUnits` enjekte edildi (Organization modülünün cross-module contract'ı, zaten `Sense.Modules.Leave.Application.csproj`'da referanslı ve `AddLeaveServices` API-only DI kaydında `IOrgUnitLookup` zaten mevcuttu — aynı desende `LeaveBalanceSummaryReportProvider`/`LeaveRequestsReportProvider` de kullanıyor). `ResolveTeamOverlapAsync` artık `ResolveOrgUnitNameAsync` ile `orgUnits.ListActiveUnitsAsync(...)`'tan isim çözüyor (best-effort, lookup kaçırırsa null — talebi asla başarısız etmiyor).
- `web/src/services/leave.service.ts`: `LeaveTeamOverlap` arayüzüne `orgUnitName?: string | null` eklendi.

## Testler

- `web/src/lib/dates.test.ts`: `addDaysToDateOnly` (ay/yıl taşması dahil) ve `enumerateDateOnlyRange` (dahil aralık, tek gün, ters aralık boş, ay taşması) için 8 yeni test.
- `web/src/components/leave/team-overlap-calendar.test.tsx` (yeni, RTL): 7 test — başlık+sayaç+org birim adı render, sütun aralığı (talep + 3 gün bağlam), bar segmentlerinin doğru günlere düşmesi, pencere dışına taşan bir aralığın kırpılması, 5+ satırda "Tümünü gör" genişletmesi, sunucu kapağının "listelenmedi" notu, ≤5 satırda genişletme butonunun hiç render edilmemesi.
- `tests/Modules/Sense.Modules.Leave.Tests/LeaveManagementTests.cs`: yeni `Detail_TeamOverlap_ResolvesOrgUnitName` testi (`SubstituteDetails` helper'ına opsiyonel `(Guid, string)? orgUnit` parametresi eklendi, `IOrgUnitLookup` mock'landı) — `LeaveRequestDetailBuilder`'ın org birim adını gerçekten `TeamOverlapDto.OrgUnitName`'e taşıdığını doğruluyor. Mevcut `SubstituteDetails` çağrısı (`CancelApproved_...` testi) `IOrgUnitLookup` mock'u eklenmesi gerektiği için güncellendi (yeni zorunlu constructor parametresi).

## Doğrulama sonuçları

- **`dotnet build`** (Docker SDK konteyneri, `tests/Modules/Sense.Modules.Leave.Tests/Sense.Modules.Leave.Tests.csproj` — tüm solution'ı transitif olarak derliyor): **Build succeeded**, hata yok.
- **Hedefli `dotnet test`** (`Detail_TeamOverlap_ResolvesOrgUnitName`, `CancelApproved_PostsReversal_...`, `Preview_TeamOverlap_CountsSameUnitActiveOnly`): **3/3 FAIL** — ama üçü de aynı önceden var olan altyapı hatasıyla düşüyor: `AttendanceDbContext` `PendingModelChangesWarning`. Bunun bu PR'dan bağımsız olduğu doğrulandı: (1) `git status` bu worktree'de yalnızca bu kartın dosyalarının değiştiğini gösteriyor, Attendance modülüne hiç dokunulmadı; (2) tamamen ilgisiz, önceden var olan bir test de (`Resubmission_RequiresOwnRejectedRequest`) aynı hatayla düşüyor; (3) `docs/handoffs/age-50-exception.md` (senseik-007, ayrı bir kart) bu tam hatayı zaten "38 önceden var olan hata, `c72791f` commit'inden miras, bu değişiklikle ilgisiz" diye kayıt altına almış. **Sonuç: bu üç testin C# tarafı mantığı derleme seviyesinde doğrulandı (build yeşil, kod değişikliği küçük ve izole), ama testcontainers ortamındaki gerçek yeşil koşu, önceden var olan Attendance migration sorunu çözülene kadar bu worktree'de alınamıyor.** Bu bir blocker olarak ayrıca bildirilmeli — Attendance modülünün migration'ı bu kartın kapsamı dışında, dokunulmadı.
- **Web:** bu worktree'de host `pnpm` PATH'te yoktu (CLAUDE.md "host pnpm çalışır" varsayımı bu ortamda geçerli değildi) → CLAUDE.md'nin kendi belirttiği Docker `node:24-alpine` fallback'ine geçildi (`corepack enable && pnpm install --frozen-lockfile`, sonra named volume'lar `node_modules`/`web/node_modules`/`packages/api-client/node_modules`/`apps/senseik-mobile/node_modules` üzerine mount edildi).
  - `pnpm exec tsc --noEmit`: **temiz**, hata yok.
  - `pnpm exec eslint src`: **0 hata**, 1 önceden var olan ilgisiz uyarı (`employee-list.tsx` exhaustive-deps).
  - `pnpm exec vitest run` (tüm web test suite'i): **397/398 geçti**. Tek hata `src/i18n.locales.test.ts > attendance.json > de carries no untranslated placeholder markers` — `web/public/locales/de/attendance.json` bu PR'da hiç değiştirilmedi (`git status` teyit), önceden var olan ilgisiz bir çeviri eksikliği.
  - Bu kartın kendi testleri (`dates.test.ts` + `team-overlap-calendar.test.tsx`): **29/29 geçti**.

## Commit

1 commit, `b/leave-overlap-calendar-view` branch'inde, `feat:` prefix'i ile. Push yapılmadı.

## Değişen dosyalar

- `web/src/components/leave/team-overlap-calendar.tsx` (yeni)
- `web/src/components/leave/team-overlap-calendar.test.tsx` (yeni)
- `web/src/components/leave/leave-request-detail.tsx`
- `web/src/lib/dates.ts`, `web/src/lib/dates.test.ts`
- `web/src/services/leave.service.ts`
- `web/public/locales/{tr,en,de}/leave.json`
- `src/Modules/Leave/Sense.Modules.Leave.Application/Requests/LeaveRequestPreview.cs`
- `src/Modules/Leave/Sense.Modules.Leave.Application/Requests/LeaveRequestManagementFeatures.cs`
- `tests/Modules/Sense.Modules.Leave.Tests/LeaveManagementTests.cs`

## Bilinen açık nokta / sonraki adım

`AttendanceDbContext` `PendingModelChangesWarning` — dev dalında önceden var olan, bu kartla ilgisiz bir test-altyapı sorunu (bkz. yukarıda). Leave modülünün testcontainers testlerini gerçek yeşil koşuyla doğrulamak isteyen bir sonraki agent/geliştirici önce bunu çözmeli (muhtemelen eksik bir Attendance EF migration'ı eklemek gerekiyor — `dotnet ef migrations add` Attendance modülünde).
