# Mobil ZenHR özellikleri — uygulama planı (2026-09-28)

- Dal: `a/mobil-ozellikler` (`a/mobil-yeniden-tasarim` @637957f7 / PR #134 üstüne yığılı). Branch sonu yalnız PR.
- Kapsam: FG-001…FG-028 (FG-006, FG-023, FG-029 hariç 26 kart) + QA-006. Kaynak: `docs/takim/isler/*.json`, `docs/analysis/zenhr-mobil-ozellik-karsilastirmasi.md`.
- Yürütme: tek çalışma ağacı, **paralellik yok**; parçalar aşağıdaki sırayla, her biri kendi içinde yeşil biter (yarım değişiklik bir sonraki parçaya kalmaz).

## 0. Ortak kurallar (her parça)

1. **i18n baştan:** P01'den sonra her mobil metin `t()` ile; yeni anahtarlar `src/locales/{tr,en}/*.json`'a eklenir (ar'yi P37 ekler). Web metinleri `web/public/locales/{tr,en,de}`'ye. Backend hata kodları `SharedResource.resx` + `.en.resx` **sonuna**; bildirim şablonları tr/en/de (ar'yi P36 ekler).
2. **Paylaşılan kayıt dosyaları yalnız sona ekleme:** mobil `types/navigation.ts`, `navigation/RootNavigator.tsx`, `home/ActionGrid.tsx` (ACTION_PAGES), `profile/ProfileMenuCard.tsx`, `lib/requestOptions.ts`, `requests/requestCards.ts` (REQUEST_FILTERS), `NewRequestScreen.tsx` (RequestForm switch), `approvals/approvalLabels.ts`, locale json'ları, `package.json`/`pnpm-lock.yaml`; backend `Organization.Contracts/IOrgApprovalPolicyResolver.cs` (ApprovalRequestTypes + All), `Workflow.Contracts/IApprovalRequestSummaryProvider.cs` (ApprovalRequestSummary'e isteğe bağlı son parametre), modül `*Permissions.All` listeleri, `Notifications/.../Templates/labels.*.json`, `Sense.Worker/Program.cs`, `Employee.Contracts` arayüzleri.
3. **İzin anahtarları:** yeni self anahtarlar `&lt;modül&gt;.my.&lt;ad&gt;` biçiminde → `PermissionPatterns.SelfService` ile Çalışan (ve Müdür) rolüne kendiliğinden gider, `SystemRolePermissionSynchronizer` mevcut kiracılara yayar; **SystemRoleDefinitions değişmez** (Identity riskli alanına girmemek için). İK anahtarları `.manage` son ekiyle (HR/Owner `*` ile alır; `.view` kullanılmaz çünkü Müdür'ün `employee.*.view`/`documents.*.view` jokerleri eşler). İzin adları resx'e eklenir.
4. **Migration:** gerçek EF migration, `dotnet-ef` ile SDK konteynerinde üretilir, elle yazılmaz/düzenlenmez; hepsi riskli → insan onayı.
5. **Doğrulama:** test kapısı `node .claude/takim/betikler/test-kapisi.mjs` yalnız dotnet + web'i koşturur, **mobili koşturmaz**. Her node parçası ek olarak şunları koşturur ve çıktısını rapora koyar (bu Windows makinesinde host'ta node/pnpm/dotnet yok, Docker ile): `pnpm --dir apps/senseik-mobile exec tsc --noEmit`, `... exec eslint src`, `... exec jest`. Web'e dokunan parça kapıyı da yeşil geçirir.
6. **Riskli parça:** gözden geçirici "İNSAN ONAYI GEREKLİ" verir; yetki davranışı değişen her parçada olumsuz test (yetkisiz → 404/403) zorunlu. PII/para değerleri loglanmaz, test verisi uydurmadır.
7. UI işleri mevcut Sense HR tasarım dilini (GlassCard, StatusPill, BottomSheet, SegmentedControl) kullanır; yeni tasarım dili açılmaz (org kuralı gereği `frontend-standards` yalnız web sayfalarında).

## 1. Doğrulama: kartlardaki "mevcut uçlar"

Yollar `api/v1` önekli. ✔ = kartla uyumlu, ✖ = kart yanlış/eksik.

| Kart | Uç | Controller:satır | Yetki | Not |
|---|---|---|---|---|
| FG-001 | GET identity/me | Identity.Api/Controllers/AuthController.cs:105 (MeController) | [Authorize] | ✔ MeDto(userId, email, firstName, lastName, employeeId, **preferredLanguage**, mfaEnabled, tenant, roles, permissions, scopes, features) Dtos.cs:84. Self dil güncelleme ucu **yok** ✔ (yalnız `User.UpdateProfile`, admin). |
| FG-001/003 | Kültürler | Shared.Contracts/Configuration/SenseOptions.cs:51 | – | ✖ `src/Sense.Api/appsettings.json:31` listeyi `["tr-TR","en-US"]` ile eziyor; ar eklenirken ikisi de değişmeli. Bildirim render dilleri `ScribanNotificationTemplateRenderer.cs:41` = tr,en,de; şablon yoksa **kiracı varsayılanına (tr)** düşer, karttaki "en'e düşer" yanlış. |
| FG-022 | GET/POST identity/me/mfa, /enroll, /verify, /disable | AuthController.cs:125,130,135,141 | [Authorize] | ✔ uçlar var: MfaStatusDto(enabled, pendingEnrollment), MfaEnrollmentDto(secret, otpAuthUri), body {code}. ✖ `DisableMfaHandler` (Application/Auth/Mfa.cs:118-141) **politika kontrolü yapmıyor**, MfaStatusDto'da "politika zorunlu" bilgisi yok. |
| FG-004 | GET attendance/timesheet/me | Attendance.Api/Controllers/TimesheetController.cs:44 | attendance.my.timesheet | ✔; eksik giriş/çıkış ayrı alan değil, `anomalies` bayrak enum'unda (`missingCheckIn`, `missingCheckOut`). AttendanceDayDto: AttendanceDayFeatures.cs:18. |
| FG-004 | POST timesheet/days/{id}/resolve/worked-hours | TimesheetController.cs:80 | attendance.timesheet.resolve | ✔ İK/müdür. ✖ "attendance.corrections.approve Manager'da öngörülmüş" iddiası yanlış: SystemRoleDefinitions.cs:92-98'de yok (yalnız AttendancePermissions.cs:50 yorumu). ✖ `attendance.punchCorrection` request type adı geçersiz: ApprovalRequestTypes kuralı ASCII PascalCase (IOrgApprovalPolicyResolver.cs:12-35). |
| FG-005 | POST attendance/self-checkin/gps | SelfCheckInController.cs:30 | attendance.my.checkin | ✔ body {latitude, longitude, locationId?, direction}; locationId verilince geofence `SelfCheckInOutsideGeofence` (SelfCheckInFeatures.cs:189-206). Yanıt: attendanceDayId, date, direction, time, locationId?, locationName?, withinGeofence?. |
| FG-005 | GET self-checkin/status | SelfCheckInController.cs:36 | attendance.my.checkin | ✔ |
| FG-005 | Lokasyonlar | Organization.Application/Locations/LocationQueries.cs:17 | organization.locations.read | ✔ Çalışan rolünde yok. **Kartın atladığı mevcut yol:** `IOrgUnitLookup.GetEffectiveLocationIdAsync` (Organization.Contracts/IOrgUnitLookup.cs:29) + `IEmployeeLookup.GetOrgUnitIdAsync` + `ILocationLookup.GetGeofenceAsync` (ILocationLookup.cs:30). İSG modülü aynı zinciri kullanıyor (OhsProfileLookup.cs:18-20). |
| FG-007 | GET timesheet/team, team/month | TimesheetController.cs:56,62 | attendance.timesheet.view (TeamTimesheetFeatures.cs:24,45) | ✔ uç var, Çalışan rolünde izin yok ✔. TeamWeekRowDto(employeeId, employeeName, days[]). |
| FG-007 | GET leave/team/requests | LeaveTeamRequestsController.cs:27 | leave.requests.read (LeaveCalendarFeatures.cs:126) | ✔ |
| FG-007 | GET leave/calendar | LeaveCalendarController.cs:27 | leave.my.calendar + erişim ayarı CalendarShowMenu (LeaveCalendarFeatures.cs:27-28) | ✔ |
| FG-007 | GET attendance/board/live | LiveBoardController.cs:15 | attendance.board.view | ✔ Çalışan'da yok. |
| FG-007/012 | GET employees?managerEmployeeId= | EmployeesController.cs:63 | employee.employees.read (EmployeeQueries.cs:15) | ✔ Çalışan'da yok. Ekip kapsamı hesaplayıcı: `IEmployeeScopeLookup` (Employee.Infrastructure/Lookups/EmployeeScopeLookup.cs, Team = dolaylı dahil). |
| FG-008 | GET workflow/inbox, POST inbox/{taskId}/decide | Workflow.Api/Controllers/InboxController.cs:24,30 | workflow.inbox.view / .decide | ✔ Kanal sabit `ApprovalChannel.Web` (satır 35); enum'da Mobile yok, string olarak saklanıyor (WorkflowDbContext.cs:137,161) → yeni değer migration istemez. Red gerekçesi domain'de zorunlu (WorkflowDomainErrors.RejectionCommentRequired). Geçmiş için `ApprovalTask.DecidedByEmployeeId/DecidedAt/Comment` var (ApprovalTask.cs:38-42). |
| FG-009 | GET workflow/requests/{id}/status | RequestsController.cs:26 | handler içinde | ✖ Yalnız stepCount/currentStepNumber/currentApproverEmployeeId dönüyor (ApprovalRequestDtos.cs:9-17); adım adım onaylayıcı/karar/tarih **yok**. ✖ **Güvenlik bulgusu:** ApprovalRequestQueries.cs:37-41 `workflow.inbox.view` sahibine izin veriyor; bu izin her Çalışan'da var (SystemRoleDefinitions.cs:107), yani kimliği bilen herkes her talebin durumunu okuyabiliyor. |
| FG-009 | GET workflow/my-requests | MyRequestsController.cs:15 | workflow.my.requests | ✔ approvalRequestId ↔ subjectId eşlemesi için kullanılacak. |
| FG-009 | GET leave/me/requests/{id} | MyLeaveController.cs:61 | – | ✔ |
| FG-010 | GET employees/{id}/work-certificate | EmployeeDocumentsController.cs:16 | employee.employees.update (WorkCertificateQuery.cs:15) | ✔ (kart "İK yetkisi" diyor, gerçek anahtar employees.update). Belge teslim portu mevcut: `ISystemDocumentDeliveryService` (Documents.Contracts) ve `Document.EmployeeId/SourceModule/SourceRef` (Document.cs:89-106). |
| FG-011 | POST/GET visa-requests, GET {id}, GET {id}/documents, GET visa-processes | VisaDocumentRequestsController.cs:28,38,44,50,71 | documents.my.visa_requests | ✔ ✖ gövdede kartın atladığı zorunlu alanlar var: `countryCode`, `visaType` (+visaDocumentProcessId?, travelStart, travelEnd, description). |
| FG-012 | POST leave/employees/{id}/requests, GET .../requests/preview | LeaveEmployeeBalancesController.cs:61,71 | leave.requests.create_on_behalf | ✔ |
| FG-012 | GET .../balances | LeaveEmployeeBalancesController.cs:27 | ✖ **leave.balances.read** (LeaveBalanceFeatures.cs:82), create_on_behalf değil; HR'da ikisi de var. |
| FG-012 | POST leave/employees/{id}/overtimes | LeaveEmployeeOvertimeController.cs:32 | leave.overtimes.create_on_behalf | ✔ |
| FG-013 | POST documents/me/initiate-upload, me/{id}/confirm-upload | DocumentsController.cs:67,78 | documents.my.files | ✔ |
| FG-013 | GET documents/me/requirements, POST me/requirements/{id}/link | EmployeeDocumentRequirementsController.cs:28,59 | documents.my.requirements | ✔ |
| FG-013 | GET documents/shared-files, /{id} | SharedCompanyFilesController.cs:32,38 | documents.company.view | ✔ Çalışan'da var. "Belgelerim" listesi **yok** ✔. |
| FG-014 | GET employees/me | EmployeesController.cs:73 | employee.my.view | ✔ ✖ kendisi için NationalId **maskeli**, kan grubu/engellilik **null** döner (EmployeeDtos.cs:36 notu); "kan grubu gösterilir" kriteri karşılanamaz. |
| FG-014 | GET employees/me/career | EmployeesController.cs:115 | employee.my.view | ✔ CareerRecordDto(validFrom, validTo?, changeType, employmentType?, managerDisplayName?, assignments[], isCurrent). |
| FG-014 | PUT employees/me/profile | EmployeesController.cs:121 | employee.self.edit | ✔ alanlar: personalEmail, mobilePhone, homePhone, addressLine, city, postalCode, customFieldValues{defId:value}; boş gövde `ProfileUpdateEmpty`; yalnız `isEmployeeEditable` özel alanlar. |
| FG-014 | PUT/DELETE employees/me/photo | EmployeesController.cs:85,92 | employee.self.edit | ✔ multipart "file", ≤2 MB JPEG/PNG/WebP. |
| FG-015 | GET employees/me/bank-accounts, me/salaries | EmployeePaymentsController.cs:57,51 | employee.my.view | ✔ BankAccountDto(iban maskesiz, isIbanMasked, accountType, bankName?, currency?, isPrimary). ✖ SalaryRecordDto'da ayrı brüt/net yok: `amount` + `salaryType` + `currency` + `isCurrent` + yan ödemeler. |
| FG-016 | GET assets/my | AssetsController.cs:33 | assets.my.assets | ✔ ✖ AssetDto'da yalnız `categoryId` var (AssetDtos.cs:7-9); kategori listesi `assets.categories.view` ile korunuyor ve Çalışan'da yok. |
| FG-016 | GET assets/{id}/handover-form | AssetsController.cs:78 | assets.assets.view | ✔ Çalışan'da yok. Tutanak zaten çalışanın Documents klasörüne teslim ediliyor (IAssetHandoverFormLookup). |
| FG-017 | GET onboarding/me/tasks, POST me/tasks/{id}/complete | OnboardingController.cs:89,94 | onboarding.my.tasks | ✔ |
| FG-018 | GET/POST performance/goals/me, POST goals/{id}/progress/me | GoalsController.cs:25,49,61 | performance.my.goals | ✔ POST gövdesi {title, description?, targetDate, reviewCycleId?}. |
| FG-018 | GET review-cycles/me | ReviewCyclesController.cs:25 | performance.my.reviews | ✔ |
| FG-018 | GET goals/team | GoalsController.cs:31 | performance.team.goals | ✔ Yalnız Müdür rolünde. |
| FG-019 | GET training/enrollments/my | Training.Api/EnrollmentsController.cs:28 | training.my.enrollments | ✔ ✖ "zorunlu" alanı yok; kategori ve durumlar `ToString()` ile PascalCase dönüyor (MyTrainings.cs:41-43). |
| FG-020 | GET attendance/shifts/me | ShiftsController.cs:89 | attendance.shift.self | ✔ |
| FG-020 | GET attendance/work-schedules/me/assignments | WorkSchedulesController.cs:95 | attendance.my.workschedule | ✖ yalnız atama satırı (workScheduleId, ad, effectiveFrom/To, isCurrent) veriyor; **gün başına saat yok**, tablo detayı `attendance.workschedule.view` ister → yeni self uç gerekir. |
| FG-020 | GET leave/me/requests | MyLeaveController.cs:56 | – | ✔ |
| FG-020 | GET organization/holiday-calendars/upcoming?days=&amp;companyId= | HolidayCalendarsController.cs:39 | organization.holidays.view | ✔ Çalışan'da var. |
| FG-021 | GET employees/directory | EmployeesController.cs:369 | employee.directory.view | ✔ DirectoryEntryDto(id, displayName, orgUnitId?, workEmail?, workPhone? (erişim ayarıyla maskelenir), titleName?, departmentName?); yönetici alanı yok. |
| FG-021 | GET employees/org-chart | EmployeesController.cs:375 | employee.orgchart.view + erişim ayarı AccountShowOrgChart (EmployeeQueries.cs:287-289) | ✔ Ayar kapalıysa 403; OrgChartNodeDto ağacı (children, titleName, departmentName, companyName). |
| FG-021 | GET employees/{id}/photo | EmployeesController.cs:97 | employee.directory.view | ✔ |
| QA-006 | GET announcements/feed | AnnouncementsController.cs:39 | announcements.my.feed | ✔ bulgu doğru (AnnouncementFeed.cs:13-14,63,91-92). Web `enumKey()` küçük harfe çeviriyor (web/src/services/announcements.service.ts:21), `home.tsx:78` iki biçimi de kabul ediyor → camelCase'e geçiş web'i bozmaz. |

## 2. Kararlar

1. **FG-007 ekip yetki modeli.** Karar: yeni self anahtarlar `employee.my.team`, `attendance.my.team`, `leave.my.team`; kapsam, handler'da hesaplanan **doğrudan bağlılar** (`ManagerEmployeeId == çağıran`), DataScope/rol değişmez. Neden: SystemRoleDefinitions'a (Identity, riskli) dokunmadan Çalışan grubundaki yöneticiye açılır; bağlısı yoksa her uç boş liste döner, sızıntı yok. Yanlışsa maliyeti: dolaylı bağlılar istenirse çözümleyici `IEmployeeScopeLookup(DataScope.Team)` ile değiştirilir (uç başına tek satır).
2. **FG-005 çalışan↔lokasyon.** Karar: yeni model yok; lokasyon = çalışanın biriminin etkin lokasyonu (birim → üst birim zinciri, İSG ile aynı yol). Uç 0 ya da 1 lokasyon döner. Mobil yalnız yarıçaplı lokasyon varsa `locationId` gönderir; birimi lokasyonsuz (gezici) çalışanda davranış değişmez. Kiracı ayarı yok. Neden: migration yok, mevcut veriden çalışır. Yanlışsa maliyeti: ofis birimindeki uzaktan çalışan alan dışında reddedilir. İK bu çalışanı lokasyonsuz birime taşır ya da sonra "mobilde konum zorunlu" kiracı ayarı eklenir. Konum göndermeyen değiştirilmiş istemci sunucuda engellenmez (bugünkü davranış). Zorunlu kılmak ayrı iş.
3. **FG-005 biyometrik teyit.** Karar: cihazda biyometri kayıtlıysa giriş/çıkış/mola öncesi istenir; iptal edilirse punch gönderilmez. Kayıtlı değilse teyit istenmez; PIN'e düşülmez çünkü `biometric.ts` `disableDeviceFallback: true` kuralı korunuyor. Yanlışsa maliyeti: PIN'e düşme istenirse `authenticate` için yalnız punch'a özel bir seçenek eklenir.
4. **FG-010 İK yazısı belgesi.** Karar: sunucuda şablondan PDF üretilmez. Akış: çalışan talep eder → Workflow `HrLetter` onayı (varsayılan çözümleyici; kiracı birime İK politikası atayabilir) → İK web'de hazır belgeyi yükleyip "teslim et" der → belge çalışanın klasörüne bağlanır (`Document.EmployeeId`) ve bildirim gider. "Diğer" türü aynı akışla serbest metinli talep olur. Neden: maaş verisinin Documents modülüne akmasını ve bir şablon motorunu önler. Yanlışsa maliyeti: İK'ya elle iş düşer. Sonra "çalışma belgesi" için Employee'nin mevcut belge çizicisiyle otomatik üretim eklenebilir.
5. **FG-014 KVKK.** Karar: sunucunun kendisine döndüğü kişisel alanlar gösterilir (doğum tarihi, medeni hal, çocuk sayısı, eğitim, askerlik, adres, sözleşme/çalışma türü, hassas olmayan özel alanlar). T.C. kimlik no maskeli bile **gösterilmez**. Sağlık alanları sunucudan zaten null geldiği için görünmez. `ProfileService.ts` yorumu güncellenir. Yanlışsa maliyeti: tek satırla maskeli TC satırı eklenir.
6. **FG-015 yerleşim ve PII.** Karar: banka + maaş geçmişi Profil'de "Ödemeler" sekmesine konur, Bordro ekranına segment eklenmez (riskli `PayrollScreen.tsx`'e dokunulmaz). IBAN varsayılan maskeli; açmak için biyometri kayıtlıysa teyit istenir, değilse doğrudan açılır. Maaş satırı `amount` + `salaryType` etiketiyle gösterilir (ayrı brüt/net alanı yok). Sekme açıkken `expo-screen-capture` ile ekran görüntüsü/uygulama değiştirici koruması açılır. Yanlışsa maliyeti: Bordro ekranına taşımak bir segment işi.
7. **FG-022 MFA.** Karar: backend'de `DisableMfa` politika gereği reddeder ve `MfaStatusDto`'ya `requiredByPolicy` eklenir. Mobilde QR gösterilmez, çünkü kullanıcı zaten aynı telefonda. Bunun yerine "Doğrulayıcı uygulamada aç" (`otpauth://` bağlantısı) ve kopyalanabilir secret gösterilir. Secret loglanmaz, ekran görüntüsü engellenir. Mobilde MFA uçları yeni `services/SecurityService.ts` içine yazılır (riskli `AuthService.ts`'e dokunulmaz). Yanlışsa maliyeti: QR istenirse `react-native-qrcode-svg` bağımlılığı eklenir.
8. **FG-009 yorum ve güvenlik.** Karar: yorumlar yeni tablo yerine `ApprovalHistoryEntry` kaydına `EventType = Commented` olarak yazılır (string saklanıyor, migration gerekmez). Tepkiler kapsam dışı. Durum ve yorum erişimi daraltılır: talep eden, görev sahipleri/karar verenler, yedek rol görevinde rol üyesi ve `workflow.request.override` sahibi. `workflow.inbox.view` maddesi kaldırılır. Talep edene, politika gizliyorsa bekleyen adımın onaylayıcı adı gösterilmez. Yanlışsa maliyeti: yorum düzenleme/silme istenirse ayrı tablo gerekir. Erişimi kaybeden İK zaten `*` ile override iznine sahip.
9. **FG-008.** Karar: toplu karar en fazla 50 görev; her görev kendi transaction'ında, sonuç görev başına. Kanal `X-Client-App: mobile` ise `Mobile`. Geçmiş = çağıranın kendi verdiği kararlar. Yanlışsa maliyeti: sınır bir sabit.
10. **FG-004.** Karar: talep türü `PunchCorrection`, ApprovalRequestTypes.All'a eklenir (birime politika atanabilir). Onay Workflow gelen kutusundan, atama bazlı; yeni onay anahtarı açılmaz. Kilit = ayın ShiftPlanLock kaydı kilitli **veya** çalışanın o ay MonthlyTimesheetApproval kaydı var. Kilit gönderimde ve onayda ayrı ayrı kontrol edilir; onayda kilitliyse gün değişmez, talep `applyFailed` durumuna geçer. Gün başına tek açık talep. Gece vardiyası düzeltmesi kapsam dışı (checkOut &gt; checkIn şartı). Yanlışsa maliyeti: gece vardiyası için ayrı bir bayrak eklenir.
11. **FG-012.** Karar: yalnız `leave.requests.create_on_behalf` / `leave.overtimes.create_on_behalf` sahibine (İK/Owner) gösterilir; FG-007 ekip modeli çalışan adına talebe açılmaz. İzin kontrolü `useAuthStore` seçicisiyle `user.permissions` okunarak yapılır (store değişmez). Yanlışsa maliyeti: yöneticiye açmak backend'de ekip kapsamlı yeni bir komut ister.
12. **FG-016.** Karar: AssetDto'ya isteğe bağlı `categoryCode`/`categoryName` eklenir. Teslim tutanağı Belgelerim'de (FG-013) zaten görünür, ayrı self uç açılmaz.
13. **FG-017/018/019.** Karar: yalnız işe alışım görevleri; öz değerlendirme doldurma kapsam dışı; ekip hedefleri sekmesi yalnız `performance.team.goals` sahibine gösterilir. Eğitimde "Zorunlu" etiketi düşer (model yok), durum eşlemesi büyük/küçük harf duyarsız yapılır. Yanlışsa maliyeti: her biri ayrı backend kartı.
14. **FG-020.** Karar: yeni self uç `GET attendance/work-schedules/me/days`. Gösterim önceliği: izin &gt; resmi tatil &gt; vardiya &gt; çalışma programı. Başlık "Vardiyam" kalır.
15. **FG-024/025/026.** Karar: aile kaydında T.C. kimlik ve engellilik **tutulmaz** (KVKK minimizasyonu, bordro etkisi yok). Disiplin kaydında İK notu self DTO'da yer almaz; müdür erişimi yok (yalnız `employee.disciplinary.manage`). Sigorta kaydında SGK satırı gösterilmez. İK veri girişi web çalışan kartında yeni sekmelerle yapılır. Yanlışsa maliyeti: eklemeli (additive) migration.
16. **FG-027.** Karar: bileşenler hook tabanlı temaya taşınmaz (88 dosya statik `palette` kullanıyor). Görünüm tercihi açılışta `expo-secure-store` senkron `getItem` ile yeni `lib/bootPreferences.ts` üzerinden okunur, `theme.ts` paleti modül yüklenirken seçer. Değişince tercih yazılır ve `expo-updates` `reloadAsync()` ile uygulama yeniden başlar (RTL ile aynı deneyim). Varsayılan koyu. Yanlışsa maliyeti: canlı geçiş istenirse `useAppTheme()` ile tam refactor.
17. **FG-028.** Karar: yalnız yerel saklama (yeni `lib/homeLayoutStorage.ts`, AsyncStorage); cihazlar arası eşitleme yok.
18. **FG-001 dil kaynağı.** Karar: öncelik yerel seçim &gt; cihaz dili (tr/en değilse tr). Seçim değişince `PUT identity/me/language` en iyi çabayla çağrılır, böylece push/e-posta dili de uyar. Tercih AsyncStorage'da tutulur; mevcut `PreferenceStorage.setLanguage/getLanguage` kullanılır, `storage.ts` değişmez.
19. **FG-002.** Karar: Arapçada Latin rakamlar (`ar-u-nu-latn`); yazı tipi `@expo-google-fonts/ibm-plex-sans-arabic`; RTL için `I18nManager.forceRTL` + `reloadAsync`.
20. **FG-003.** Karar: `ar` hem SenseOptions varsayılanına hem `appsettings.json`'a eklenir. Renderer'da şablon yoksa kiracı varsayılanına düşme davranışı korunur. Bordro PDF'i Türkçe kalır.
21. **QA-006.** Karar: alanlar `string` kalır (Recognition bir AnnouncementType değeri değil), değerler `JsonNamingPolicy.CamelCase.ConvertName` ile üretilir, Kind sabitleri `announcement` / `recognition` olur. Yönetim DTO'su (AnnouncementDto) kapsam dışı.

## 3. Parçalar (sırayla)

### P01 [node] FG-001a — i18n altyapısı + tr/en + riskli olmayan ekranlar — riskli: hayır
- Dosyalar: `apps/senseik-mobile/package.json` (+i18next, react-i18next, expo-localization; web sürümleriyle hizalı), `pnpm-lock.yaml`, yeni `src/lib/i18n.ts`, `src/locales/{tr,en}/{common,home,checkin,attendance,leave,requests,approvals,notifications,people,profile,shift,goals,support,errors}.json`, `src/lib/apiClient.ts` (sabit `Accept-Language: tr` yerine aktif dil, istek interceptor'ında), `src/lib/formatters.ts` (locale parametresi / aktif dil; "dk önce", "sa dk" anahtarları), `src/App.tsx` (splash kapanmadan i18n init + kayıtlı dil), `src/__tests__/setup.ts` (testlerde `tr`), riskli olmayan tüm `screens/**`, `components/**`, `navigation/**`, `lib/*Labels.ts`, `lib/requestOptions.ts`; yeni `screens/app/profile/SettingsCard.tsx` (ayar satırları kaydı: Dil) ve `profile/LanguageSheet.tsx`.
- Kabul: FG-001 kart kriterleri, riskli dosyalar hariç. `tr-TR` sabiti `formatters.ts` dışında kalmaz. Ham metin testi geçer; P03'te boşaltılacak açık bir ALLOWLIST içerir (`screens/auth/**`, `PayrollScreen.tsx`, `AuthService.ts`, `hooks/useAuth.ts`, `components/auth/**`).
- Test: yeni `src/__tests__/i18n/locales.test.ts` (tr/en anahtar ve interpolasyon eşitliği, web `i18n.locales.test.ts` yaklaşımı), `src/__tests__/i18n/rawText.test.ts` (Türkçe karakterli string literal taraması + allowlist), `apiClient.test.ts` (Accept-Language aktif dil), `formatters.test.ts` (tr/en). Mevcut testler kaybolmaz.

### P02 [dotnet] Identity: FG-001 dil ucu + FG-022 MFA politikası — riskli: **evet** (src/Modules/Identity/**, tests Identity)
- Sözleşme:
  - `PUT /api/v1/identity/me/language` body `{ "language": "tr" }` → 204. Doğrulama: `SenseOptions.SupportedCultures` içindeki iki harfli dillerden biri olmalı (P36 sonrası `ar` kendiliğinden kabul edilir). Hata 400 `identity.user.language_unsupported`. [Authorize], izin anahtarı yok (MeController kalıbı). Domain: `User.ChangePreferredLanguage(string)`.
  - `GET /api/v1/identity/me/mfa` → `{ enabled, pendingEnrollment, requiredByPolicy }` (eklemeli; `TenantSecurityPolicy.RequireMfaForRoles ∩ kullanıcı rolleri`).
  - `POST /api/v1/identity/me/mfa/disable` → politika zorunluysa 400 `identity.mfa.required_by_policy`.
- Dosyalar: `Identity.Api/Controllers/AuthController.cs` (MeController), `Identity.Application/Auth/Mfa.cs`, MFA durum handler'ı, `Identity.Application/Dtos.cs`, yeni `Identity.Application/Users/ChangeMyLanguage.cs`, `Identity.Domain/Users/User.cs`, `SharedResource{,.en}.resx` (sona), `tests/Modules/Sense.Modules.Identity.Tests/**`.
- Kabul: dil değişince GET me yeni dili döner; desteklenmeyen dil 400; politika rolündeki kullanıcı MFA kapatamaz (olumsuz test); politikasız kullanıcı kapatabilir; web güvenlik sayfası bozulmaz.
- Test: domain + handler birim testleri; ilgili dosyada entegrasyon testi.

### P03 [node] FG-001b — riskli ekranların çevirisi + dil eşitleme — riskli: **evet** (screens/auth/**, PayrollScreen.tsx, AuthService.ts, hooks/useAuth.ts)
- Dosyalar: `screens/auth/**`, `components/auth/**`, `screens/app/PayrollScreen.tsx`, `services/AuthService.ts` (yalnız kullanıcıya gösterilen metinler), `hooks/useAuth.ts` (metin varsa), yeni `services/PreferencesService.ts` (`PUT identity/me/language`), `profile/LanguageSheet.tsx` (eşitleme çağrısı), rawText ALLOWLIST'inin boşaltılması, locale json'ları.
- Kabul: kodda sabit Türkçe UI metni kalmaz (allowlist boş); dil seçimi sunucuya gider, hata sessizce yutulur ama seçim yerelde korunur; giriş/MFA akışı davranışı değişmez.
- Test: rawText allowlist boş; `LoginScreen`/`AuthSecondaryScreens` testleri yeşil; PreferencesService servis testi.

### P04 [dotnet] QA-006 — duyuru akışı camelCase — riskli: hayır
- Dosyalar: `Announcements.Application/Announcements/AnnouncementFeed.cs`, `tests/Modules/Sense.Modules.Announcements.Tests/**`.
- Sözleşme: feed öğelerinde `type` ∈ {general, hr, policy, emergency, recognition}, `priority` ∈ {low, normal, high, urgent}, `kind` ∈ {announcement, recognition}.
- Kabul: QA-006 kriterleri; web `announcements.test.tsx`, `home.tsx` yeşil.
- Test: handler testi (camelCase değerler), web testleri kapıda.

### P05 [dotnet] FG-008 backend — onay geçmişi + toplu karar + Mobile kanalı — riskli: hayır
- Sözleşme:
  - `GET /api/v1/workflow/inbox/history?from=&amp;to=&amp;type=&amp;page=&amp;pageSize=` (workflow.inbox.view) → `PagedResult&lt;{ taskId, approvalRequestId, requestType, subjectId, requesterEmployeeId, requesterName?, stepNumber, outcome: "approved"|"rejected", decidedAt, comment?, requestStatus, summary? }&gt;`. Kaynak: `DecidedByEmployeeId == çağıran`, `decidedAt` azalan.
  - `POST /api/v1/workflow/inbox/decide-bulk` (workflow.inbox.decide) body `{ taskIds: Guid[1..50, tekil], outcome, comment? }` → 200 `{ results: [{ taskId, succeeded, errorCode?, errorMessage? }] }`. Red + boş yorum → 400 (üst seviye). Her görev ayrı `DecideApprovalTaskCommand`.
  - Yeni `ApprovalChannel.Mobile` (sona). `decide` ve `decide-bulk` çağrılarında `X-Client-App: mobile` gelirse Mobile, gelmezse Web.
  - Hatalar: `workflow.bulk.empty`, `workflow.bulk.too_many_tasks`.
- Dosyalar: `Workflow.Api/Controllers/InboxController.cs`, `Workflow.Application/ApprovalRequests/{ApprovalRequestQueries,ApprovalRequestCommands,ApprovalRequestDtos}.cs`, `Workflow.Domain/ApprovalRequests/ApprovalRequest.cs` (enum), `Workflow.Domain/Repositories.cs` + `Workflow.Infrastructure/Persistence/Repositories.cs` (`ListDecidedTasksAsync`), resx, `tests/Modules/Sense.Modules.Workflow.Tests/**` (proje adı depodaki ile).
- Kabul: kart FG-008 backend kriterleri; başkasının görevi toplu listede `AssigneeMismatch` ile başarısız, diğerleri başarılı (kısmi sonuç testi).
- Test: handler + entegrasyon (kısmi başarı, yetki, kanal).

### P06 [dotnet] FG-009 backend — adım zaman çizelgesi + yorumlar + bildirim + yetki düzeltmesi — riskli: **evet** (yetki davranışı değişiyor)
- Sözleşme:
  - `GET /api/v1/workflow/requests/{id}/status` eklemeli alanlar: `submittedAt`, `completedAt?`, `steps: [{ stepNumber, status: "pending"|"approved"|"rejected", approverEmployeeId?, approverName?, isFallbackRole, decidedAt?, comment? }]`. Politika gizliyorsa talep edene bekleyen adımda `approverEmployeeId`/`approverName` dönmez.
  - `GET /api/v1/workflow/requests/{id}/comments` → `[{ id, authorEmployeeId, authorName, text, createdAt }]`.
  - `POST /api/v1/workflow/requests/{id}/comments` body `{ text (1..WorkflowLimits.CommentMaxLength) }` → 200 `{ id }`; iptal edilmiş talepte 400 `workflow.comment.request_closed`.
  - Yetki (status + comments): talep eden, herhangi bir görevin approver/decidedBy'ı, yedek rol görevinde rol üyesi, `workflow.request.override` sahibi; diğerleri 404. `workflow.inbox.view` maddesi kaldırılır.
  - Olay: `Workflow.Contracts/Events` sonuna `ApprovalRequestCommented(TenantId, ApprovalRequestId, RequestType, SubjectId, RequesterEmployeeId, AuthorEmployeeId, RecipientEmployeeIds)`. Alıcılar: talep eden + tüm görev onaylayıcıları, yazar hariç.
  - Bildirim: şablon anahtarı `request.comment_added` (in_app tr/en/de), `ActionPath` = `/app/requests/{approvalRequestId}`.
- Dosyalar: `Workflow.Api/Controllers/RequestsController.cs`, Application ApprovalRequests dosyaları, `Workflow.Domain/ApprovalRequests/ApprovalRequest.cs` (`AddComment`, `ApprovalHistoryEventType.Commented` sona), `Workflow.Contracts/Events/WorkflowIntegrationEvents.cs` (sona), `Notifications.Application/Publishing/WorkflowEventHandlers.cs` (yeni handler sona), `NotificationTemplateKeys`, `Notifications.Infrastructure/Templates/request.comment_added.in_app.{tr,en,de}.sbn`, Notifications DI kaydı, gerekirse `Sense.Worker/Program.cs` (sona), resx, testler.
- Kabul: kart FG-009 backend kriterleri; taraf olmayan (inbox.view'lı sıradan çalışan) status/comments'te 404 (olumsuz test); yeni yorum karşı tarafa in-app bildirim.
- Test: domain (yorum), handler (yetki matrisi), Notifications şablon render testi, entegrasyon.

### P07 [node] FG-008 mobil — Geçmiş + toplu seçim — riskli: hayır
- Dosyalar: `services/ApprovalsService.ts` (getHistory, decideBulk), `screens/app/ApprovalsScreen.tsx` (başlıkta "Geçmiş" hapı, "Seç" modu, sabit cam eylem çubuğu), `approvals/ApprovalTaskCard.tsx` (onay kutusu), yeni `approvals/ApprovalHistoryScreen.tsx`, yeni `approvals/approvalSelection.ts` (saf seçim durumu), `types/navigation.ts` + `RootNavigator.tsx` (sona `ApprovalHistory`), locale'ler.
- Kabul: FG-008 mobil kriterleri; kısmi başarısızlıkta başarısız kartlar hata etiketiyle kalır; ana sayfa rozeti yenilenir.
- Test: `approvalSelection` saf testleri, servis testleri.

### P08 [node] FG-009 mobil — talep detayı + yorumlar + derin bağlantı — riskli: hayır
- Dosyalar: yeni `services/WorkflowService.ts` (myRequests, getStatus, comments), yeni `screens/app/requests/RequestDetailScreen.tsx` + `requests/requestTimeline.ts` (saf eşleme), `requests/RequestCardView.tsx` (dokununca detay; `my-requests` ile subjectId → approvalRequestId), `approvals/ApprovalTaskCard.tsx` (detaya git), `lib/notificationNavigation.ts` + `lib/pushNavigation.ts` (`/app/requests/{id}`), navigation kayıtları (sona), locale'ler. Web: bildirim tıklamasında bilinmeyen yol davranışı kontrol edilir; gerekiyorsa `/app/requests/*` web'de `/app/inbox`'a yönlenir (web bildirim bileşeninde tek eşleme).
- Kabul: FG-009 kart kriterleri.
- Test: `requestTimeline` ve derin bağlantı eşlemesi saf testleri, WorkflowService testi.

### P09 [dotnet] FG-004 backend — eksik giriş/çıkış düzeltme talebi — riskli: **evet** (Attendance migration, puantaj/bordro etkisi)
- Sözleşme:
  - Aggregate `PunchCorrectionRequest { id, tenantId, employeeId, attendanceDate, requestedCheckIn?, requestedCheckOut?, reason (1..500), status: pending|approved|applyFailed|rejected|withdrawn, approvalRequestId?, createdAt, decidedAt? }`; en az bir saat zorunlu, ikisi varsa checkOut &gt; checkIn.
  - Migration: `AddPunchCorrectionRequests` (Attendance şeması; `status='pending'` için (employee_id, attendance_date) kısmi tekil indeks).
  - `POST /api/v1/attendance/corrections/me` `{ date, checkIn?: "HH:mm:ss", checkOut?, reason }` → PunchCorrectionRequestDto.
  - `GET /api/v1/attendance/corrections/me?from=&amp;to=` → `[{ id, date, requestedCheckIn?, requestedCheckOut?, currentCheckIn?, currentCheckOut?, reason, status, approvalRequestId?, submittedAt, decidedAt? }]`.
  - `POST /api/v1/attendance/corrections/me/{id}/withdraw` → 204 (yalnız pending; `IWorkflowService.CancelAsync`; başkasınınki 404).
  - İzin: `attendance.my.corrections` (AttendancePermissions sonuna + All).
  - Hatalar: `attendance.correction.future_date`, `.period_locked`, `.pending_exists`, `.times_required`, `.invalid_range`.
  - Workflow: `ApprovalRequestTypes.PunchCorrection = "PunchCorrection"` (+All, sona). `ApprovalCompleted` → `AttendanceDayUpsert` ile uygula (verilmeyen saat korunur, Source mevcut Manual değeri), yeniden hesapla, `TimesheetWeekEvaluator`. `ApprovalRejected` → rejected. Onayda kilit → applyFailed.
  - Özet: `Workflow.Contracts` `ApprovalRequestSummary`'e isteğe bağlı son parametre `PunchCorrection: PunchCorrectionApprovalSummary(Date, RequestedCheckIn?, RequestedCheckOut?, CurrentCheckIn?, CurrentCheckOut?, Reason)`; Attendance.Infrastructure provider.
  - Bildirim etiketi: `labels.{tr,en,de}.json` → `"PunchCorrection"` (sona).
- Dosyalar: Attendance Domain/Application/Infrastructure/Api (yeni `Corrections/` klasörleri, `CorrectionsController.cs`), `AttendancePermissions.cs`, `Attendance.Application.csproj` + Infrastructure csproj (Workflow.Contracts referansı), `AddAttendanceIntegrationEventHandlers` kaydı, `Organization.Contracts/IOrgApprovalPolicyResolver.cs`, `Workflow.Contracts/IApprovalRequestSummaryProvider.cs`, labels, resx, `Persistence/Migrations/**`, testler.
- Kabul: FG-004 backend kriterleri; onay → gün saatleri güncellenir ve anomaliler temizlenir (entegrasyon).
- Test: domain, handler (gelecek tarih, kilit, mükerrer, başkasının talebini geri çekme 404), entegrasyon (onay → gün), migration uygulanır.

### P10 [node] FG-004 mobil — riskli: **evet** (Attendance tarafı)
- Dosyalar: `services/AttendanceService.ts` (corrections), `attendance/AttendanceDayCard.tsx` ("Düzeltme iste"), `AttendanceScreen.tsx`, `types/navigation.ts` (`RequestKind` + "punchCorrection", sona; `NewRequest` parametresine isteğe bağlı `date`), `NewRequestScreen.tsx` (switch sona), yeni `components/requests/PunchCorrectionSection.tsx`, `lib/requestOptions.ts`, `requests/requestCards.ts` (+filtre, kart, geri çek), `approvals/approvalLabels.ts` + `approvalPresentation.ts` (özet), locale'ler; web `web/public/locales/{tr,en,de}/&lt;workflow namespace&gt;.json` tür etiketi (sona).
- Kabul: FG-004 mobil kriterleri (gün ve planlanan saat önceden dolu).
- Test: kart/etiket/doğrulama saf testleri, servis testi.

### P11 [dotnet] FG-005 + FG-020 backend — Attendance self uçları — riskli: **evet** (Attendance)
- Sözleşme:
  - `GET /api/v1/attendance/self-checkin/locations` (attendance.my.checkin) → `[{ locationId, name, latitude?, longitude?, radiusMeters? }]` (0 veya 1 öğe; Karar 2 zinciri).
  - `GET /api/v1/attendance/work-schedules/me/days?from=&amp;to=` (attendance.my.workschedule, en fazla 42 gün) → `[{ date, isWorkingDay, start?, end?, breakMinutes }]` (`IWorkScheduleReadService.GetForDateAsync`). Hata `attendance.work_schedule.range_too_long`.
- Dosyalar: `SelfCheckInController.cs`, `SelfCheckIns/` yeni query, `WorkSchedulesController.cs`, `WorkSchedules/` yeni query, resx, testler. Migration yok.
- Kabul: birimi/lokasyonu olmayan → []; yarıçapsız lokasyon null radius ile döner; aralık sınırı.
- Test: handler birim testleri + entegrasyon.

### P12 [node] FG-005 mobil — geofence + biyometrik teyit — riskli: **evet** (Attendance, biyometri)
- Dosyalar: `services/AttendanceService.ts` (getCheckInLocations), `CheckInScreen.tsx`, `checkin/MapHeader.tsx` (Circle), `checkin/ClockCard.tsx` (Ofis alanında/Alan dışında StatusPill), yeni `checkin/geofence.ts` (mesafe/içeride saf fonksiyonu), `lib/punch.ts`, `lib/biometric.ts` (punch teyidi; `disableDeviceFallback` korunur), locale'ler.
- Kabul: FG-005 kriterleri, Karar 2-3'e göre.
- Test: geofence/mesafe, biyometri iptali → punch yok, locationId gönderim kuralı.

### P13 [node] FG-020 mobil — takvimim — riskli: hayır
- Dosyalar: `services/ShiftService.ts` (getMyScheduleDays, onaylı izinler, tatiller), `ShiftScreen.tsx`, `shift/WeekStrip.tsx`, `shift/ShiftCard.tsx`, `shift/shiftWeek.ts` (saf birleştirme), locale'ler.
- Kabul: FG-020 kriterleri, Karar 14 önceliğiyle.
- Test: `shiftWeek` birleştirme (vardiya/program/izin/tatil çakışmaları).

### P14 [dotnet] FG-007 backend — ekip uçları — riskli: **evet** (yetki modeli, Attendance)
- Sözleşme (hepsinde kapsam = çağıranın doğrudan bağlıları; bağlı yoksa []):
  - `Employee.Contracts`: `IEmployeeLookup.GetDirectReportIdsAsync(tenantId, managerEmployeeId)` (sona).
  - `GET /api/v1/employees/me/team` (employee.my.team) → `[{ employeeId, displayName, titleName?, departmentName?, workEmail? }]`.
  - `GET /api/v1/attendance/timesheet/me/team?weekStart=` ve `.../me/team/month?monthStart=` (attendance.my.team) → `TeamWeekRowDto[]`.
  - `GET /api/v1/attendance/me/team/today` (attendance.my.team) → `[{ employeeId, employeeName, status: "checkedIn"|"checkedOut"|"onLeave"|"notCheckedIn", checkIn?, checkOut? }]`; izin türü açılmaz ("uzaktan" durumu yok, veri modeli yok).
  - `GET /api/v1/leave/me/team/requests?from=&amp;to=&amp;status=` (leave.my.team) → `LeaveCalendarEntryDto[]` (takvimdeki hassas tür maskelemesiyle aynı).
- Dosyalar: Employee (Contracts, Application yeni query, Infrastructure lookup, EmployeesController, EmployeePermissions), Attendance (TimesheetController, yeni `MyTeamFeatures.cs`, AttendancePermissions), Leave (yeni controller/query, LeavePermissions), resx, testler.
- Kabul: FG-007 backend kriterleri; başka müdürün bağlısı hiçbir uçta görünmez (olumsuz test); `SystemRolePermissionResolver` yeni anahtarları Çalışan rolüne verir (test).
- Test: handler + entegrasyon yetki matrisi.

### P15 [node] FG-007 mobil — Ekibim — riskli: **evet** (yetki)
- Dosyalar: yeni `services/TeamService.ts`, yeni `screens/app/team/TeamScreen.tsx` + `team/teamStatus.ts`, `AttendanceScreen.tsx` (Benim/Ekibim segmenti, yalnız ekip varsa), `home/ActionGrid.tsx` (Ekibim kutucuğu sona, koşullu), navigation kayıtları, locale'ler.
- Kabul: FG-007 mobil kriterleri; bekleyen ekip izinleri Onaylarım'a yönlendirir.
- Test: durum eşleme/sıralama saf testleri, servis testi.

### P16 [dotnet] FG-013 + FG-016 backend — Belgelerim + zimmet kategori — riskli: **evet** (PII belgeler)
- Sözleşme:
  - `GET /api/v1/documents/me?page=&amp;pageSize=` (documents.my.files) → `PagedResult&lt;{ id, title, contentType, size, uploadedAt, expiryDate?, documentTypeName?, source: "requirement"|"delivered" }&gt;`; Active belgeler: `Document.EmployeeId == çağıran` VEYA çağıranın EmployeeDocumentRequirement.DocumentId'si.
  - `GET /api/v1/documents/me/{id}/download-url` → `{ downloadUrl, expiresAt }` (15 dk); sahip değilse 404.
  - AssetDto sonuna isteğe bağlı `categoryCode?`, `categoryName?` (AssetMapper).
- Dosyalar: `DocumentsController.cs`, `Documents.Application/Documents/{DocumentQueries,DocumentDtos}.cs`, `Documents.Domain/Repositories.cs` + `DocumentRepository.cs`, `Assets.Application/Assets/AssetDtos.cs` + mapper, testler. Migration yok.
- Kabul: başkasının belgesi 404; silinmiş belge listelenmez; gereksinime bağlı belge listelenir; AssetDto kategori taşır.
- Test: handler + entegrasyon olumsuz yetki.

### P17 [node] FG-013 mobil — Belgelerim ekranı — riskli: **evet** (PII)
- Dosyalar: `services/DocumentService.ts` (list, downloadUrl, requirements, sharedFiles, link), yeni `screens/app/documents/DocumentsScreen.tsx` + `documents/documentRows.ts`, `lib/attachmentDownload.ts` (yeniden kullanım; imzalı URL loglanmaz), `home/ActionGrid.tsx` (Belgelerim sona), `profile/ProfileMenuCard.tsx` (sona), navigation kayıtları, locale'ler.
- Kabul: FG-013 mobil kriterleri.
- Test: satır eşleme ("Süresi doluyor" eşiği), boyut kontrolü, servis testi.

### P18 [node] FG-011 mobil — vize belgesi — riskli: hayır
- Dosyalar: yeni `services/VisaService.ts`, yeni `components/requests/VisaRequestSection.tsx`, `NewRequestScreen.tsx` / `types/navigation.ts` / `lib/requestOptions.ts` (aktif süreç yoksa gizli) / `requests/requestCards.ts` (sona), locale'ler. Alanlar web `web/src/pages/visa-requests.tsx` ile aynı: countryCode, visaType, süreç, tarihler, açıklama.
- Kabul: FG-011 kriterleri.
- Test: eşleme/doğrulama saf testleri, servis testi.

### P19 [dotnet] FG-010 backend — İK yazısı talebi — riskli: **evet** (Documents migration, maaş içerikli belge)
- Sözleşme:
  - Aggregate `HrLetterRequest { id, tenantId, employeeId, type: employmentCertificate|salaryCertificate|other, purpose (1..500), status: pending|approved|completed|rejected, approvalRequestId?, documentId?, createdAt, completedAt? }`. Migration: `AddHrLetterRequests`.
  - Self (documents.my.letters): `POST /api/v1/documents/letters/me { type, purpose }` → HrLetterRequestDto; `GET /api/v1/documents/letters/me` → liste; `GET /api/v1/documents/letters/me/{id}/download-url` (yalnız completed) → `{ downloadUrl, expiresAt }`.
  - İK (documents.letters.manage): `GET /api/v1/documents/letters?status=&amp;page=` → sayfalı + employeeName; `POST /api/v1/documents/letters/{id}/fulfill { documentId }` (yalnız approved; belge Active) → completed. Domain `Document.AssignToEmployee(employeeId, "HrLetter", letterId)` (mevcut kolonlar, migration yok). Olay `HrLetterDelivered` → şablon `documents.letter_ready` in_app tr/en/de.
  - Workflow: `ApprovalRequestTypes.HrLetter = "HrLetter"` (+All, sona); onay/red handler'ları; özet `LetterApprovalSummary(Type, Purpose)` ApprovalRequestSummary sonuna; labels `"HrLetter"`.
  - Hatalar: `documents.letter.purpose_required`, `.not_approved`, `.document_invalid`.
- Dosyalar: Documents Domain/Application/Infrastructure/Api (yeni `HrLetters/`), `DocumentsPermissions.cs`, `DocumentsServiceCollectionExtensions.cs`, `Sense.Worker/Program.cs` (sona, gerekiyorsa), Organization/Workflow Contracts (sona), Notifications şablon + labels, resx, `Persistence/Migrations/**`, testler.
- Kabul: FG-010 backend kriterleri; başkasının yazısını indirme 404; İK olmayan fulfill 403.
- Test: domain, handler, entegrasyon (talep → onay → fulfill → indir), migration.

### P20 [node] FG-010 mobil — riskli: **evet** (maaş yazısı)
- Dosyalar: `services/DocumentService.ts` (letters), yeni `components/requests/HrLetterSection.tsx`, `NewRequestScreen.tsx` / `types/navigation.ts` / `requestOptions.ts` / `requestCards.ts` / `approvalLabels.ts` (sona), `documents/DocumentsScreen.tsx` ("Yazılarım" segmenti), locale'ler; web tür etiketi (sona).
- Kabul: FG-010 mobil kriterleri; red gerekçesi FG-009 detayında görünür.
- Test: kart/etiket saf testleri, servis testi.

### P21 [node] FG-010 web — İK yazı talepleri sayfası — riskli: hayır
- Dosyalar: yeni `web/src/pages/documents/hr-letters.tsx`, `web/src/services/hr-letters.service.ts`, rota ve menü kaydı (`web/src/App.tsx`, `layouts/sidebar-nav.tsx`, sona; izin `documents.letters.manage`), `web/public/locales/{tr,en,de}/documents.json` (sona), sayfa testi.
- Kabul: İK onaylı talepleri listeler, dosya yükleyip teslim eder, durum güncellenir.
- Test: vitest sayfa/servis testi; kapı yeşil.

### P22 [node] FG-014 — profil sekmeleri + düzenleme + fotoğraf — riskli: **evet** (KVKK/PII)
- Dosyalar: `services/ProfileService.ts` (MyProfile genişletme, getMyCareer, updateMyProfile yalnız değişen alanlar, setMyPhoto/deleteMyPhoto), `profile/profileGroups.ts` → sekme kaydı (`profileTabs.ts`; sonraki parçalar sekmeyi sona ekler), `ProfileScreen.tsx`, yeni `profile/EditContactSheet.tsx`, yeni `profile/CareerTimeline.tsx`, locale'ler (enum etiketleri).
- Kabul: FG-014 kriterleri, Karar 5'e göre (TC gösterilmez, sağlık alanları yok).
- Test: `profileGroups`/sekme saf testleri, düzenleme gövdesi (yalnız değişen alanlar), servis testi.

### P23 [node] FG-015 — Ödemeler sekmesi — riskli: **evet** (para/banka PII)
- Dosyalar: `services/ProfileService.ts` (getMyBankAccounts, getMySalaries), yeni `profile/PaymentsTab.tsx` + `profile/ibanMask.ts`, sekme kaydı (sona), `package.json` (+expo-screen-capture), locale'ler.
- Kabul: FG-015 kriterleri, Karar 6'ya göre; IBAN/tutar hiçbir log çağrısında yer almaz.
- Test: maskeleme, sıralama/"Güncel", 404 → boş durum.

### P24 [node] FG-016 + FG-017 + FG-019 — salt okuma listeleri — riskli: hayır
- Dosyalar: yeni `services/AssetsService.ts`, `services/TasksService.ts`, `services/TrainingService.ts`; profil sekmeleri "Zimmet", "Eğitimler" (kayıt sona); yeni `screens/app/tasks/TasksScreen.tsx` + `tasks/taskSort.ts`; `home/ActionGrid.tsx` (Görevlerim + bekleyen rozeti, sona); navigation kayıtları; locale'ler.
- Kabul: FG-016/017/019 kriterleri (FG-019'da "Zorunlu" etiketi yok, Karar 13).
- Test: eşleme/sıralama/rozet saf testleri, servis testleri.

### P25 [node] FG-018 — performans — riskli: hayır
- Dosyalar: `services/GoalsService.ts` (createMyGoal, getMyReviewCycles, getTeamGoals), `GoalsScreen.tsx` (segmentler), yeni `goals/NewGoalSheet.tsx`, locale'ler.
- Kabul: FG-018 kriterleri; Ekip segmenti yalnız `performance.team.goals` sahibine.
- Test: form doğrulama, dönem durumu eşleme, servis testi.

### P26 [node] FG-021 — kişi detayı + organizasyon şeması — riskli: hayır
- Dosyalar: `services/PeopleService.ts` (getOrgChart, photo URL + Authorization başlığı), yeni `screens/app/people/PersonDetailScreen.tsx`, `people/OrgChartScreen.tsx`, `people/orgTree.ts`, `PeopleScreen.tsx`, navigation kayıtları, locale'ler.
- Kabul: FG-021 kriterleri; şema 403 dönerse (erişim ayarı kapalı) yönetici/bağlı gezintisi ve "Şema" eylemi gizlenir.
- Test: `orgTree` (kendi düğümünü bulma, üst/alt), servis testi.

### P27 [node] FG-012 — çalışan adına talep — riskli: hayır
- Dosyalar: `services/LeaveService.ts` (on-behalf uçları, çalışan arama `GET employees?q=`), yeni `components/requests/OnBehalfPicker.tsx`, `components/leave/LeaveRequestForm.tsx` + `components/requests/OvertimeRequestSection.tsx` ("Kimin için"), yeni `lib/usePermission.ts` (`useAuthStore` seçicisi; store değişmez), locale'ler.
- Kabul: FG-012 kriterleri, Karar 11'e göre. Seçilen çalışanın izin türleri/bakiyesi web İK formundaki kaynaktan gelir (geliştirici web'i örnek alır).
- Test: izin koşulu, gövde eşleme, servis testi.

### P28 [node] FG-022 mobil — güvenlik ayarları — riskli: **evet** (auth, secure storage)
- Dosyalar: yeni `services/SecurityService.ts` (me/mfa uçları; `AuthService.ts`'e dokunulmaz), yeni `screens/app/settings/SecuritySettingsScreen.tsx` + `settings/mfaEnrollment.ts`, `lib/biometric.ts` (aç/kapa), `profile/SettingsCard.tsx` (satır sona), navigation kayıtları, locale'ler; ekran görüntüsü koruması `expo-screen-capture`.
- Kabul: FG-022 kriterleri, Karar 7'ye göre (QR yok; `requiredByPolicy` ise kapatma düğmesi pasif + açıklama).
- Test: kayıt adımı durum makinesi, hatalı kod mesajı, biyometri kapatma → bayrak temizlenir; secret hiçbir log çağrısında yok.

### P29 [dotnet] FG-024 backend — aile bilgileri — riskli: **evet** (Employee migration, 3. kişi verisi)
- Sözleşme: `FamilyMember { id, employeeId, relationship: spouse|child|mother|father|sibling|other, firstName, lastName, birthDate?, isDependent }` (TC ve engellilik yok). Migration `AddEmployeeFamilyMembers`. `GET /api/v1/employees/me/family` (employee.my.family). İK: `GET/POST /api/v1/employees/{id}/family`, `PUT/DELETE .../family/{memberId}` (employee.family.manage, İK uçlarının kapsam kontrolüyle).
- Dosyalar: Employee Domain/Application/Infrastructure/Api, `EmployeePermissions.cs`, resx, `Persistence/Migrations/**`, testler.
- Kabul: FG-024 backend kriterleri; başkasının verisi 404.
- Test: domain, handler, entegrasyon, migration.

### P30 [dotnet] FG-025 backend — disiplin kayıtları — riskli: **evet**
- Sözleşme: `DisciplinaryAction { id, employeeId, actionDate, type: warning|reprimand|finalWarning, reason (≤1000), defenseRequested, defenseDueDate?, defenseDocumentId?, hrNote (≤2000; self DTO'da yok), createdAt }`. Migration `AddEmployeeDisciplinaryActions`.
  - `GET /api/v1/employees/me/disciplinary-actions` (employee.my.disciplinary).
  - `POST /api/v1/employees/me/disciplinary-actions/{id}/defense { documentId }`: yalnız defenseRequested; belge Active ve `UploadedBy == çağıran` (`IDocumentAttachmentLookup`).
  - İK CRUD `/api/v1/employees/{id}/disciplinary-actions` (employee.disciplinary.manage).
- Kabul: FG-025 kriterleri; İK notu self yanıtta yok (test); müdür erişemez (olumsuz test).
- Test: domain, handler, entegrasyon, migration.

### P31 [dotnet] FG-026 backend — yan hak sigortaları — riskli: **evet** (Employee migration)
- Sözleşme: `BenefitInsurancePolicy { id, employeeId, kind: health|life|other, insurerName, policyNumber?, coverageSummary?, startDate, endDate?, coveredFamilyMemberIds[], policyDocumentId? }`. Migration `AddEmployeeBenefitInsurances`. `GET /api/v1/employees/me/insurances` + `GET .../me/insurances/{id}/document-url` (employee.my.insurance; `IDocumentAttachmentLookup.GetDownloadUrlAsync`). İK CRUD (employee.insurance.manage).
- Kabul: FG-026 kriterleri; kapsanan kişi FamilyMember'a bağlı; SGK satırı yok.
- Test: domain, handler, entegrasyon, migration.

### P32 [node] FG-024/025/026 web — çalışan kartı İK sekmeleri — riskli: **evet** (PII)
- Dosyalar: web çalışan kartına "Aile", "Disiplin", "Sigorta" sekmeleri (mevcut çalışan kartı sekme yapısına sona), servisler, `web/public/locales/{tr,en,de}/employees.json` (sona), testler.
- Kabul: İK ekler/düzenler/siler; izni olmayan sekmeyi görmez.
- Test: vitest; kapı yeşil.

### P33 [node] FG-024/025/026 mobil — profil sekmeleri — riskli: **evet** (PII)
- Dosyalar: `services/ProfileService.ts` (family, disciplinary, insurances, defense), profil sekmeleri "Aile", "Disiplin", "Sigorta" (kayıt sona; kayıt yoksa gizli), savunma yükleme (`useLeaveDocuments` yükleme yolu yeniden kullanılır), locale'ler.
- Kabul: FG-024/025/026 mobil kriterleri.
- Test: eşleme/gizleme saf testleri, servis testi.

### P34 [node] FG-028 — ana sayfa düzenleme — riskli: hayır
- Dosyalar: yeni `lib/homeLayoutStorage.ts`, yeni `home/ArrangeSheet.tsx`, yeni `home/homeLayout.ts` (saf sıralama/birleştirme), `home/ActionGrid.tsx`, `home/HomeMenu.tsx` ("Düzenle"), `DashboardScreen.tsx`, `home/homeSections.ts`, locale'ler.
- Kabul: FG-028 kriterleri (yeni kutucuk sona düşer; hareket azaltma açıkken yukarı/aşağı düğmeleri).
- Test: `homeLayout` saf testleri.

### P35 [node] FG-027 — açık tema — riskli: hayır
- Dosyalar: yeni `lib/bootPreferences.ts` (senkron `SecureStore.getItem`), `theme/theme.ts` (açık token seti + açılışta seçim), `navigation/statusBarStyle.ts`, `app.json` (`userInterfaceStyle: automatic`), `profile/SettingsCard.tsx` ("Görünüm" satırı sona) + yeni `profile/AppearanceSheet.tsx`, sabit renk kullanan bileşenlerin token'a çekilmesi, locale'ler.
- Kabul: FG-027 kriterleri, Karar 16'ya göre (değişiklik yeniden başlatır, varsayılan koyu).
- Test: yeni `__tests__/theme/contrast.test.ts` (iki token setinde metin/zemin WCAG AA), bootPreferences testi.

### P36 [dotnet] FG-003 — backend Arapça — riskli: hayır (çeviri insan gözden geçirmesi şart)
- Dosyalar: `Shared.Contracts/Configuration/SenseOptions.cs` (`Cultures.Arabic`, varsayılan liste), `src/Sense.Api/appsettings.json` (`SupportedCultures` + `ar`; varsa diğer appsettings), yeni `SharedResource.ar.resx` (tr ile aynı anahtar kümesi, P02-P31'de eklenenler dahil), `ScribanNotificationTemplateRenderer.cs:41` (+ar), tr'de olan tüm şablonların `*.ar.sbn` karşılıkları + `labels.ar.json`, `Announcements.Application/Support/HelpArticleCatalog.cs` (ar dalı), `tests/Modules/Sense.Shared.Infrastructure.Tests/ErrorResourceParityTests.cs` (ar), şablon eşitliği testi.
- Kabul: FG-003 kriterleri (fallback kiracı varsayılanı; bordro PDF tr kalır, belgelenir).
- Test: resx ve şablon eşitliği testleri, `Accept-Language: ar` entegrasyon testi, `fr` → tr.

### P37 [node] FG-002 — Arapça + RTL — riskli: hayır
- Dosyalar: `src/locales/ar/*.json` (tüm namespace'ler), `lib/i18n.ts` (ar + `I18nManager.forceRTL` + `reloadAsync` + onay diyaloğu), `lib/bootPreferences.ts`, `theme/theme.ts` (`fontFor`: ar için IBM Plex Sans Arabic; Arapça başlıkta negatif tracking kapalı), `package.json` (+@expo-google-fonts/ibm-plex-sans-arabic), `navigation/NotchedTabBar.tsx` (SVG oyuk + sekme sırası aynalama), yön bildiren ikonlar ve `marginLeft/Right` → `Start/End` taraması (`screens/**`, `components/**`), `checkin/PunchTiles.tsx`, `home/ActionGrid.tsx`, `home/BalanceCarousel.tsx`, `formatters.ts` (`ar-u-nu-latn`), `profile/LanguageSheet.tsx` (العربية).
- Kabul: FG-002 kriterleri (Android emülatör ekran görüntüleri rapora eklenir).
- Test: `locales.test.ts` hedeflerine ar, formatters ar (Latin rakam, ₺ konumu), NotchedTabBar RTL render testi.

## 4. Paralel çalışabilecekler
Yok (tek çalışma ağacı, sıralı yürütme). Sıra bağımlılıkları: P02 → P03, P28 · P05 → P07 · P06 → P08 · P09 → P10 · P11 → P12, P13 · P14 → P15 · P16 → P17, P24 · P17 + P19 → P20, P21 · P22 → P23, P24, P33 · P29-P31 → P32, P33 · P36 → P37.

## 5. Sorular
1. **Mobil test kapısı:** `takim.json` kapısı mobili (tsc/eslint/jest) koşturmuyor. İnsan `apps/senseik-mobile` komutlarını kapıya ekleyecek mi? Eklenene kadar her node parçası üç komutu elle koşturup çıktıyı rapora koyar.
2. **Migration üretimi:** `takim-riskli-alan.md` "üretimi insan yapar" diyor, proje CLAUDE.md'si `dotnet-ef` ile üretmeyi söylüyor. Plan varsayımı: geliştirici `dotnet-ef` ile üretir, elle düzenlemez, insan onaylar (P09, P19, P29, P30, P31). Doğru mu?
3. **FG-003 çeviri kalitesi:** yaklaşık 1436 resx anahtarının ve şablonların Arapçasını kim gözden geçirecek (ana dili Arapça olan biri)?
4. **FG-024/025 KVKK:** aile (3. kişi) ve disiplin verisi için aydınlatma metni ve hukuk onayı kimde? Teknik iş bekletilmeden yapılır, birleştirme bu onaya bağlanabilir.
5. **FG-027:** kullanıcının önceki "tek koyu tema" kararıyla çelişiyor; varsayılan koyu kalıyor ve geri alınabilir. Kullanıcıya bilgi verilmeli.
6. **FG-009 güvenlik düzeltmesi (P06):** `workflow.inbox.view` ile durum okuma yolunun kapanması web'de İK'nın gelen kutusu dışı bir ekranını etkiliyor mu? P06'da web kullanımları taranacak.
7. **Dal tabanı:** FG-001 "yeniden tasarım birleşmiş olmalı" diyor. `a/mobil-ozellikler` PR #134 üstüne yığılı; PR #134 birleşince bu dal `main` üzerine rebase edilecek.

## 6. Kontrolcü kararları (sorulara)

1. Mobil kapı: `.claude/takim/**` elle değiştirilmez; her node parçasında geliştirici **ve** gözden geçirici mobil `tsc --noEmit`, `eslint src`, `jest` koşturur (host'ta `PATH="/c/Program Files/nodejs:$PATH" pnpm --dir apps/senseik-mobile exec …` çalışır). Kapıya ekleme insan kararı olarak PR'da not edilir.
2. Migration: geliştirici `dotnet-ef` ile SDK konteynerinde üretir, elle düzenlemez; parça riskli işaretlenir, onay PR'da insanda.
3. FG-003 Arapça çeviri makine üretimidir; birleştirme öncesi ana dili Arapça bir kişinin gözden geçirmesi PR'da zorunlu not.
4. FG-024/025 teknik olarak uygulanır; aydınlatma metni + hukuk onayı birleştirme koşulu olarak PR'da işaretlenir.
5. FG-027 uygulanır, varsayılan koyu tema korunur; kullanıcıya raporda bildirilir.
6. P06'da web'in `workflow/requests/{id}/status` kullanımları taranır; İK ekranı etkileniyorsa override izni yolu yeterli olmalı, değilse parça raporunda belirtilir.
7. Dal yığılı kalır; PR tabanı `a/mobil-yeniden-tasarim`, #134 birleşince `dev`'e yeniden hedeflenir.
