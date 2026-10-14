# Paralel Geliştirme Planı — İki Makine (PC-A / PC-B)

> **Geçersiz (2026-09-24):** PC-B yok; tüm modüller, web ve mobil tek makinede geliştiriliyor. §1, §2, §3, §4, §7, §8'deki makine ayrımı ve sahiplik artık uygulanmaz. Aynı çalışma ağacını paralel oturumlar paylaştığı için §5 (sıcak dosyalara yalnız ekleme) ve §6 (git akışı) geçerli; dal öneki `a/<konu>` (takım kartları `takim/<konu>`).

**Tarih:** 2026-09-15
**Kaynaklar:** [11.7-yol-haritasi.md](../analysis/kolayik-video/11.7-yol-haritasi.md), [faz0-durum-takibi.md](../analysis/kolayik-video/faz0-durum-takibi.md), ADR [0001](../adr/0001-modular-monolith.md) (20 modül), [0003](../adr/0003-per-module-dbcontext.md) (modül başına DbContext)

## 1. Özet

| Makine | Hat | Sahip olduğu alan |
|---|---|---|
| **PC-A** (mevcut) | Faz 0 kalanları → Faz 1a Employee (çalışan listesi/kartı, özlük alanları) | Identity, Organization, Employee, Documents, `src/Shared/*`, web kabuğu (layout, navigasyon, ayarlar, çalışan sayfaları) |
| **PC-B** (yeni) | Faz 0'ı beklemeyen yeni modüller: **Workflow → Notifications → Leave (backend motoru)** | `src/Modules/Workflow`, `Notifications`, `Leave` + bunların web sayfaları |

**Neden mümkün:** Her modülün kendi şeması, DbContext'i ve migration klasörü var (ADR 0003). Farklı modüllerde çalışan iki makine migration/snapshot çakışması üretmez. Modüller arası erişim yalnız `*.Contracts` üzerinden olduğu için sınırlar net.

**Neyi çözmez:** Ortak "sıcak dosyalar" (bkz. §5) ve Faz 0'ın bitmemiş kalemlerine bağlı ekranlar (import, toplu işlem, menü). Bunlar senkronizasyon noktalarıyla yönetilir (§8).

## 2. Başlamadan önce — PC-A'da bir kez (bloklayıcı)

