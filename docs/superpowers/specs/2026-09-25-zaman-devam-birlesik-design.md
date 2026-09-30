# Zaman & Devam + İK eksikleri — birleşik program tasarımı

**Tarih:** 2026-09-25 · **Durum:** Onaylı tasarım (sohbette), spec incelemesi bekliyor
**Kaynak:** [11.10 yükseltme planı](../../analysis/kolayik-video/11.10-zaman-devam-yukseltme-plani-2026-09-24.md) (Z0–Z5 + Ek A E1–E9)
**Entegrasyon dalı:** `a/zaman-devam` (`dev`'den)

## 1. Amaç ve başarı ölçütü

11.10'daki zaman-devam planını (Z0–Z4, Z5.1/Z5.3/Z5.4) ve Ek A iş paketlerini (E1–E9) **tek bir program** olarak,
makinenin kaldırabildiği en yüksek ajan paralelliğiyle uygulamak.

Başarı:
- Her iş kalemi kendi kabul kriterini karşılar (§6), `takim-gozden-gecirici` ONAY verir, test kapısı yeşil.
- Mevcut bordro / fazla mesai / aylık özet sonuçları değişmez (§8, karakterizasyon testleri).
- `a/zaman-devam` → `dev` PR'ı tek seferde tam test kapısından geçer.
- Takvim süresi ≈ 15–20 iş günü (ardışık ≈ 90–115 kişi-gün).

Kapsam dışı: Z5.2 (cihaz marka/protokol bağlantısı — karar gelene kadar), Z4.2 (11.7 #70'e ait), gerçek SMS sağlayıcı,
nitelikli e-imza, iş seviyesi / yaka tipi veri alanları.

## 2. Kararlar (11.10 §5 ve Ek A açık kararları)

| # | Karar |
|---|---|
| K1 | **Esnek vardiya** = sabit süre (`DurationMinutes`), giriş saati bir pencere içinde serbest. **Dinamik vardiya** = günün okutmalarına en yakın başlangıçlı aktif `ShiftDefinition` otomatik seçilir (aday küme: çalışana atanmış vardiya grubu yoksa tüm dinamik tipler). |
| K2 | `WorkSchedule` haftalık varsayılan olarak kalır. Plan çözümleme önceliği: `ShiftAssignment` (varsa, `ShiftDefinitionId` üzerinden kural setiyle) → `WorkSchedule` günü → plansız. |
| K3 | E1 filtresi mevcut boyutlarla (org birim tipleri: şube, birim, unvan, takım, proje + durum + işten ayrılanlar). İş seviyesi / yaka tipi eklenmez. |
| K4 | Bordro "imzası" = çalışan onayı + sunucu zaman damgası + IP/istemci bilgisi. Nitelikli e-imza yok. |
| K5 | SMS: `ISmsSender` arayüzü + `NullSmsSender`. Kanal UI'da "yakında" olarak pasif. |
| K6 | Kilitli dönem: bir çalışanın ayı için onaylı `MonthlyTimesheetApproval` varsa o günler yeniden hesaplamada ve okutma ekleme/silmede **atlanır / reddedilir**. `ShiftPlanLock` yalnız plan (vardiya ataması) değişikliklerini kilitler (bugünkü davranış). |
| K7 | Kapsam dışı ama B3'e alınan düzeltme: `ShiftPeriodAutoLock` işi hiçbir handler'a bağlı değil; `LockDueShiftPeriodsCommand`'ı çalıştıran `IScheduledJobHandler` eklenir. |
| K8 | E3 (dış bordro PDF dağıtımı) Documents modülünde; eşleşme dosya adındaki sicil no ile (`<sicil>_<YYYY-MM>.pdf`), eşleşmeyenler sonuç raporunda. |
| K9 | Vekalet (Z2.2) görevleri **yeniden atamaz**: aktif vekalet süresince vekil, asilin gelen kutusunu görür ve karar verebilir; karar kaydı "X adına Y" olarak düşer. Geri alınabilir, geçmişi bozmaz. |

## 3. Paralellik modeli

### 3.1 Darboğazlar (keşiften)

1. `AttendanceDbContext.cs` (DbContext + tüm konfigürasyonlar + tablo adları tek dosya) ve Attendance migration snapshot'ı.
2. Hesaplama çekirdeği: `AttendanceDayUpsert.cs`, `AttendanceDay.Recompute`, `PlannedDayResolver` (bugün `ShiftAssignment`'ı okumuyor).
3. Ortak kayıt dosyaları — backend: `*Permissions.cs`, `SystemRoleDefinitions.cs`, `SharedResource{,.en}.resx`,
   `NotificationKeys.cs`, `NotificationCatalog.cs`, bildirim şablonları + `labels.*.json`, `ApprovalRequestSummary`,
   `JobTypes.cs`, `BackgroundJobSeeding.cs`, `Sense.Worker/Program.cs`, `Domain/Repositories.cs`,
   `Infrastructure/Persistence/Repositories.cs`, `AttendanceServiceCollectionExtensions.cs`;
   web: `App.tsx`, `navigation.ts`, `i18n.ts`, `web/public/locales/*`, `attendance.service.ts`, `components/ui/index.ts`;
   mobil: `RootNavigator.tsx`, `types/navigation.ts`.
4. Makine: 20 çekirdek, ~53 GB boş RAM; her tam test kapısı `dotnet build/test Sense.slnx` + Testcontainers + web build.

### 3.2 İlke

- **Önce sözleşme ve iskelet (Dalga 0)**: sıcak dosyalara tek elden, bir kez yazılır; tüm yeni Attendance şeması tek migration.
- **Sonra geniş dalga**: her şerit yalnız kendi yeni dosyalarına yazar; her modülde migration ekleyen en fazla bir şerit.
- Sıcak dosyaya sonradan ekleme gerekirse **yalnız sona ekleme** (paralel plan §5 kuralı); çakışmayı birleştirme sırasında ana oturum mekanik çözer.

### 3.3 Eşzamanlılık: ajan ≠ derleme

- **Ajan eşzamanlılığı = dalga genişliği.** Dalgadaki tüm şeritler aynı anda başlatılır (Dalga 1 ≈ 32 ajan).
  Ajanın zamanının çoğu okuma/düzenleme; makineyi yormaz.
- **Derleme/test slotu = makineye göre semafor.** Ağır komutlar (`dotnet build|test`, `vite build`, `pnpm test`)
  bir slot kilidi alarak koşar:
  `SLOTS = max(2, min(floor(nproc / 4), floor((MemAvailableGB − 8) / 6)))` → bu makinede 5.
  Uygulama: host'taki `dotnet` shim'i (CLAUDE.md'deki docker komutu) `flock` ile `SLOTS` kilit dosyasından birini alır;
  web komutları için aynı sarmalayıcı. Slot sayısı ortam değişkeniyle (`SENSE_BUILD_SLOTS`) ezilebilir.
- NuGet: paylaşılan `senseik-nuget` birimi; eşzamanlı restore çakışmasına karşı her worktree'nin ilk restore'u da slot içinde.
- pnpm: paylaşılan store, worktree başına `pnpm install --frozen-lockfile`.

### 3.4 Dal ve birleştirme protokolü

- Her şerit: kendi worktree'si, dal `takim/<şerit-id>` (`a/zaman-devam`'dan), iş kartı `docs/takim/isler/<şerit-id>.json`,
  handoff `docs/takim/handoff/<şerit-id>.md`.
- Şerit döngüsü: `takim-gelistirici-{dotnet,node}` → test kapısı → "İNCELEMEYE HAZIR" → `takim-gozden-gecirici` ONAY.
- **Birleştirme (ana oturum, tek tek):** ONAY alan şerit `a/zaman-devam`'a rebase edilir; sona ekleme çakışmaları
  mekanik çözülür; şema değiştiren şeritte migration rebase sonrası yeniden üretilir; birleşik dalda derleme + ilgili
  modül testleri koşar; sonra bir sonraki şerit.
- Riskli alanlar (auth: Z5.1; para: E2; migration: W0-a, B1 backfill) insan onayı ister (TAKIM.md "bitti" tanımı).
- `a/zaman-devam` → `dev` PR (Dalga 3). `main`'e doğrudan yazılmaz.

## 4. Veri modeli (Dalga 0-a'da tek migration)

Tüm varlıklar `TenantAggregateRoot<Guid>` / tenant kapsamlı; tablolar `attendance` şemasında.

**`Punch`** (Z0.1) — ham okutma
- `EmployeeId`, `OccurredAtUtc` (DateTime UTC), `WorkDate` (DateOnly, hangi puantaj gününe ait — §5.1),
  `Direction` (`PunchDirection` mevcut enum + `Unknown = 2`), `Source` (`Manual | Import | Device | SelfService | Request | Terminal`),
  `SourceRef` (string?, ör. SelfCheckIn id / PunchRequest id / import job id), `TerminalId?`, `LocationId?`, `Latitude?`, `Longitude?`,
  `IsManual`, `CreatedByUserId?`, `Reason?`, `DeletedAtUtc?`, `DeletedByUserId?`, `DeleteReason?` (yumuşak silme — denetim).
- İndeksler: (Tenant, Employee, WorkDate); tekillik (Tenant, Employee, OccurredAtUtc, Direction, Source) silinmemişlerde.
- `IAuditLogged`.

**`ShiftDefinition`** (Z0.2)
- `Code` (tenant içinde tekil), `Name`, `ColorHex`, `Type` (`Fixed | Flexible | Dynamic`), `StartMinute`, `EndMinute`
  (gün başından dakika; End ≤ Start → gece), `StartsPreviousDay`, `DurationMinutes?` (Flexible), `FlexStartEarliestMinute?`,
  `FlexStartLatestMinute?`, `Breaks` (JSON: `[{ StartMinute?, DurationMinutes, IsFlexible }]`),
  `LateToleranceMinutes`, `EarlyLeaveToleranceMinutes`, `EarlyArrivalOvertimeMinMinutes?`, `EarlyArrivalOvertimeMaxMinutes?`,
  `LateDepartureOvertimeMinMinutes?`, `LateDepartureOvertimeMaxMinutes?`, `BonusMinutes`, `IsActive`.
- `ShiftAssignment` ek alanlar: `ShiftDefinitionId?` (geriye uyumlu), `DayType` (`Work | WeeklyRest | RestDay | PublicHoliday`, varsayılan `Work`;
  `Work` dışındaki günlerde saat alanları hesaplamada yok sayılır).

**`PunchRequest`** (Z2.1) — `EmployeeId`, `WorkDate`, `OccurredAtUtc`, `Direction`, `Reason`, `Status` (`Pending | Approved | Rejected | Cancelled`),
`ApprovalRequestId?`, `CreatedPunchId?`, `DecidedAtUtc?`.

**`ShiftPlanTemplate`** (Z3.2) — `Name`, `PeriodLengthDays`, `Entries` (JSON: `[{ EmployeeId, DayOffset, ShiftDefinitionId?, DayType }]`), `CreatedByUserId`.

**`RotationPattern`** (Z3.3) — `Code`, `Name`, `Preset` (`Custom | TwoByTwelve | ThreeByEight | FourBySix`), `CycleLengthDays`,
`Steps` (JSON: `[{ DayOffset, ShiftDefinitionId?, DayType }]`), `IsActive`.
**`RotationAssignment`** — `EmployeeId`, `RotationPatternId`, `StartDate`, `EndDate?`, `OffsetDays`.

**`TerminalGroup`** (Z5.1) — `Name`. **`Terminal`** — `Code`, `Name`, `TerminalGroupId?`, `LocationId?`, `DirectionMode` (`Both | InOnly | OutOnly`),
`ApiKeyHash`, `ApiKeyPrefix`, `LastSeenAtUtc?`, `IsActive`.
**`EmployeeTerminalCredential`** (Z5.4) — `EmployeeId`, `Kind` (`Card | Pin | ExternalId`), `Value` (tenant içinde tür başına tekil), `IsActive`.

`AttendanceAnomaly.Incalculable = 1 << 10`; `AttendanceDaySource`'a `Request`, `Terminal` eklenir.

Diğer modüllerin şema değişiklikleri kendi şeritlerinde (modül başına tek şerit): Workflow `ApprovalDelegation`;
Payroll `Payslip` onay/itiraz alanları; Documents `SharedFileType`, `SharedCompanyFile.TypeId?/VisibleOnMobile`, dış bordro
belge kaydı; Announcements `CompanyEvent` saat/tip/oturum/davetli; Onboarding kategori/şablon/önem; Notifications
`HrMessage`, `HrMessageRecipient`, `MessageGroup`; Employee `EmployeeNote`.

## 5. Davranış kuralları

### 5.1 Okutma → gün (Z0.1, Z0.4)

- Bir okutmanın `WorkDate`'i: çalışanın o tarih ve bir önceki tarih için çözülen plan penceresine
  (`start − 4 sa`, `end + 4 sa`; `StartsPreviousDay`/gece dahil) düşüyorsa o planın günü; ikisine birden düşüyorsa
  başlangıcı yakın olan (eşitse `Incalculable` — gece vardiyası çakışması); hiçbirine düşmüyorsa yerel takvim günü.
- Gün hesabı: `CheckIn` = ilk `In`, `CheckOut` = son `Out`; ara `Out→In` boşlukları `ActualBreakMinutes`'a eklenir.
  `AttendanceDay` alanları bundan türetilir; `AttendanceDay.Record` imzası korunur (türetilmiş değerle çağrılır).
- `Incalculable` koşulları: tek sayıda okutma ve yön bilgisi yok (`Unknown`); yön sırası bozuk (`In,In`, ilk kayıt `Out` vb.);
  okutma var ama hiç plan yok (ne atama ne çizelge); gece çakışması (yukarıda).
  `Incalculable` olan günde `WorkedMinutes` hesaplanmaz (0), diğer bayraklar konmaz.
- Elle kayıt (mevcut `Record`/`UpdateRecord`) okutmaya dönüşür (`Source = Manual`, `IsManual = true`).
- Veri taşıma (B1): mevcut her `AttendanceDay` için `CheckIn`/`CheckOut`'tan okutma üretilir; `SelfService` günleri
  `SelfCheckIn` satırlarından üretilir. Taşıma sonrası yeniden hesaplanan günler taşımadan önceki değerlerle birebir aynı olmalı
  (karakterizasyon testi + taşıma doğrulama sorgusu).

### 5.2 Plan çözümleme ve toleranslar (Z0.2)

- `PlannedDayResolver` K2 önceliğini uygular. `ShiftDefinition` varsa: geç kalma = `CheckIn > start + LateTolerance`;
  erken çıkma = `CheckOut < end − EarlyLeaveTolerance`; fazla mesai sayımı giriş/çıkış tarafında min eşiği aşmayan kısmı atar,
  maks'ı keser; mola listesi `BreakMinutes`'ı belirler; `BonusMinutes` çalışılan süreye eklenir.
- Esnek: planlanan giriş = gerçek giriş (pencere içindeyse), bitiş = giriş + süre. Dinamik: K1.

### 5.3 Yeniden hesaplama (Z0.3)

- `IAttendanceDayRecalculator.RecalculateAsync(tenantId, from, to, employeeIds?, ct)` → okutma + plandan günleri yeniden kurar;
  K6 kilitli günleri atlar; `Resolved`/`Ignored` çözüm durumu ve override korunur.
- Arka plan: istek kuyruğa alınır (Leave'deki hosted-service iş kalıbı), ilerleme sorgulanabilir; "Kaydet & Geçmişe Uygula"
  (`ShiftDefinition` güncellemesi) bu servisi etkilenen atamaların aralığıyla çağırır.

### 5.4 Günlük listeler (Z1.1)

`GET v1/attendance/daily?date&status&orgUnitId&includeSubUnits&page&pageSize` — `IDailyAttendanceQuery`.

| status | Kural |
|---|---|
| `incalculable` | `Incalculable` bayrağı |
| `arrived` | `CheckIn != null` |
| `late` | `LateArrival` |
| `earlyLeave` | `EarlyLeave` |
| `absent` | `NoData` ve o gün onaylı izin yok |
| `overtime` | `Overwork` veya `OvertimeMinutes > 0` |
| `undertime` | `Underwork` |
| `onLeave` | Leave.Contracts'tan o tarihte onaylı izni olanlar (yeni: tarih + kapsam bazlı sorgu) |

Veri kapsamı `IDataScopeService` ile. Elle okutma ekle/sil: gerekçe zorunlu, K6 kilidi, denetim kaydı.

### 5.5 Onay akışları

- **Eksik kayıt talebi (Z2.1):** `RequestType = "AttendancePunch"`; `IWorkflowService.StartAsync`; `ApprovalCompleted` →
  `Punch` (`Source = Request`, `SourceRef = PunchRequest.Id`) + ilgili günün yeniden hesabı; `ApprovalRejected` → `Rejected`.
  `ApprovalRequestSummary`'ye talep özeti alanı. Web + mobil.
- **Vekalet (Z2.2):** `ApprovalDelegation { DelegatorEmployeeId, DelegateEmployeeId, StartDate, EndDate, RequestTypes? (null = tümü), Reason, RevokedAtUtc? }`;
  `ListInboxTasksAsync` ve karar yetki kontrolü aktif vekaleti tanır (K9); e-posta karar bağlantısı vekile de gider.
- **Bordro itirazı (E2):** `RequestType = "PayslipObjection"`; onay = itiraz kabul.

### 5.6 Ek A davranışları (özet — ayrıntı plan aşamasında)

- **E1:** `EmployeeFilter`'a `OrgUnitIds` (çoklu, tipten bağımsız) ve `IncludeTerminated` eklenir (geriye uyumlu opsiyonel);
  web `EmployeeMultiPicker` + `EmployeeFilterPanel` (+ "seçilenleri listede tut"), `EmployeeSelection` üretir.
- **E2:** Dönem ödendiğinde bordro çalışana açılır; `me/payslips` uçları; onayla / itiraz et; imzalamayanlara hatırlatma işi + elle tetik; web + mobil (TK-030 mobil bordro kartını açar).
- **E3:** Toplu PDF/zip yükleme → sicil eşleşmesi (K8) → çalışan belgesi (`SourceModule = Payroll`) + bildirim; sonuç raporu.
- **E4:** Başlangıç/bitiş tarih-saat + tüm gün; şirket etkinlik tipi kataloğu (renk); yüz yüze/online (konum/URL); davetliler (E1); bildirim seçeneği; eski `EventDate`/enum verisi taşınır.
- **E5:** Kategori katmanı; madde önemi + e-evrak bayrağı; isimli şablonlar (işe alım / ayrılış); profilden süreci elle başlatma; kategori ilerlemesi.
- **E6:** İK mesajı (e-posta / uygulama içi; SMS pasif — K5), gelen/giden kutusu, kalıcı mesaj grupları; alıcılar E1.
- **E7:** Çalışan notu (başlık 255, içerik 512, önem, ek dosya); yalnız İK/yönetici izni (`employee.notes.*`, `*.my.*` değil).
- **E8:** Paylaşılan dosyada tip kataloğu, "mobilde görünsün", toplu indirme (zip).
- **E9:** `FormModal`'a opsiyonel "Oluştur ve Yeni"; çalışan oluşturma formunda "giriş bilgisi gönder" anahtarı (mevcut davet akışı, e-posta).

## 6. Dalgalar ve şeritler

Tahminler kişi-gün (kg). "Sahip" = geliştirici ajan türü. Her şeridin kabul kriteri iş kartına §5'ten kopyalanır.

### Dalga 0 — Sözleşme ve iskelet (3 şerit paralel)

| Şerit | İş | Sahip | Dokunduğu sıcak dosyalar |
|---|---|---|---|
| **W0-a** | §4 Attendance varlıkları + konfigürasyonlar + repository arayüz/uygulama iskeletleri + **tek migration**; `Incalculable`; bordro/FM/aylık özet **karakterizasyon testleri** (§8) | dotnet | `AttendanceDbContext.cs`, snapshot, `Domain/Repositories.cs`, `Infrastructure/Persistence/Repositories.cs`, `AttendanceAnomaly.cs` |
| **W0-b** | Modüller arası kayıtlar: tüm yeni izin anahtarları + `SystemRoleDefinitions`; onay tipleri (`AttendancePunch`, `PayslipObjection`) + `ApprovalRequestSummary` alanları + etiketler; bildirim anahtarları + katalog + şablon taslakları (tr/en/de); `JobTypes` + seeding; rapor kategorisi `time` + tek tarih parametresi; sözleşme arayüzleri `IPunchRecorder`, `IAttendanceDayRecalculator`, `IDailyAttendanceQuery`, `IAttendanceDailyKpiReader`, Leave tarih bazlı izinli sorgusu arayüzü; DI kayıt noktaları (boş uygulamalar); `Sense.Worker/Program.cs` kayıtları | dotnet | `*Permissions.cs`, `SystemRoleDefinitions.cs`, `NotificationKeys.cs`, `NotificationCatalog.cs`, şablonlar, `labels.*.json`, `IApprovalRequestSummaryProvider.cs`, `JobTypes.cs`, `BackgroundJobSeeding.cs`, `IReportProvider.cs`, `AttendanceServiceCollectionExtensions.cs`, `Worker/Program.cs`, resx |
| **W0-c** | Web/mobil iskelet: yeni i18n namespace'leri (tr/en/de) + `i18n.ts`; tüm yeni rotalar + menü girişleri (boş sayfalar); yeni servis dosyaları DTO tipleriyle (§4–5 sözleşmesinden); raporlarda PDF/Excel biçim seçici; mobil navigasyon girişleri (eksik kayıt talebi, bordro) | node | `App.tsx`, `navigation.ts`, `i18n.ts`, `locales/*/navigation.json`, `reports.tsx`, `RootNavigator.tsx`, `types/navigation.ts` |
| **W0-s** | (ana oturum) Derleme slot semaforu + `dotnet` shim (§3.3); iş kartları (`docs/takim/isler/*.json`) | — | `scripts/dev/*` |

W0-a ile W0-b çakışmaz (W0-b Attendance'ta yalnız `AttendancePermissions.cs` + DI dosyası + Contracts arayüzleri; W0-a DbContext/Domain).
Dalga 0 birleştirme sırası: W0-a → W0-b → W0-c.

### Dalga 1 — Geniş dalga (tümü paralel)

Backend (dotnet):

| Şerit | İş | Modül / yeni dosyalar | Migration? | kg |
|---|---|---|---|---|
| B1 | Z0.1 + Z0.4: `IPunchRecorder` uygulaması; upsert, cihaz aktarımı, Excel aktarımı, self check-in okutmaya yazar; §5.1 gün türetme; veri taşıma | Attendance çekirdek (`AttendanceDayUpsert.cs`, `DeviceLogImportFeatures.cs`, `SelfCheckInFeatures.cs`, yeni `Punches/*`) | **evet (veri)** — Attendance'ta Dalga 1'deki tek migration | 6–8 |
| B2 | Z0.2: vardiya kataloğu CRUD + "Kaydet & Geçmişe Uygula"; §5.2 plan çözümleme + toleranslar | Attendance (`ShiftDefinitions/*`, yeni controller, `PlannedDayResolver`, `DailyWorkCalculator.cs`) | hayır | 5–7 |
| B3 | Z0.3 + K7: yeniden hesaplama servisi + kuyruk işi + ilerleme ucu; oto-kilit handler | Attendance (`Recalculation/*`, `AttendanceModule.cs`) | hayır | 3–4 |
| B4 | Z1.1: `IDailyAttendanceQuery` + `DailyAttendanceController` + elle okutma ekle/sil; Leave tarih bazlı izinli sorgusu uygulaması | Attendance (`Daily/*`) + Leave (`Lookups/*`) | hayır | 3–4 |
| B5 | Z1.3: `IAttendanceDailyKpiReader` + Analytics `TimeKpisController` (7 gün devam, birim bazlı geç/erken, FM gruplamalı, vardiya devam oranı) | Attendance Contracts/Lookups + Analytics | hayır | 3–4 |
| B6 | Z2.1: `PunchRequest` akışı + Workflow bağlantısı + özet sağlayıcı | Attendance (`PunchRequests/*`) | hayır | 3–4 |
| B7 | Z2.2: vekalet | Workflow | evet | 4–6 |
| B8 | Z3.1: takvim toplu atama/silme, gün tipleri, vardiyasızlar filtresi, dip toplam | Attendance (`ShiftAssignmentFeatures.cs`, `ShiftsController.cs`) | hayır | 3–4 |
| B9 | Z3.3: rotasyon kalıpları + hazır kalıplar + atama → taslak `ShiftAssignment` üretimi (11.7 #47) | Attendance (`Rotations/*`, yeni controller) | hayır | 4–5 |
| B10 | Z3.2: plan şablonu kaydet/uygula + `ShiftPlanImportTemplate` (DataTransfer) | Attendance (`ShiftTemplates/*`, `Import/*`) | hayır | 3–4 |
| B11 | Z3.4: aylık puantaj matrisi sorgusu (çalışan × gün, gün durumu kodu) | Attendance (`MonthlyTimesheet/MatrixQuery*`, yeni controller) | hayır | 2 |
| B12 | E2: bordro onay/itiraz + hatırlatma işi | Payroll | evet | 4–5 |
| B13 | E3 + E8 | Documents | evet | 4–6 |
| B14 | E4 backend | Announcements | evet | 3–4 |
| B15 | E5 backend | Onboarding | evet | 3–4 |
| B16 | E6 backend + `ISmsSender` | Notifications | evet | 4–5 |
| B17 | E7 backend | Employee | evet | 2 |
| B18 | E1 backend: `EmployeeFilter` genişletme | Employee (sorgu/çözümleyici dosyaları; B17'den ayrı dosyalar) | hayır | 1–2 |
| B19 | E9 backend (gerekirse davet ucu uyarlaması) | Identity | hayır | 0.5–1 |

Web / mobil (node) — W0-c'deki DTO sözleşmesine göre, backend'i beklemeden:

| Şerit | İş | kg |
|---|---|---|
| F1 | **E1** `EmployeeMultiPicker` + `EmployeeFilterPanel` (Dalga 2'deki E4/E6 web'in önkoşulu) | 3 |
| F2 | Z1.2 günlük listeler `/attendance/daily/:status` + elle okutma ekle/sil | 3–4 |
| F3 | Z1.3 zaman dashboard'u | 2–3 |
| F4 | Z0.2 vardiya kataloğu düzenleyici (15 dk adımlı çizelge, mola listesi, toleranslar) | 3–4 |
| F5 | Z3.1 takvim yenilemesi (palet, sürükle-bırak, gün tipleri, toplu işlem, dip toplam) | 5–6 |
| F6 | Z3.3 rotasyon ekranları | 2–3 |
| F7 | Z3.2 şablon kaydet/uygula + Excel yükleme | 2 |
| F8 | Z3.4 aylık puantaj matrisi | 2–3 |
| F9 | Z2.1 eksik kayıt talebi — web + **mobil** | 3 |
| F10 | Z2.2 vekalet ayarı | 2 |
| F11 | E2 bordro onay/itiraz — web + **mobil** | 3–4 |
| F12 | E3 + E8 dokümanlar | 2–3 |
| F13 | E5 onboarding + E7 notlar (çalışan profili) | 3–4 |
| F14 | E9 "Oluştur ve Yeni" + giriş bilgisi anahtarı | 1 |

Dalga 1 genişliği: 19 backend + 14 web/mobil = **33 ajan** (§3.3 derleme slotlarıyla).

### Dalga 2 — Bağımlı işler (tümü paralel)

| Şerit | İş | Bağımlılık | kg |
|---|---|---|---|
| R1 | Z4.1 raporlar (günlük sorgudan): Hesaplanamayanlar, Vardiya Atanmayan, günlük durum listeleri — PDF + Excel | B4 | 3–4 |
| R2 | Z4.1 raporlar (okutmadan): Elle Girilen Kayıtlar, İçerdekiler, Mola, Ara Kayıtlar — PDF + Excel | B1 | 3–4 |
| T1 | Z5.1 terminal/grup CRUD, API anahtarı üretimi (yalnız hash saklanır), `POST v1/attendance/terminals/{code}/punches` (başlık `X-Terminal-Key`, toplu, idempotent, hız sınırlı), heartbeat — **güvenlik incelemesi + insan onayı** | B1 | 5–6 |
| T2 | Z5.4 toplu kart/kimlik tanımlama (Excel + ekran) | T1 şeması (W0-a) | 2–3 |
| T3 | Z5.3 canlı monitör (live-board üstüne, terminal durumu) | B1, T1 | 2 |
| F15 | Z5 web ekranları (terminal, grup, kimlik, monitör) | T1–T3 sözleşmesi | 3–4 |
| F16 | E4 etkinlik web | F1, B14 | 3 |
| F17 | E6 İK mesajları web | F1, B16 | 3 |

### Dalga 3 — Entegrasyon (2 şerit)

| Şerit | İş |
|---|---|
| I1 | Birleşik dalda tam test kapısı; karakterizasyon testleri; B1 veri taşımasının gerçek Postgres'te (Testcontainers) doğrulanması; performans: 1 ay × 1000 çalışan yeniden hesaplama süresi raporu |
| I2 | `faz*-durum-takibi.md`, 11.7 #47 kapanışı, 11.10 durum satırı, `docs/takim/handoff`; `a/zaman-devam` → `dev` PR |

**Kritik yol:** W0-a → B1 → (R2, T1) → T3 → I1. B1 en uzun Dalga 1 şeridi olduğundan ilk başlatılır.

## 7. Sözleşme arayüzleri (Dalga 0-b)

```csharp
// Attendance.Contracts
public interface IPunchRecorder {
    Task<Result<Guid>> RecordAsync(PunchInput input, CancellationToken ct);           // tek okutma + ilgili günün yeniden hesabı
    Task<Result<int>> RecordBatchAsync(IReadOnlyList<PunchInput> inputs, CancellationToken ct); // idempotent (tekillik §4)
    Task<Result> SoftDeleteAsync(Guid punchId, string reason, CancellationToken ct);
}
public sealed record PunchInput(Guid TenantId, Guid EmployeeId, DateTime OccurredAtUtc, PunchDirectionDto Direction,
    PunchSourceDto Source, string? SourceRef, Guid? TerminalId, Guid? LocationId, double? Latitude, double? Longitude,
    bool IsManual, Guid? CreatedByUserId, string? Reason);

public interface IAttendanceDayRecalculator {
    Task<Guid> EnqueueAsync(Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, CancellationToken ct);
    Task<RecalculationStatusDto?> GetStatusAsync(Guid tenantId, Guid jobId, CancellationToken ct);
    Task<int> RecalculateNowAsync(Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employeeIds, CancellationToken ct);
}

public interface IDailyAttendanceQuery {
    Task<PagedResult<DailyAttendanceRowDto>> ListAsync(Guid tenantId, DateOnly date, DailyStatus status,
        Guid? orgUnitId, bool includeSubUnits, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyDictionary<DailyStatus, int>> CountsAsync(Guid tenantId, DateOnly date, Guid? orgUnitId, bool includeSubUnits, CancellationToken ct);
}

public interface IAttendanceDailyKpiReader {
    // Günlük: planlı çalışan sayısı, gelen, geç, erken çıkan, devamsız, izinli (son N gün, varsayılan 7)
    Task<IReadOnlyList<DailyAttendanceTotalsDto>> DailyTotalsAsync(Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct);
    // Çalışan bazlı dönem toplamları: geç/erken adet + dakika, FM dakika, planlı/gerçekleşen vardiya günü.
    // Birim/şube/takım/unvan gruplaması Analytics'te IOrgUnitLookup ile yapılır (Attendance org bilmez).
    Task<IReadOnlyList<EmployeeAttendancePeriodDto>> EmployeeTotalsAsync(Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct);
}

// Leave.Contracts
public interface ILeaveOnDateReader {
    Task<IReadOnlyList<EmployeeLeaveOnDate>> ListAsync(Guid tenantId, DateOnly date, IReadOnlyCollection<Guid>? employeeIds, CancellationToken ct);
}
```

Web DTO'ları bu sözleşmelerle birebir (camelCase, enum'lar camelCase string, null alanlar yok sayılır — CLAUDE.md).
Kesin alan listeleri Dalga 0'da kod olarak yazılır; W0-c, W0-b'nin C# sözleşmesinden türetir.

## 8. Test stratejisi

- **TDD** her şeritte (superpowers `test-driven-development`): önce kırmızı test.
- **Karakterizasyon (W0-a, Z0.1 riski):** mevcut `AttendanceDay` girdileriyle `IAttendanceMonthlySummaryReader`,
  `AttendanceOvertimePayrollReader`, `AttendanceWorkedTimeReaderAdapter` (Payroll) ve `OvertimeCostKpi` çıktıları sabitlenir;
  B1/B2 sonrası aynı girdiler okutmaya dönüştürülerek aynı çıktılar beklenir.
- **Birim:** §5.1 gün türetme ve `Incalculable` kuralları, §5.2 toleranslar, rotasyon üretimi, vekalet yetki kontrolü — saf fonksiyon testleri.
- **Entegrasyon (Testcontainers):** her yeni migration, B1 veri taşıması, terminal ucu (API anahtarı, idempotans), onay akışları (talep → onay → okutma).
- **Web:** Vitest; i18n tr/en/de eşitlik testleri; navigasyon/route-permission testleri.
- **Mobil:** Jest (servis + ekran).
- Test silme/atlama/filtreleme yasak; kapı: `node .claude/takim/betikler/test-kapisi.mjs`.

## 9. Riskler

| Risk | Önlem |
|---|---|
| B1 hesap sonuçlarını değiştirir (bordro) | Karakterizasyon testleri W0-a'da; B1 insan onayı |
| Attendance migration çakışması | Dalga 0'da tek şema migration'ı; Dalga 1'de yalnız B1 migration ekler; zorunlu kalan şerit rebase sonrası yeniden üretir |
| Sıcak dosya birleştirme çakışmaları | Dalga 0'da ön kayıt; sonrasında yalnız sona ekleme; ana oturum tek tek birleştirir |
| 33 ajanlık dalgada derleme yükü | Slot semaforu (§3.3) |
| Sözleşme sapması (web ↔ backend) | DTO'lar W0'da kodda sabit; değişiklik yalnız sözleşme şeridi üzerinden, bağımlı şeritlere bildirilir |
| Terminal ucu güvenliği | Yalnız hash saklanan anahtar, hız sınırı, tenant izolasyonu testi, security-reviewer + insan onayı |
| Dinamik vardiya yanlış eşleşme | Aday küme sınırlı (K1); belirsizlikte `Incalculable` |

## 10. Açık kalanlar (programı durdurmaz)

- Z5.2 cihaz markası/protokolü (karar gelince ayrı spec).
- SMS sağlayıcısı (K5 arayüzü hazır).
- İş seviyesi / yaka tipi alanları (K3).
