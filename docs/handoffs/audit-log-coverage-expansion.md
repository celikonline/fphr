# senseik-049 — Denetim Kaydı Kapsamı Genişletilmesi — UYGULAMA NOTU

**Durum: Kod tamam, BLOKE (GATE 1/GATE 2 önceden onaylı). `a/audit-log-coverage-expansion` dalında commit edildi (509aead); push yapılmadı. Bu oturumda Docker'a erişilemedi — `docker ps`/`docker info`/`docker version` gibi en temel komutlar bile "failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine" hatasıyla sürekli başarısız oldu (yalnızca yavaşlık/kuyruklanma değil, pipe'a bağlanamama). Yalnızca `git commit` (husky pre-commit hook'u tetiklemiş olsa da) çalıştı — hook'un `dotnet format` adımının bu izole worktree'de hiç tetiklenmediği görülüyor (commit çıktısında format'a dair hiçbir iz yok), muhtemelen bu agent oturumunun sandbox'ı Docker soket/pipe erişimini kasıtlı olarak kısıtlıyor. `dotnet build`/`dotnet test`/`dotnet ef migrations add` bu oturumda HİÇ çalıştırılamadı - tekrar denemek yerine bir sonraki oturumda/insan tarafından çalıştırılması gerekiyor. Aşağıdaki "Migration" ve "Test" bölümleri neyin YAZILDIĞINI (ve neyin hâlâ eksik olduğunu) anlatıyor — "çalıştırıldı ve geçti" anlamına gelmiyor.**

## senseik-018'den Farkı (ÖNEMLİ)

senseik-018 (`a/audit-log-fix`, `dev`'de birleşti) **yalnızca Identity modülünün** (`IdentityDbContext` — User/Role/Invitation/...) audit interceptor'ını düzeltti: actor artık gerçek kullanıcı, `Source` (Web/Api/Import/System) doğru kaydediliyor. O tur, interceptor'ı Identity dışına **hiç bağlamadı** — `IdentityAuditLogInterceptor`, `AddModuleDbContext<IdentityDbContext>`'in `configureAdditional` parametresiyle yalnız Identity'nin DbContext'ine takılıydı. Employee/Leave/Workflow'un kendi DbContext'lerinde bu interceptor hiç yoktu, dolayısıyla bu modüllerdeki HİÇBİR create/update/delete denetim kaydına düşmüyordu — `IAuditLogged` işaretli olsalar bile (bkz. aşağıda "Zaten IAuditLogged olan ama hiç loglanmayan agregatlar").

Bu kart, aynı deseni **mimari olarak genelleştirip** Employee/Leave (Overtime dahil)/Workflow'a **genişletti** + üstüne **alan bazlı diff** (senseik-018'in bilinçli olarak ertelediği "Alan bazlı diff YOK" kararını bu turda uyguladı) ekledi.

## Mimari Karar: Neden Identity'nin AuditLogEntry'si Taşınmadı

Identity'nin `AuditLogEntry`/`IdentityAuditLogInterceptor`/`AuditActions` üçlüsü (`src/Modules/Identity/Sense.Modules.Identity.Infrastructure/Persistence/`) **hiç değiştirilmedi**. EF Core migration snapshot'ı entity'yi CLR tam adına göre eşleştirir; bu tipi yeni bir namespace'e taşımak "tabloyu sil + yeniden oluştur" migration'ı üretir — 83 demo denetim kaydı dahil veri kaybı riski. Bunun yerine:

