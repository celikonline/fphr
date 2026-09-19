# senseik-049 — Denetim Kaydı Kapsamı Genişletilmesi — UYGULAMA NOTU

**Durum: REVIEW (2026-09-19, devam oturumu). Dal `dev`'e rebase edildi (51 commit geride idi; tek çakışma kart JSON'u). Önceki oturumda Docker'a erişilemediği için eksik kalan migration'lar dev üzerinde yeniden üretildi; `dotnet build` + Identity/Employee/Leave/Workflow testleri native dotnet 10.0.401 + Docker Desktop (Testcontainers) ile çalıştırıldı (sonuçlar: kartın `result` alanı). Push/merge yapılmadı.**

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
4. `dotnet ef migrations add Add<Modul>AuditLogEntries -c <TContext> -p <Infrastructure.csproj> -s src/Sense.Migrator` (bkz. Migration bölümü).

## Migration — ÜRETİLDİ (dev üzerine, rebase sonrası)

Önceki oturumun rebase öncesi ürettiği Employee/Leave migration dosyaları (dev'deki yeni Employee/Leave migration'larıyla snapshot çakışacağı için) atıldı; rebase'den sonra modül başına TEK migration yeniden üretildi (`--startup-project src/Sense.Migrator`):

| Modül | Migration |
|---|---|
| Employee | `20260919103003_AddEmployeeAuditLogEntries` |
| Leave | `20260919103044_AddLeaveAuditLogEntries` |
| Workflow | `20260919103116_AddWorkflowAuditLogEntries` |

Her biri yalnız `<schema>.audit_log_entries` tablosunu + `ix_audit_log_entries_tenant_id` (tekil) + `ix_audit_log_entries_tenant_id_occurred_at` (`occurred_at desc`) index'lerini oluşturuyor; başka model farkı yok. `OnModelCreating` sıralama notu doğrulandı (iki index de çıktı). Şema Identity'nin `AddAuditLogEntries` migration'ıyla aynı.

## Test — ÇALIŞTIRILDI

- `Sense.Modules.Identity.Tests/AuditLog/GetAuditLogHandlerTests.cs`: çoklu `IAuditLogSource` birleştirme/sıralama (unit).
- `Sense.Modules.Employee.Tests/AuditLog/EmployeeAuditLogInterceptorIntegrationTests.cs`: create; `BirthDate`/`HireDate` diff'i; `AuditedFields` dışı alan → genel özet.
- `Sense.Modules.Leave.Tests/AuditLog/LeaveAuditLogInterceptorIntegrationTests.cs`: LeaveRequest create / onay (`Pending → Approved`) / ret (`Pending → Rejected`, bu oturumda eklendi); OvertimeRequest create / onay.
- `Sense.Modules.Workflow.Tests/AuditLog/WorkflowAuditLogInterceptorIntegrationTests.cs`: ApprovalRequest create + `Cancel` → `Pending → Cancelled`.

Bu oturumda `AuditLogInterceptor`'a details uzunluk koruması da eklendi: alan diff'i (ör. serbest metin `CancellationReason`) `details` kolonunun 2000 karakter sınırını aşarsa kesilip `…` ekleniyor — aksi halde audit satırı asıl iş işleminin SaveChanges'ını da düşürürdü.

**Merge öncesi kalan (insan):** merge gate'in "demo'da 3+ farklı işlem türü denetim kaydında doğru görünüyor" kısmı — demo ortamında çalışan düzenleme, izin onayı ve FM onayı yapıp denetim kaydı ekranında kontrol edin. Kaynak sütununda CLR tip adı görünür (`Employee`, `LeaveRequest`, `OvertimeRequest`, `ApprovalRequest`); web bu değeri çevirmeden gösteriyor.

## Kalan İşler / Takip Önerileri

1. ~~Migration + build/test doğrulaması~~ — tamamlandı (2026-09-19 devam oturumu). Demo doğrulaması insan tarafında.
2. **Diğer modüller** (Documents/Organization/Assets/Training/Recruitment/Expense/Onboarding/Announcements/Performance) — yukarıdaki "Yeni bir modüle nasıl eklenir" 4 adımıyla genişletilebilir, bu kart bilinçli olarak kapsam dışı bıraktı.
3. **Alan bazlı değer maskeleme** (`SensitiveFields`) — şu an satır-bazlı (`Details` tamamı) maskeleniyor; sendika/sağlık gibi özel nitelikli alanlar için "alan değişti ama değer gizli" ayrımı istenirse ayrı bir tur.
4. **DB-side sayfalama** — `IAuditLogSource.QueryAsync` şu an her kaynağın TÜM eşleşen satırlarını döndürüyor, birleştirme/sayfalama bellek içinde. Tenant başına audit hacmi büyürse (çok yıllık üretim verisi) DB'de `UNION ALL` + `OFFSET/LIMIT`'e geçmek gerekebilir.
5. **Login/invite/role_change action'ları** (senseik-018 handoff'unda not edilen yan bulgu) hâlâ hiçbir kod yolunda üretilmiyor — bu kartın da kapsamı dışında kaldı.