1. **Çalışma ağacını commit + push et.** `main` üzerinde ~160 değiştirilmiş/izlenmeyen dosya var (çalışan liste/kart sayfaları, erişim ayarları, veri aktarım sayfası, `EmployeeProfileAndManager` migration'ı, gözlemlenebilirlik eklemeleri…). PC-B bunları görmeden başlarsa ilk rebase'de büyük çakışma olur. Mantıksal commit'lere böl (Conventional Commits, commitlint).
2. **`faz0-durum-takibi.md`'yi gerçek koda göre güncelle.** Tablo item 3–6 merge'lerinden ve yukarıdaki commit edilmemiş işlerden önce yazılmış görünüyor (ör. `web/src/pages/employees/employee-list.tsx`, `settings/data-transfer.tsx` mevcut ama tabloda "yok"). PC-B'nin "Faz 0 bitti mi" kararlarını bu tablo belirleyecek.
3. **`Sense.slnx` kontrolü:** Documents projeleri çözüm dosyasında görünmüyor (`ModuleCatalog` ve Migrator ise referans veriyor). Kasıtlı değilse ekle.
4. CI (`pr.yml`, `main.yml`) yeşil olsun; başlangıç noktasını etiketle: `git tag paralel-baslangic && git push --tags`.

## 3. PC-B kurulumu

```bash
git clone https://github.com/algosense-yazilim/senseik.git && cd senseik
cp .env.example .env
docker compose -f infra/docker-compose.yml up -d
```

- **Host'ta .NET/Node yoksa** (PC-A'daki gibi) Docker üzerinden çalıştır:
  - .NET: `docker run --rm -v "$PWD":/src -v senseik-nuget:/root/.nuget/packages -v /var/run/docker.sock:/var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal --add-host=host.docker.internal:host-gateway -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet <args>` (`-p:UseAppHost=false` verme; xUnit v3 app host ister).
  - Web: `node:24-alpine`, önce `corepack enable`; `node_modules`, `web/node_modules`, `packages/api-client/node_modules`, `apps/senseik-mobile/node_modules` için isimli volume kullan (host'taki macOS derlemeleriyle karışmasın).
- **Baz doğrulama:** `dotnet build Sense.slnx` + `dotnet test` + `pnpm --filter web typecheck` PC-B'de yeşil olmadan modül işine başlama.
- **Claude Code hafızası makineye özeldir.** PC-A'daki kurallar (Docker araç zinciri, "roadmap kalemi durum değiştirince takip dokümanını güncelle") `~/.claude/projects/…/memory` altında ve PC-B'ye taşınmaz. Tek kaynak olsun diye repo köküne `CLAUDE.md` olarak eklenmesi önerilir.

## 4. Sahiplik

Kural: **Bir klasörü yalnız sahibi değiştirir.** Diğer makinenin ihtiyacı küçük, ayrı bir PR ile gelir ve hızlı merge edilir.

| Alan | PC-A | PC-B |
|---|---|---|
| `src/Modules/Identity`, `Organization`, `Employee`, `Documents` | Sahip | Yalnız `*.Contracts`'a ekleme PR'ı (bkz. §6 contract-first) |
| `src/Modules/Workflow`, `Notifications`, `Leave` | — | Sahip |
| `src/Shared/*` (Kernel, Contracts, Infrastructure, Web) | Sahip | Ayrı küçük PR |
| `web/src/pages/employees/*`, `settings/*`, `config/navigation.ts`, layout | Sahip | — |
| `web/src/pages/leave/*`, `requests/*`, `approvals/*`, `notifications/*` | — | Sahip |
| `src/Modules/Recruitment`, `web/src/pages/recruitment/*` | Sahip (2026-09-16, kullanıcı kararıyla Faz 6'ya erken geçildi — bkz. `faz6-durum-takibi.md`) | — |
| `src/Modules/Expense`, `web/src/pages/expense/*` (henüz yok) | Sahip (2026-09-16, kullanıcı kararıyla Faz 5 Performance yerine seçildi — Performance başka bir oturumun kilitli `worktree-performance-core` dalında zaten işleniyordu; bkz. `faz4-durum-takibi.md` Ex0) | — |
| `web/public/locales/*/<namespace>.json` | Kendi namespace'leri | Kendi namespace'leri (`leave.json`, `workflow.json`, `notifications.json`) — ayrı dosya olduğu için çakışmaz |
| `tests/Modules/Sense.Modules.<M>.Tests` | Kendi modülleri | Kendi modülleri |

## 5. Sıcak dosyalar (iki makinenin de dokunduğu)

| Dosya | Neden dokunulur |
|---|---|
| `src/Sense.Api/ModuleCatalog.cs` | Yeni modül kaydı |
| `src/Sense.Migrator/Program.cs`, `src/Sense.Worker/Program.cs` | `AddModuleDbContext<…>` |
| `Sense.slnx` | Yeni projeler |
| `Directory.Packages.props` | Yeni NuGet paketi |
| `src/Modules/Identity/…/Roles/SystemRoleDefinitions.cs` | Yeni permission anahtarları rollere |
| `src/Sense.Api/SeedData/DataSeeder.cs`, `SeedDataConstants.cs` | Demo seed |
| `packages/api-client/src/index.ts` | Yeni servis export'u |
| `web/src/config/navigation.ts`, web router | Menü/rota |
| `src/Sense.Api/appsettings*.json`, `.env.example` | Modül ayarları |

**Kurallar**
- Yalnız **ekleme**, listenin **sonuna**; mevcut satırları yeniden sıralama, biçimlendirme veya toplu düzenleme yok.
- Modül iskeleti kaydı (catalog + migrator + worker + slnx + mimari test) tek başına küçük bir commit: `chore(workflow): register module skeleton` → hemen main'e.
- `Directory.Packages.props`'a paket eklemeden önce `git pull --rebase`.
- **Navigasyon:** PC-A Faz 0 #12'yi (menü + Ayarlar bilgi mimarisi) bitirene kadar PC-B `navigation.ts`'e dokunmaz; sayfaları yalnız rota olarak ekler, menüye bağlamayı #12 merge'ünden sonra yapar.

## 6. Git akışı

- `main` korumalı; dallar: `a/<konu>` (PC-A), `b/<modül>-<konu>` (PC-B).
- Her gün en az bir kez: `git fetch && git rebase origin/main`.
- PR'lar küçük, en geç 1–2 günde merge; uzun yaşayan dal yok.
- **Contract-first:** PC-B, sahibi PC-A olan bir modülden yeni veri isterse (ör. `IEmployeeDirectory`'ye işe giriş tarihi, `IOrgUnitLookup`'a onaycı slotu) önce yalnız arayüz + basit implementasyon içeren PR merge edilir, sonra kullanılır.
- **Migration:** yalnız sahibi olduğun modülde üret. Aynı modülde iki makine aynı anda migration üretmez.

## 7. Modül bazında "şimdi başlanabilir mi?"

| Modül | Faz | Bağımlılık | Şimdi? |
|---|---|---|---|
| Identity, Organization, Employee, Documents | 0–1a | — | Mevcut (PC-A) |
| **Workflow** | 1a | Organization (`OrgUnit` onay slotu, Faz 0 #4), Employee (yönetici) | ✅ Evet — onaycı çözümleme portu ile; birim bazlı atama #4 bitince bağlanır |
| **Notifications** | 1b | Shared e-posta (`SmtpEmailSender`) ve SignalR (`NotificationsHub`) zaten var | ✅ Evet |
| **Leave** | 1b | Employee, Organization (tatil takvimi, `WorkCalendar`), Workflow, Notifications, DataTransfer (#7/#8), toplu işlem (#9) | 🟡 Backend motoru evet; import, toplu atama ve menü Faz 0 sonrası |
| Attendance | 2 | Leave (`TimesheetTreatment`), `WorkCalendar` | 🟡 Başladı — A1 (çalışma takvimi + atama) bitti; Leave artık kendi Pzt–Cum yer tutucusu yerine `Attendance.Contracts.IWorkScheduleReadService` kullanıyor |
| Payroll | 3 | `LegislationParameter`, Attendance, Leave, FM dönüşümü | 🟡 Hesap motoru + golden set saf domain olarak mümkün (en uzun ve riskli faz); ancak kapasite varsa |
| Expense | 4 | Workflow, Documents | 🟡 Ex0 bitti (2026-09-16, kullanıcı kararıyla erken başlatıldı — bkz. `faz4-durum-takibi.md`); yalnız çekirdek (harcama talebi + avans + ödemeler özeti), Documents'a fiili bağımlılık henüz yok (dosya yükleme entegre edilmedi) |
| Assets, Announcements (+Support), Training | 4 | Employee, Documents | Teknik olarak mümkün, öncelik düşük |
| Performance, Compensation (+Benefits adayı) | 5 | Employee, Organization, Payroll (Compensation) | ❌ |
| Recruitment (ATS) | 6 | Identity rolleri, `Location`/`OrgUnit` | ✅ R0 bitti (2026-09-16, kullanıcı kararıyla erken başlatıldı — bkz. `faz6-durum-takibi.md`); R1+ (requisition+onay, form builder, kariyer sitesi, raporlar) sırayla devam edecek |
| Analytics, Ai | 7 | Diğer modüllerin verisi | ❌ |
| Integrations, Billing | 8 | — | ❌ |

## 8. PC-B iş sırası

### B1 — Workflow çekirdeği
- **Kaynak:** [11.3.04-onay-surecleri.md](../analysis/kolayik-video/11.3.04-onay-surecleri.md); yol haritası #29, #30.
- **Kapsam:** talep türü başına sıralı onaycı listesi (en çok 6), zorunlu/opsiyonel onaycı, talep anında onaycı listesi düzenleme, Admin doğrudan onay, "onay sırası kimde" görünürlüğü, e-postadan onay/red (tek kullanımlık token), audit (IP/tarih/kanal).
- **Port:** `IApproverResolver` — varsayılan "ilk yönetici"; Faz 0 #4 (`OrgUnit.ApprovalPolicyIds[RequestType]`) merge edilince birim ataması + üst birimden devralma.
- **Çıktı:** `Workflow.Contracts` (başlat/iptal komutu, `ApprovalCompleted`/`ApprovalRejected` integration event'leri). Leave, Expense, vize ve ATS bunu tüketecek; bu yüzden B1 başında 11.3.05, 11.3.08, 11.3.09 (vize) ve 11.3.11 (ATS talep onayı) onay gereksinimleri birlikte okunur (yol haritası karar #10: ATS gereksinimleri genel motor özelliği).

### B2 — Notifications modülü
- **Kapsam:** kullanıcı bazlı e-posta bildirim koşulları, kaynak anahtarlı şablonlar (tr/en/de), ana sayfa bildirimi (SignalR), talep oluşturuldu/onaylandı e-postaları (#43).
- Mevcut Shared altyapısını kullanır; `Shared` değişikliği gerekirse PC-A'ya ayrı PR.

### B3 — Leave backend motoru
- **Kaynak:** [11.3.05-izin-ve-fazla-mesai.md](../analysis/kolayik-video/11.3.05-izin-ve-fazla-mesai.md); yol haritası #14–#27.
- **Önce karar (ADR):** `LegislationParameter`'ın yeri. Leave, Payroll ve FM ortak kullanıyor. Öneri: okuma portu `Sense.Shared.Contracts`'ta, tarih aralıklı veri ve seed tek bir modülde. ADR numarası 0008 (0007 yol haritasında DOCX için ayrılmış).
- **Şimdi:** `LeaveRuleSet`, `LeaveType` (sihirbaz alan seti), `LeaveSeniorityTier`, `LeaveLedger` (`decimal(9,4)`, ExtraGrant), `LeaveBalance` projeksiyonu (yalnız `PerYear`), `ILeaveDayCalculator`, `RecalculateBalances` job'ı, `CloneLeaveRuleSet`, seed türler, `LeaveRequest` + Workflow entegrasyonu, FM talebi + TimeOff dönüşümü, API + temel ekranlar (rota olarak).
- **Faz 0 sonrası:** izin import şablonu (`IImportTemplate`), toplu "İzin kuralı ata", menü öğeleri (İzinler/Talep/Takvim), takvim görünürlüğü.
- **Faz 3'e kalan:** FM → Pay dönüşümünün bordro tarafı.

## 9. Senkronizasyon noktaları

| Olay (merge edildiğinde) | Kimden | PC-B'de açılan iş |
|---|---|---|
| Faz 0 #4 `OrgUnit` onay slotu | PC-A | ✅ Tüketildi — B1'in `DefaultApproverResolver`'ı `IOrgApprovalPolicyResolver`'ı ilk basamak olarak kullanıyor (birim ağacı → devralma → yoksa yönetici → yoksa HR kuyruğu) |
| Faz 0 #7/#8 veri aktarım çekirdeği + şablon kuralları | PC-A | Leave import şablonu |
| Faz 0 #9 toplu işlem sözleşmesi | PC-A | "İzin kuralı ata" toplu aksiyonu |
| Faz 0 #12 navigasyon | PC-A | Menü öğeleri |
| Faz 0 #1 `[RequiresAccessSetting]` gerçekten uygulanır | PC-A | Takvim ve FM görünürlüğü |
| Çalışan kartı sekme slotları (İzin, Fazla mesailer, Ekstra izinler) | PC-A (boş slot) | Sekme içerikleri |
| `Workflow.Contracts` ilk sürümü | PC-B | PC-A: gerekiyorsa vize/özlük talepleri |
| `LegislationParameter` ADR'ı | PC-B önerir, birlikte karar | Leave seed ve hesapları |

## 10. Takip

- `faz0-durum-takibi.md`: PC-A günceller (PC-B'nin senkronizasyon kararları buna bakar).
- PC-B hattı durumu aşağıdaki tabloda tutulur; kalem durumu değişince güncellenir.

| Kalem | Durum | Dal / PR | Not |
|---|---|---|---|
| §2 ön koşullar (PC-A) | 🟡 Kısmi | `a/parallel-prereqs` | Commit + push, Faz 0 takip tablosu, Documents → `Sense.slnx`, kök `CLAUDE.md`, `paralel-baslangic` etiketi yapıldı; mantıksal commit'lere bölme yapılmadı (tek commit `eb82c17`); CI durumu bu makineden doğrulanamadı (`gh` yok) |
| §3 PC-B kurulumu + baz doğrulama | ✅ Bitti | — | Ayrı klon gerekmedi (aynı çalışma ağacı); `dotnet build Sense.slnx` ve `pnpm exec tsc --noEmit && vite build` Docker üzerinden yeşil (2026-09-15) |
| B1 Workflow çekirdeği | ✅ Bitti | `b/workflow-core` | 5 projeli modül (Domain/Contracts/Application/Infrastructure/Api); `ApprovalPolicy`/`ApprovalRequest` state machine, ≤6 adım, zorunlu/opsiyonel onaycı, Admin doğrudan onay, e-postadan tek kullanımlık token ile onay/red, IP/UA/kanal audit; onaycı çözümleme sırası: Organization'ın `IOrgApprovalPolicyResolver` (birim ağacı, S1 senkron noktası kullanılarak) → doğrudan yönetici → HR rol kuyruğu (kullanıcı kararı). Event'ler `IIntegrationEventOutbox` ile Workflow'un kendi outbox'ına yazılıyor, `Sense.Worker`'a poller eklendi. Admin ekranı (`/app/settings/company/approval-flows`), inbox (`/app/inbox`), public onay sayfası (`/approve/:token`) dahil. Contract-first: `Identity.Contracts.IRoleAssignmentLookup` (yeni), `Employee.Contracts.IEmployeeLookup.GetManagerEmployeeIdAsync` (yeni). Önemli düzeltme: `ApprovalTask`/`ApprovalHistoryEntry`/`EmailDecisionToken`'ın client-generated Guid id'leri `ValueGeneratedNever()` olmadan EF'in Added/Modified sınıflandırmasını bozup her gerçek karar isteğinde `TenantId` immutability hatası veriyordu — testler bu haliyle 15/15 geçiyor. Tam çözüm build+test yeşil (2026-09-15). Kapsam dışı: birim/ünvan bazlı politika ataması entegrasyonu şimdi eklendi ama delegasyon, tek onaycılı vize kilidi, koşullu/paralel "Gelişmiş mod", `RequestTypeDefinition` kataloğu, ana sayfa widget'ı, tam e-posta şablonlama (B2'nin işi) hâlâ yok. Henüz main'e merge edilmedi. |
| B2 Notifications | ✅ Bitti (PC-B başlattı; PC-A makinesinde tamamlandı, kullanıcı isteğiyle, 2026-09-15) | `b/notifications-core` → `b/notifications-finish` | **Yapıldı (PC-B):** contract-first `Identity.Contracts.IUserProfileLookup`, `Employee.Contracts.IEmployeeContactLookup` (Worker'da data-scope bağımlılığı olmadan çözülebilsin diye); `NotificationsHub` düzeltmesi (`tid` claim'i okunmuyordu → tenant grubuna hiç girilmiyordu) + `user-{id:N}` grubu; Workflow artık senkron TR e-posta atmıyor, `ApprovalRequested`/`ApprovalTaskAssigned` olaylarını outbox'a yazıyor, tek kullanımlık onay linki gönderim anında `Workflow.Contracts.IApprovalDecisionLinkIssuer` ile üretiliyor (ham token outbox'a düşmüyor). Yeni 5 projeli `Sense.Modules.Notifications`: `UserNotification` (uygulama içi), `NotificationDelivery` (e-posta kuyruğu + log, üstel geri çekilme), `NotificationPreference`; katalog (`request.created`, `approval.task_assigned`, `request.approved`, `request.rejected`); Workflow olay handler'ları (tekrar teslimde idempotent); Scriban şablonları tr/en/de (`Templates/{key}.{channel}.{lang}.sbn`); SignalR ile kullanıcıya push (Worker → Redis backplane); `EmailDeliveryPollingService` (Worker, `FOR UPDATE SKIP LOCKED`); `/api/v1/notifications/my` (liste, okunmamış sayısı, okundu, tümünü okundu, tercihler GET/PUT); `InitialNotifications` migration'ı; sıcak dosya kayıtları (slnx, ModuleCatalog, Migrator, Worker + appsettings, TestFixture şeması, resx). Api/Worker/Migrator host'ta build yeşil. **Tamamlandı (PC-A makinesi, 2026-09-15):** main'e merge sonrası tam çözüm build'i 0 hata; Worker'da her modül tek kez kayıtlı, Notifications bloğu + `EmailDeliveryPollingService` var. Hata düzeltmesi: `*.tr.sbn`, `labels.en.json` gibi şablon dosyaları MSBuild tarafından kültür dosyası sanılıp uydu (satellite) derlemelere taşınıyordu → ana derlemede gömülü şablon yoktu, her render `InvalidOperationException` veriyordu (`TemplateRendererTests` 30/31 kırmızı); `EmbeddedResource`'a `WithCulture="false"` eklendi. Yeni testler: `NotificationPublishingIntegrationTests` (gerçek PostgreSQL; alıcı başına uygulama içi + Pending e-posta, tercihle e-posta kapatma, hesabı olmayan çalışana yalnız e-posta, aynı olay tekrar yayınlanınca çift kayıt/push yok; işlemci: gönderim + onay linkinin gönderim anında eklenmesi, güncelliğini yitirmiş görevde Skipped, SMTP hatasında Attempts/NextAttemptAt geri çekilmesi) ve Workflow'da `ApprovalDecisionLinkIssuerTests` (yalnız bekleyen güncel adım + somut onaycıda link, DB'de yalnız hash; sonraki adım / rol kuyruğu / başka tenant / karar verilmiş talep → null). Notifications.Tests 33/33, Workflow.Tests 17/17, Architecture 30/30, Employee 82/82, Identity 46/46, Integration `NotificationsHub*` 2/2; tam `dotnet test Sense.slnx` yalnız bilinen 4 `BackgroundJobServiceTests` hatasıyla yeşil. Contractor rolüne `notifications.my.*` eklendi (`SystemRoleDefinitions`; mevcut tenant'lara senkronizör taşır). Web: başlıkta zil (okunmamış rozeti, 45 sn polling, son 10 bildirim, tıklayınca okundu + `actionUrl`'e gider, tümünü okundu, ayarlara link) ve `/app/notifications/settings` tercih sayfası (şablon × kanal anahtarları, kilitli kanallar pasif); `notifications` i18n namespace'i tr/en/de; `notifications.my.view`/`manage` iznine göre gizleniyor; tsc/eslint/vite build temiz, tarayıcıda tıklanarak denenmedi. **Kalan:** web `@microsoft/signalr` istemcisi (şimdilik polling; hub + sunucu push hazır); ayrı `/app/notifications` tam liste sayfası; Scriban 6.6.0 NuGet güvenlik uyarıları (NU1902-1904; şablonlar gömülü/güvenilir ama sürüm yükseltme değerlendirilmeli); `ManagerChanged` hâlâ dinlenmiyor (katalogda şablonu yok); Workflow olayı → Worker outbox → handler → e-posta zincirinin tüm host üzerinden uçtan uca testi yok (parçalar ayrı ayrı test edildi). |
| B3 `LegislationParameter` ADR | 🟡 Önerildi | `b/leave-engine` | [ADR 0008](../adr/0008-legislation-parameter.md): okuma portu `Sense.Shared.Contracts.Legislation.ILegislationParameterReader` (+ `LegislationParameterKeys`), tarih aralıklı veri + seed şimdilik Leave şemasında (`leave.legislation_parameters`, `HasData`, 24 satır, her satırda kaynak); sahip modül kararı PC-A ile birlikte verilecek |
| B3 Leave backend motoru | 🟡 Kısmi | `b/leave-engine`, `b/leave-overtime`, `b/leave-web` | Modül iskeleti (5 proje, şema `leave`, `InitialLeave` migration, catalog/Migrator/Worker/slnx); `LeaveRuleSet`/`LeaveType` (sihirbaz alan seti; `IsSensitive` takvimde tür adını zorla kapatır), kıdem kademeleri, `LeaveLedgerEntry` (`numeric(9,4)`, negatif devir), `LeaveBalanceEngine` (dönem, yasal alt sınır + yaş istisnası, Accrual/CarryOver yeniden üretimi, izin başlangıç tarihine göre bakiye projeksiyonu), `LeaveDayCounter` + `ILeaveDayCalculator` (Pzt–Cum yer tutucu + Organization tatil takvimi), `CloneLeaveRuleSet`, "Örnek Türk İş Kanunu kuralı" seed'i; `EmployeeHired` tüketicisi (profil + varsayılan set + kümülatif tahakkuk); `LeaveRequest` (gönder / bekleyeni düzenle / geri çek, gün hesabı, bakiye + çakışma doğrulaması, Workflow `Leave` onayı, `ApprovalCompleted` → Usage, `ApprovalRejected`); API `leave/rule-sets`, `leave/employees/{id}/{balances,ledger,requests,adjustments,recalculate}`, `leave/me/{balances,requests}`. `b/leave-overtime`: FM talebi (#26: `OvertimeRequest` gönder/geri çek/adına ekle, `OverlapWithScheduleMinutes` + `PayableMinutes` — yer tutucu Pzt–Cum 09–18 planlı saat, tatil günleri düşülür —, `DayKind`, çakışan FM hatası, uyarılar: onay yok / gece onayı yok / planlı saat çakışması / yıllık `Overtime.AnnualLimitHours`; Workflow `Overtime` onayı + `ApprovalCompleted/Rejected`), `OvertimeConsent` (#27: süresiz, geri alma `+Overtime.ConsentRevocationNoticeDays`, yürürlükteki geri alma → `leave.errors.overtime_consent_revoked`), TimeOff dönüşümü (#27: `OvertimeConversion{MinutesBasis, RateSnapshot (vars. 1,5), ResultMinutes, ResultDays}` → ExtraGrant ledger satırı, gün = dakika / 450 (7,5 sa yer tutucu), `Full` esası `leave.overtimes.override_payable` ister), günlük `RecalculateBalances` job'ı (#15, Worker hosted service, 01:00 UTC, kiracı bazlı); `LeaveOvertime` migration; API `leave/me/overtimes`, `leave/employees/{id}/{overtimes,overtime-conversions,overtime-consents}`; izinler `leave.overtimes.{read,create_on_behalf,convert,override_payable}`, `leave.my.overtimes`, `leave.overtime_consents.manage`. Olaylar (Leave.Contracts, outbox): `OvertimeRequestSubmitted`, `OvertimeApproved`, `OvertimeRejected`, `OvertimeConvertedToTimeOff`, `OvertimeConsentRecorded`. Yok: toplu kural atama, FM → Pay dönüşümü / dönüşüm düzenleme-geri alma / ekstra izin son kullanma (`CompTimeExpiryMonths`), FM yasak grupları, `AnnualLimitMode=Block`, FM olaylarının tüketicileri (bildirim şablonları), onaylı iptal/erken dönüş, web ekranları **Web (b/leave-web, 2026-09-15, PC-A makinesinde kullanıcı isteğiyle):** çalışan kartı İzinler sekmesi (Özet + Detaylar ledger diyaloğu + talepler, İşlemler: bakiye düzeltme/yeniden hesapla), `/app/leave` (bakiyelerim, "+ Talep", bekleyen talebi düzenle/geri çek), `/app/settings/leave/rule-sets` (liste, türler, kopyala) — yalnız rota, menü yok. **`b/leave-gaps` (2026-09-15):** `GET leave/me/leave-types` (`leave.my.requests`; aktif + self-servis türler, ad API'de `IErrorLocalizer` ile çevrilir), `GET leave/me/requests/preview?leaveTypeId&start&end[&editedRequestId]` ve `GET leave/employees/{id}/requests/preview` (`ILeaveDayCalculator` gün/hafta tatili/resmi tatil + kural ihlalleri çevrilmiş uyarı olarak), `LeaveRequestDto.leaveTypeName` (çalışanın kural setinden, çevrilmiş), `GET leave/me/ledger` (`leave.my.balances`), `POST leave/employees/{id}/requests` (İşlemler → İzin Ekle, `Source=OnBehalf`, self-servis bayrağı aranmaz; `bypassApproval=true` → Workflow başlatılmadan Approved + Usage satırı, `leave.requests.bypass_approval` ister, yoksa `leave.errors.bypass_approval_not_allowed`); izinler `leave.requests.{create_on_behalf,bypass_approval}` (HR `leave.*`); web: "+ Talep" türleri `/me/leave-types`, canlı gün önizleme, kendi kartında "Detaylar", kart İşlemler → "İzin Ekle" (onay atlama anahtarı). Web'in bulduğu kalan backend eksikleri: submit yalnız id dönüyor; `LeaveRequestDto`'da onaycı/karar notu yok; kural seti varsayılan yap/yeniden adlandır/sil yok. **`b/leave-web-gaps` (2026-09-15, PC-A makinesinde kullanıcı isteğiyle):** `SubmitLeaveRequestCommand`/`CreateLeaveRequestOnBehalfCommand` artık `Guid` değil tam `LeaveRequestDto` döndürüyor (web `submitLeaveRequest`/`createLeaveRequestOnBehalf` dönüş tipi güncellendi; `LeaveRequestDialog`'un `save` prop'u zaten `Promise<unknown>` olduğundan başka çağrı noktası etkilenmedi); `LeaveRequestDetailDto`'daki onay adımına (`LeaveApprovalStepDto`) karar notu eklendi (Workflow `ApprovalStepStatus`/`IApprovalRequestLookup` → `Comment` alanı `ApprovalTask.Comment`'ten aktarılıyor; gizli adımlarda notu da gizler), web detay çekmecesinde adımın altında gösteriliyor. Not: kural seti varsayılan/yeniden adlandır/sil komutları incelemede zaten mevcut çıktı (`SetDefaultLeaveRuleSetCommand`, `RenameLeaveRuleSetCommand`, `DeleteLeaveRuleSetCommand`) — yukarıdaki not güncel değildi, gerçek eksik yalnız submit dönüşü ve karar notuydu. Doğrulama: Leave.Tests 53/53, Workflow.Tests 17/17, tam `dotnet build Sense.slnx` 0 hata. Web `tsc`/`eslint` bu oturumda çalıştırılamadı (host'ta node/pnpm bulunamadı); değişiklik küçük ve tip uyumlu (dönüş tipi genişletme + tek opsiyonel alan), ama bir sonraki fırsatta doğrulanmalı. Arayüz tarayıcıda denenmedi. |

PC-A hattı (kalem ayrıntısı [faz1a-durum-takibi.md](../analysis/kolayik-video/faz1a-durum-takibi.md)):

| Kalem | Durum | Dal | Not |
|---|---|---|---|
| E0 senkronizasyon slotları (S1 birim onay süreci, S2 kart sekmeleri) | ✅ Bitti | `a/sync-slots` | Bulgu: domain event'ler hiç yayınlanmıyordu (bkz. faz1a-durum-takibi.md); Worker'a yalnız Organization için dar kapsamlı düzeltme yapıldı, genel çözüm PC-B ile koordinasyon gerektiriyor |
| E1 özlük alanları + durum modeli + kart bölümleri | ✅ Bitti | `a/e1-personal-employment-fields` | PC-B için: `EmployeeHired`, `EmployeeSuspended`, `EmployeeReactivated` (Employee.Contracts/Events), Worker Employee outbox'ını işliyor |
| E2 kariyer geçmişi | ✅ Bitti | `a/e2-career-history` | PC-B için: `ManagerChanged` (onay zinciri yöneticisi değişince), `CareerChanged` (Employee.Contracts/Events); `EmployeeHired.LeaveAccrualStartDate` artık çalışanın "İzne esas hak ediş tarihi" alanından geliyor |
| E3 maaş + banka | ✅ Bitti | `a/e3-salary-bank` | PC-B için: `SalaryChanged`, `EmployeeProfileChanged{Section}` (Employee.Contracts/Events); yeni izinler `employee.salary.edit`, `employee.bank_accounts.edit` |
| E4 özel alanlar + self-servis | ❌ Başlanmadı | — | |
| E5a işten ayrılış + iş güvencesi + çalışma dönemleri | ❌ Başlanmadı | — | |
| E5b sendika + alt işveren + sözleşme belgesi | ❌ Başlanmadı | — | |
| E6 özlük aktarım şablonu | ❌ Başlanmadı | — | |
| E7 grid filtreleri + ana sayfa | ❌ Başlanmadı | — | |

## 11. Riskler

- **Uzun yaşayan dal:** rebase maliyeti katlanarak artar → günlük rebase, küçük PR.
- **Workflow API'sinin erken donması:** Leave dışındaki tüketicilerin (Expense, vize, ATS) gereksinimleri kaçabilir → B1 başında dört kaynağı birlikte oku; `Contracts` ilk sürümünü küçük tut.
- **Faz 0 takip tablosunun güncel olmaması:** PC-B yanlış "bitti" varsayımıyla bağımlı iş açar → §2.2 zorunlu.
- **Tek geliştirici iki makine:** darboğaz kod yazmak değil, inceleme ve merge olur → PR'ları küçük tut; aynı gün iki makinede aynı sıcak dosyaya dokunmaktan kaçın.
- **Farklı ortam:** PC-B'de Docker imaj sürümleri ve `.env` farklıysa "bende çalışıyor" hataları → `.nvmrc`, `global.json` ve compose dosyalarına uy; `.env` farkları `.env.example`'a yansıtılsın.

## 2026-10-07 — Ayrıntılı izin bakiye raporu dosya sahipliği

`takim/leave-balance-detail`: Leave rapor/projeksiyon dosyaları, Reporting async
sözleşme/handler/job/DI dosyaları, Shared Export PDF seçenekleri ve TR/EN kaynaklar,
web rapor sayfası/viewer/service ve TR/EN/DE rapor etiketleri bu işin geliştiricisine aittir.
Mevcut ortak dosyalarda diğer oturumların değişiklikleri korunur. Ana oturum iş kartı,
handoff ve Faz 4 durum belgesini yönetir; bağımsız gözden geçirici salt okuma yapar.

## 2026-10-14 — SAP HCM sicil yönetimi dosya sahipliği

Backend geliştiricisi Employee domain/application/infrastructure/API ve migration,
Employee/etkilenen modül testleri, permission/system role ve Shared TR/EN kaynaklarını
yönetir. Web geliştiricisi sicil servisi, ayarlar, çalışan oluşturma/kart, rota/menü,
TR/EN/DE çeviri ve web testlerini yönetir. Ana oturum `docs/**` sahibi; bağımsız
gözden geçirici salt okuma ile testleri tekrar çalıştırır. Mevcut cost-center ve
organizasyon şeması değişiklikleri korunur; ortak dosyalarda yalnız ilgili eklemeler
yapılır. Sözleşme: `sap-hcm-sicil-yonetimi.md`.