- **Yeni paylaşımlı yazma altyapısı**, Outbox/Inbox ile AYNI desende (`ModuleDbContext.OnModelCreating`'in zaten uyguladığı desen): `Sense.Shared.Infrastructure.Persistence.Audit.AuditLogEntry` + `AuditLogEntryConfiguration` + `AuditActions` (Create/Update/Delete) + `AuditLogInterceptor` (Identity'nin `IdentityAuditLogInterceptor`'ının genelleştirilmiş hali — `ICurrentUser`/`IAuditSourceAccessor`/`TimeProvider`/`ILogger` alıyor, herhangi bir `ModuleDbContext`'e bağlanabiliyor).
- Her yeni modül (Employee/Leave/Workflow) bu paylaşımlı `AuditLogEntry` tipini **kendi şemasında ayrı bir tabloya** map ediyor (`employee.audit_log_entries`, `leave.audit_log_entries`, `workflow.audit_log_entries`) — `DbContext.OnModelCreating`'i override edip `modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration())` çağırarak. Bilerek TÜM modüllere (ör. Documents/Organization/Assets) otomatik uygulanmadı (Outbox/Inbox'ın aksine) — kapsam dışı modüllere migration eklemek bu kartın hedefi değildi, paralel çalışan diğer agent'ların şemasına dokunma riskini artırırdı.
- Okuma tarafı: yeni `Sense.Shared.Contracts.Audit.IAuditLogSource` sözleşmesi (Contracts katmanında — "modüller yalnız Contracts üzerinden konuşur" kuralına uyar). Her modül kendi audit tablosunu bu arayüz üzerinden sağlar: Employee/Leave/Workflow için tek, paylaşımlı `Sense.Shared.Infrastructure.Persistence.Audit.ModuleAuditLogSource<TContext>` (generic, DbSet<AuditLogEntry> okuyor); Identity için ayrı `IdentityAuditLogSource` (kendi CLR tipini `AuditLogEntrySnapshot`'a çeviren ince adaptör — eski `AuditLogReadStore`'un yerini aldı). `GetAuditLogHandler` (Identity.Application) artık tek bir `IAuditLogReadStore` değil, DI konteynerine kayıtlı **her** `IAuditLogSource`'u sorgulayıp (`IEnumerable<IAuditLogSource>`) sonuçları bellek içinde birleştirip (`OrderByDescending(OccurredAt)`) sayfalıyor. Tenant başına audit hacmi bu aşamada DB'de UNION ALL gerektirecek boyutta değil; ileride gerekirse `IAuditLogSource.QueryAsync` DB-side sayfalamaya genişletilebilir (arayüz sözleşmesi bunu engellemiyor).

## Alan Bazlı Diff (KVKK m.12, kabul kriteri 5)

