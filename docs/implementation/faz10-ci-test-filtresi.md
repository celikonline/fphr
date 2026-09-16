# CI test filtresi (K-8/ADR-0010) — karar: ters işaretleme, `Category!=Integration`

**Karar (ADR-0010, PO, 16:52):** `Category=Unit` / `Category=Integration` çift-filtre yerine **ters
işaretleme**. Testcontainers/gerçek Postgres kullanan sınıflara `[Trait("Category","Integration")]`
eklenir; CI'nin "Unit" adımı `--filter "Category!=Integration"` çalıştırır — trait'siz (bugüne kadar hep
öyle olan) testler otomatik "unit" sayılır, yeni bir test dosyası trait eklemeyi unutursa CI'den
düşmez (B seçeneğinin B'nin aksine "unutma riski" yok — varsayılan davranış zaten dahil olmak). Önceki
turun A/B analizi (proje/namespace filtresi vs. trait ekleme) bu kararla **kapandı**; bu belge önceki
sürümün yerine geçer.

## Bu turda eklenen trait'ler — kaynak: grep + kod okuma

Arama: `grep -rlE "Testcontainers|PostgreSqlContainer|MigrateAsync" tests/ --include=*.cs` (obj/ hariç).
Sonuç 33 dosya → sınıf bazında sınıflandırıldı:

**Trait eklendi (26 dosya, dosya başına tek satır, sınıfın tamamı Integration):**
`tests/Modules/Sense.Modules.Assets.Tests/AssetLookupIntegrationTests.cs`,
`Sense.Modules.Attendance.Tests/AttendanceIntegrationTests.cs`,
`Sense.Modules.DataTransfer.Tests/DataTransferIntegrationTests.cs`,
`Sense.Modules.Employee.Tests/{Career,CustomField,EmployeeAnalyticsQueries,Employee,EmployeeLifecycle,
EmploymentPeriod,Payments,Subcontractor,Termination,UnionMembership}IntegrationTests.cs` (10 dosya),
`Sense.Modules.Expense.Tests/ExpenseClaimIntegrationTests.cs`,
`Sense.Modules.Identity.Tests/UnitAuthorizations/UnitAuthorizationIntegrationTests.cs`,
`Sense.Modules.Identity.Tests/Users/UserAccountLookupIntegrationTests.cs`,
`Sense.Modules.Leave.Tests/LeaveIntegrationTests.cs`,
`Sense.Modules.Notifications.Tests/NotificationPublishingIntegrationTests.cs`,
`Sense.Modules.Organization.Tests/{HolidayCalendar,Location,OrgUnitApprovalPolicy,OrgUnit,
OrganizationStructure}IntegrationTests.cs` (5 dosya),
`Sense.Modules.Recruitment.Tests/RecruitmentIntegrationTests.cs`,
`Sense.Modules.Workflow.Tests/{ApprovalDecisionLinkIssuer,ApprovalRequestIntegration}Tests.cs` (2 dosya).

**Trait eklendi — sınıf bazında (dosyada hem domain hem Integration sınıfı karışık, yalnız Integration
sınıfına eklendi, domain sınıfına dokunulmadı):**
`Sense.Modules.Leave.Tests/LeaveManagementTests.cs` → yalnız `LeaveManagementIntegrationTests`
(`LeaveManagementDomainTests`'e dokunulmadı),
`Sense.Modules.Leave.Tests/LeaveRequestTests.cs` → yalnız `LeaveRequestIntegrationTests`
(`LeaveRequestStateTests`'e dokunulmadı),
`Sense.Modules.Leave.Tests/OvertimeTests.cs` → yalnız `OvertimeIntegrationTests`
(`OvertimeDomainTests`'e dokunulmadı).

**Kararsız kalıp sonradan eklendi (8 dosya) — literal grep bunları yakalamadı çünkü `Testcontainers`/
`MigrateAsync` kelimeleri dosyanın kendisinde değil, miras aldıkları `IdentityStackTestBase` içinde
(`tests/Sense.Tests.Integration/Identity/IdentityStackTestBase.cs:66-68`, üç modülün DbContext'ine
`MigrateAsync` çağırıyor). Semantik olarak gerçek Postgres'e karşı çalıştıkları için trait eklendi:**
`tests/Sense.Tests.Integration/Identity/{AccessSettingsEnforcement,UnitAuthorizationScope,
PlatformImpersonation,AccessTypeRoleSyncAndSelfCard,TenantSlugLookup,Mfa,PasswordReset,
EmployeeInvitation}Tests.cs`.

**Değişiklik yapılmadı — false positive (yalnız yorum satırında "Testcontainers" geçiyor, sınıf mock/NSubstitute
tabanlı, gerçek DB kullanmıyor):**
`Sense.Modules.Documents.Tests/{DocumentExpiryHandler,DocumentType}HandlerTests.cs`,
`Sense.Modules.Payroll.Tests/PayrollCalculatorGoldenSetTests.cs`.

**Değişiklik yapılmadı — sınıfın kendisi değil, temel/fixture sınıfı (soyut, `[Fact]` içermiyor):**
`Sense.Tests.Shared/Fixtures/TestFixture.cs` (`PostgreSqlCollection` dahil), `Sense.Tests.Integration/
Identity/IdentityStackTestBase.cs`.

**Zaten trait'liydi, dokunulmadı:** `Sense.Tests.Integration/AccessSettingsIntegrationTests.cs` (2 test,
metod-seviyesinde `[Trait("Category","Integration")]`), `Sense.Tests.Integration/ExampleIntegrationTest.cs`
(6 örnek/şablon test).

**Bu turda taranmayan, `tests/Sense.Tests.Integration/` kökündeki diğer 4 dosya** (`BackgroundJobServiceTests`,
`CorrelationIdMiddlewareTests`, `NotificationsHubTests`, `RequestHeaderMiddlewareTests`) — literal grep'te
eşleşmedi (Testcontainers/MigrateAsync kelimesi geçmiyor, muhtemelen `WebApplicationFactory` tabanlı ayrı bir
kalıp kullanıyorlar, Postgres testcontainer'ı olmadan da çalışabilirler). Bu turda **kod okumadım** —
sonraki turda teyit edilmeli; teyit edilene kadar `Category!=Integration` filtresiyle "unit" adımında
koşacaklar (yanlış tarafta kalma riski varsa CI süresi/DB bağımlılığı ile ortaya çıkar, davranışsal risk
yok).

## CI filtre satırları (bu turda uygulandı)

`pr.yml:100` ve `main.yml:66`: `--filter "Category=Unit"` → `--filter "Category!=Integration"`.
Integration adımı (`pr.yml:106`, `main.yml:71`, `--filter "Category=Integration"`) **değişmedi** — artık bu
turda eklenen 37 dosyadaki testleri de kapsıyor. Tetikleyicilere (`on:`) dokunulmadı (K-9).

## Etki

Önceki durumda (belgenin önceki sürümü, bkz. git geçmişi) iki filtreli adım da repodaki gerçek testlerin
~%5'ini çalıştırıyordu. Bu turdan sonra: "Integration" adımı 37 dosya + `AccessSettingsIntegrationTests`
(zaten trait'li) = tenant/DB'ye karşı çalışan tüm bilinen sınıfları kapsıyor; "Unit" adımı (`Category!=
Integration`) geri kalan ~19 modül test projesindeki 706 trait'siz testi + `ExampleIntegrationTest.cs`'in
6 örnek testini (bunlar da Testcontainers kullanıyor ama trait'siz kaldı — aşağıya bakın) çalıştırır.

**Bilinen artık:** `ExampleIntegrationTest.cs` TestFixture kullanan 6 örnek/şablon test içeriyor ama trait
taşımıyor (dosya sınırım "yalnız Reporting/Analytics + tests/** trait satırı" idi; bu dosya zaten
"örnek/şablon" olarak işaretli, gerçek özellik testi değil — trait eklemek dosyanın amacı dışına taşımaz
ama bu dosya benim taradığım Testcontainers/PostgreSqlContainer/MigrateAsync grep'inde zaten göründüğü için
burada not düşüyorum: teknik olarak `Category!=Integration` filtresiyle "Unit" adımında koşacak, Postgres
container'ı ayağa kaldıracak — CI süresine küçük bir etkisi olabilir, davranışsal risk yok). Sonraki turda
istenirse tek satır trait eklenebilir.

## Kapsam dışı / doğrulanamadı (araç erişimi yok)

- `dotnet test --filter "Category!=Integration"` bu turda **çalıştırılamadı** (build/test erişimi yok) —
  filtre sözdizimi vstest standardı, ama gerçek koşu bir sonraki build-erişimli turda teyit edilmeli.
- `Sense.Tests.TenantIsolation` ve `Sense.Tests.Contract` projeleri hâlâ **boş** (0 test dosyası) — bu
  belgenin/kararın kapsamı dışı, ayrı bulgu olarak PO'ya bildirildi (önceki tur raporunda).
