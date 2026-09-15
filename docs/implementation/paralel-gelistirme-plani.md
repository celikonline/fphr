# Paralel Geliştirme Planı — İki Makine (PC-A / PC-B)

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
| Attendance | 2 | Leave (`TimesheetTreatment`), `WorkCalendar` | ❌ Leave motorundan sonra |
| Payroll | 3 | `LegislationParameter`, Attendance, Leave, FM dönüşümü | 🟡 Hesap motoru + golden set saf domain olarak mümkün (en uzun ve riskli faz); ancak kapasite varsa |
| Expense | 4 | Workflow, Documents | ❌ Workflow'dan sonra |
| Assets, Announcements (+Support), Training | 4 | Employee, Documents | Teknik olarak mümkün, öncelik düşük |
| Performance, Compensation (+Benefits adayı) | 5 | Employee, Organization, Payroll (Compensation) | ❌ |
| Recruitment (ATS) | 6 | Identity rolleri, `Location`/`OrgUnit` | Teknik olarak bağımsız; yol haritası Faz 5 ile paralel öneriyor — şimdi önerilmez |
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
| Faz 0 #4 `OrgUnit` onay slotu | PC-A | Workflow birim bazlı onaycı ataması + devralma |
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
| §2 ön koşullar (PC-A) | ❌ Başlanmadı | — | |
| §3 PC-B kurulumu + baz doğrulama | ❌ Başlanmadı | — | |
| B1 Workflow çekirdeği | ❌ Başlanmadı | — | |
| B2 Notifications | ❌ Başlanmadı | — | |
| B3 `LegislationParameter` ADR | ❌ Başlanmadı | — | |
| B3 Leave backend motoru | ❌ Başlanmadı | — | |

## 11. Riskler

- **Uzun yaşayan dal:** rebase maliyeti katlanarak artar → günlük rebase, küçük PR.
- **Workflow API'sinin erken donması:** Leave dışındaki tüketicilerin (Expense, vize, ATS) gereksinimleri kaçabilir → B1 başında dört kaynağı birlikte oku; `Contracts` ilk sürümünü küçük tut.
- **Faz 0 takip tablosunun güncel olmaması:** PC-B yanlış "bitti" varsayımıyla bağımlı iş açar → §2.2 zorunlu.
- **Tek geliştirici iki makine:** darboğaz kod yazmak değil, inceleme ve merge olur → PR'ları küçük tut; aynı gün iki makinede aynı sıcak dosyaya dokunmaktan kaçın.
- **Farklı ortam:** PC-B'de Docker imaj sürümleri ve `.env` farklıysa "bende çalışıyor" hataları → `.nvmrc`, `global.json` ve compose dosyalarına uy; `.env` farkları `.env.example`'a yansıtılsın.