`IAuditLogged` arayüzüne (`Sense.Shared.Kernel.Domain.Markers.cs`) yeni bir `static virtual IReadOnlyList<string> AuditedFields => []` üyesi eklendi (var olan `SensitiveFields`'la aynı desen — implicit static property ile override edilir, ör. `Identity.Domain.Users.User.SensitiveFields`). `AuditLogInterceptor`, yalnız `action == update` olduğunda ve agregatın `AuditedFields` listesindeki alanlardan gerçekten `IsModified` olanlar için `"{Resource} update: Alan: eski → yeni; ..."` biçiminde bir özet üretir; liste boşsa veya hiçbir izlenen alan değişmemişse eski davranışa ("{Resource} {action}") döner. Static virtual bir interface üyesini yalnızca `Type`'tan (generic olmadan) okumak için reflection kullanıldı (`GetProperty(nameof(IAuditLogged.AuditedFields), Public|Static)`, sonuç `ConcurrentDictionary<Type,...>`'te cache'leniyor) — aynı dosyadaki `TryGetResourceId`'nin zaten kullandığı reflection deseniyle tutarlı.

`AuditedFields` eklenen agregatlar:

| Agregat | Modül | Alanlar |
|---|---|---|
| `Employee` | Employee | `BirthDate`, `HireDate`, `SgkExitCode`, `TerminationDate`, `Status` |
| `LeaveRequest` | Leave | `Status`, `CancellationReason` |
| `OvertimeRequest` | Leave | `Status` |
| `ApprovalRequest` | Workflow | `Status` |

`SensitiveFields` (ayrı bir kavram — Details'teki DEĞERİ maskelemek için) bu turda kullanılmadı: mevcut satır-bazlı maskeleme (`identity.audit.sensitive_view` izni yoksa TÜM `Details` maskelenir, `GetAuditLogHandler.Mask`) KVKK gereğini zaten karşılıyor. Alan bazlı değer maskeleme (ör. sendika/sağlık bilgisi için "Alan değişti" gösterip değeri gizleme) istenirse `SensitiveFields`'in kullanılmaya başlanması ayrı, küçük bir takip işi (bkz. Kalan İşler).

## Zaten IAuditLogged Olan Ama Hiç Loglanmayan Agregatlar (Kapsam Dışı)

Kod taramasında `IAuditLogged` işaretli ama interceptor'a hiç bağlı olmayan (bu kartın kapsamına girmeyen) modüller: Documents (VisaDocumentRequest/Process, Document, EmployeeDocumentRequirement, DocumentTypeDefinition), Organization (CostCenter, Company, OrgUnit, OrgUnitType, Location, HolidayCalendar(Entry), OrgUnitApprovalPolicyAssignment), Assets (Asset, AssetCategory), Training (TrainingProgram), Recruitment (Tag, JobRequisition, JobPosting, FormTemplate/Response, Candidate, JobApplication, AutomationRule), Expense (ExpenseClaim, ExpenseCategory, AdvanceRequest), Onboarding (OnboardingTaskTemplate, EmployeeOnboardingTask), Announcements (Announcement, SupportTicket), Performance (ReviewForm, ReviewCycle, Goal), Identity (UnitAuthorization, ImpersonationGrant — Identity'nin interceptor'ı bunları zaten kapsıyor, sadece not için). Bu kart bunlara dokunmadı; genişletme deseni artık hazır (bkz. "Yeni bir modüle nasıl eklenir" altındaki 4 adım), bir sonraki tur aynı 4 adımı tekrarlayarak istenen modülü ekleyebilir.

## Yeni Bir Modüle Nasıl Eklenir (takip turları için)

1. Modülün DbContext'ine `DbSet<AuditLogEntry> AuditLogEntries` + `OnModelCreating` override'ında `modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration())` (bkz. `EmployeeDbContext`/`LeaveDbContext`/`WorkflowDbContext`).
2. `*Module.cs`'de `services.AddScoped<AuditLogInterceptor>()` + `AddModuleDbContext<TContext>(..., (sp, o) => o.AddInterceptors(sp.GetRequiredService<AuditLogInterceptor>()))` + `services.AddScoped<IAuditLogSource, ModuleAuditLogSource<TContext>>()`.
3. Gerekirse kritik agregatlara `public static IReadOnlyList<string> AuditedFields { get; } = [nameof(Alan1), ...];` ekle.
4. `dotnet ef migrations add Add<Modul>AuditLogEntries -c <TContext> -p <Infrastructure.csproj> -s src/Sense.Api` (bkz. Migration bölümü).

## Migration — HENÜZ ÜRETİLMEDİ (kalan iş, merge'i bloke eder)

`employee.audit_log_entries`, `leave.audit_log_entries`, `workflow.audit_log_entries` için EF migration'ları **bu oturumda ÜRETİLEMEDİ** (Docker'a erişilemedi — yukarıdaki not). `EmployeeDbContext`/`LeaveDbContext`/`WorkflowDbContext` kod tarafında `AuditLogEntry`'yi zaten map ediyor (bkz. yukarıdaki mimari bölüm), yani ilk `dotnet build` migration olmadan da derlenir ama `Database.MigrateAsync()`/gerçek DB'ye karşı hiçbir test bu tablo olmadan geçemez — hem yeni testler (aşağıda) hem de olası "pending model changes" runtime kontrolü bunu hemen ortaya çıkarır. Docker tekrar müsait olduğunda:

```
dotnet ef migrations add AddEmployeeAuditLogEntries --project src/Modules/Employee/Sense.Modules.Employee.Infrastructure --startup-project src/Sense.Api --context EmployeeDbContext
dotnet ef migrations add AddLeaveAuditLogEntries     --project src/Modules/Leave/Sense.Modules.Leave.Infrastructure         --startup-project src/Sense.Api --context LeaveDbContext
dotnet ef migrations add AddWorkflowAuditLogEntries  --project src/Modules/Workflow/Sense.Modules.Workflow.Infrastructure   --startup-project src/Sense.Api --context WorkflowDbContext
```

Beklenen şema, Identity'nin `20260916142651_AddAuditLogEntries` migration'ıyla birebir aynı olmalı (append-only, `(tenant_id, occurred_at desc)` composite index + otomatik tek-kolonlu tenant_id index — bkz. `ModuleDbContext`'in Tenant query filter döngüsü). **DİKKAT:** `EmployeeDbContext`/`LeaveDbContext`/`WorkflowDbContext`'in `OnModelCreating` override'ı `AuditLogEntryConfiguration`'ı `base.OnModelCreating(modelBuilder)`'dan ÖNCE uyguluyor (bilerek — sonra uygulanırsa Tenant query filter/index döngüsünü kaçırır, bkz. dosyalardaki doc comment); üretilen migration'da `ix_<tablo>_tenant_id` (tekil) VE `ix_<tablo>_tenant_id_occurred_at` (bileşik) index'lerinin İKİSİNİN de çıktığını doğrulayın - yalnız biri çıkarsa bu ordering fix'i bir yerde bozulmuş demektir.

## Test — YAZILDI, ÇALIŞTIRILAMADI

- `Sense.Modules.Identity.Tests/AuditLog/GetAuditLogHandlerTests.cs`: `IAuditLogReadStore` yerine `IEnumerable<IAuditLogSource>` mock'lanıyor; yeni test `Handle_MergesMultipleSources_OrderedByOccurredAtDescending` iki farklı modülden (Identity + Employee) gelen kayıtların tek, zaman sıralı sayfada birleştiğini doğruluyor. (Bu dosya DB'ye dokunmuyor, saf unit test — Docker gerektirmez, ama yine de bu oturumda çalıştırılamadı.)
- `Sense.Modules.Employee.Tests/AuditLog/EmployeeAuditLogInterceptorIntegrationTests.cs`: create → `AuditActions.Create` + Resource="Employee"; `BirthDate`+`HireDate` güncellemesi → Details'te "BirthDate: 1990-01-01 → 1990-05-20" ve "HireDate: — → 2026-01-15"; `AuditedFields` dışı bir alan (WorkPhone) değişince genel özete düşüyor (regresyon testi).
- `Sense.Modules.Leave.Tests/AuditLog/LeaveAuditLogInterceptorIntegrationTests.cs`: LeaveRequest create + `ApproveWithoutWorkflow` → "Status: Pending → Approved"; OvertimeRequest create + `Approve` → aynı diff deseni.
- `Sense.Modules.Workflow.Tests/AuditLog/WorkflowAuditLogInterceptorIntegrationTests.cs`: ApprovalRequest create + `Cancel` → "Status: Pending → Cancelled" (Approval kaynağının denetim ekranında görünmesi buradan doğrulanıyor).

Tümü `IdentityAuditLogInterceptorIntegrationTests.cs`'in minimal kurulum desenini izliyor (yalnız `AuditLogInterceptor` + `AddModuleDbContext<TContext>` — modülün tam DI grafiğini kurmuyor, Testcontainers PostgreSQL'e karşı gerçek round-trip). Bu 3 entegrasyon testi migration'lar üretilmeden `Database.MigrateAsync()` çağrısında hata verecektir — migration'ları önce üretin.

**Bu kartı devralan/merge eden kişi için net TODO listesi:**
1. Docker müsait olduğunda 3 migration'ı üret (yukarıdaki komutlar) ve üretilen migration dosyalarını gözden geçir (index'ler doğru mu - bkz. Migration bölümündeki DİKKAT notu).
2. `dotnet build Sense.slnx` — derleme hatası beklenmiyor (kod mevcut çalışan desenleri birebir taklit ediyor: senseik-018'in interceptor'ı, CareerIntegrationTests'in DI kurulumu) ama bu iddia TEYİT EDİLMEDİ, ilk adım bu olmalı.
3. `dotnet test Sense.slnx --filter "FullyQualifiedName~AuditLog"` (Identity/Employee/Leave/Workflow.Tests) — yeşil olmalı.
4. Yeşilse card state'ini `blocked`'tan `review`'a çek.

## Kalan İşler / Takip Önerileri

1. **Migration + build/test doğrulaması** (yukarıdaki TODO 1-3) — bu kartın merge gate'ini karşılamak için ZORUNLU, kapsam dışı değil, yalnızca bu oturumun altyapı kısıtı yüzünden ertelendi.
2. **Diğer modüller** (Documents/Organization/Assets/Training/Recruitment/Expense/Onboarding/Announcements/Performance) — yukarıdaki "Yeni bir modüle nasıl eklenir" 4 adımıyla genişletilebilir, bu kart bilinçli olarak kapsam dışı bıraktı.
3. **Alan bazlı değer maskeleme** (`SensitiveFields`) — şu an satır-bazlı (`Details` tamamı) maskeleniyor; sendika/sağlık gibi özel nitelikli alanlar için "alan değişti ama değer gizli" ayrımı istenirse ayrı bir tur.
4. **DB-side sayfalama** — `IAuditLogSource.QueryAsync` şu an her kaynağın TÜM eşleşen satırlarını döndürüyor, birleştirme/sayfalama bellek içinde. Tenant başına audit hacmi büyürse (çok yıllık üretim verisi) DB'de `UNION ALL` + `OFFSET/LIMIT`'e geçmek gerekebilir.
5. **Login/invite/role_change action'ları** (senseik-018 handoff'unda not edilen yan bulgu) hâlâ hiçbir kod yolunda üretilmiyor — bu kartın da kapsamı dışında kaldı.
