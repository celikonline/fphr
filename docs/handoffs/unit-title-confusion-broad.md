# senseik-046 — "Birim" sütunu departman yerine ünvan gösteriyor (geniş kapsam) — UYGULAMA NOTU

**Durum: Uygulandı ve doğrulandı (GATE 1/GATE 2 onaylı). `a/unit-title-confusion-broad` dalında commit edildi; push yapılmadı. Build/test/tsc/eslint yeşil (ayrıntı: Test Planı ve Sonuç bölümü).**

## Kök Neden

`Employee.OrgUnitId` her zaman org yerleşim zincirinin **en derin** atamasını tutar — çoğu tenant'ta bu "Ünvan" seviyesidir (`Şirket > Şube > Departman > Ünvan`). Ekranlarda "Birim" başlıklı her yer bu ham `OrgUnitId`'yi doğrudan bir isimle eşleyip gösterdiğinde, kullanıcı aslında **ünvanı** "birim" sanıyor:

- Çalışanlar listesi (`employee-list.tsx`) — "Birim" sütunu `unitNames.get(orgUnitId)` (ham, en derin atama) gösteriyordu.
- Çalışan rehberi (`employee-directory.tsx`, Bordro/Puantör rolünün de gördüğü `employee.directory.view` ekranı) — aynı ham `nameById` lookup'ı; bu lookup `organization.units.read` yetkisi gerektiren `GET /v1/organization/units`'e dayanıyor, o yetkisi olmayan roller için harita boş kalıyor → sütun her zaman "—".
- Raporlar → "Birim Bazında Cinsiyet Dağılımı" (`GENDER_RATIO_BY_UNIT`) — backend `GetGenderByOrgUnitAsync` da aynı ham `OrgUnitId`'ye göre grupluyor; kırılım metni "İK Uzmanı / Kadın" gibi bir ünvanı "birim" diye sunuyordu, gerçek departman kırılımı hiç yoktu.

## senseik-017'nin Deseni — Burada Tekrar Kullanıldı

senseik-017 (`docs/handoffs/demo-data-hygiene.md`), org şemasında aynı karışıklığı çözmek için şu deseni ekledi:
- `OrgPlacement.LevelUnitFor(orgUnitId, "Department")` / `"Title"` — org yerleşim zincirini yürüyüp istenen seviyedeki atayı bulan, `Sense.Modules.Employee.Application.Career` içindeki saf hesaplama sınıfı.
- `OrgChartNodeDto`'ya `DepartmentName` eklenip FE'de ünvanın altına soluk bir satır olarak gösterilmesi.

Bu kart, **aynı `OrgPlacement`/`LevelUnitFor` mekanizmasını** üç yeni yerde tekrar kullandı (yeni bir çözümleme mantığı yazılmadı):

1. **Çalışanlar listesi** (`EmployeeListItemDto`/`ListEmployeesHandler`, `EmployeeQueries.cs`): `EmployeeDetailDto` zaten `DepartmentName`/`TitleName`'i `placement.LevelUnitFor(...)` ile çözüyordu (bu satır senseik-017'den önce de vardı, o kart sadece org şemasına taşımıştı) — grid satırına da aynı iki alan eklendi, `placement` zaten mevcut değişkendi, tek satırlık ekleme.
2. **Çalışan rehberi** (`DirectoryEntryDto`/`GetEmployeeDirectoryHandler`): `OrgPlacementLoader` enjekte edilip aynı iki `LevelUnitFor` çağrısı eklendi.
3. **Raporlar KPI'sı** (`EmployeeAnalyticsQueries.GetGenderByDepartmentAsync`, Employee.Infrastructure): `IOrgUnitLookup.ListActiveTypesAsync/ListActiveUnitsAsync`'ten `OrgPlacement` inşa edilip ham (OrgUnitId, Gender) sayımları Department seviyesine yeniden gruplandı. `OrgPlacement` sınıfı saf (yalnızca Organization.Contracts tiplerine bağımlı) olduğu için Employee.Infrastructure → Employee.Application referansı zaten var olan proje bağımlılığıyla sorunsuz kullanılabildi.

Analytics modülü Employee.Application'a asla referans veremediği için (`Modules_TalkOnlyThroughContracts`), departman çözümlemesi Employee tarafında bir Contracts metodu (`IEmployeeAnalyticsQueries.GetGenderByDepartmentAsync`) olarak dışa açıldı; Analytics sadece hazır sonucu tüketiyor — org yerleşim mantığını asla görmüyor.

## Ekran Bazında Değişiklikler

### 1) Çalışanlar listesi (`web/src/pages/employees/employee-list.tsx`)

Tek, belirsiz "Birim" sütunu **iki ayrı sütuna** bölündü: "Departman" (`departmentName`) ve "Ünvan" (`titleName`) — ikisi de artık backend'de org yerleşim zinciriyle çözülüyor (ham `OrgUnitId` adı değil). Karar gerekçesi: mevcut veri modeli (`EmployeeDetailDto`, `OrgChartNodeDto`) zaten departman/ünvanı iki ayrı, isimlendirilmiş alan olarak taşıyor; listeyi de aynı ayrıma taşımak, sütun adını sadece "Ünvan" yapıp departman bilgisini tamamen kaybetmekten daha doğru — departman filtre/kırılım ihtiyacı (kabul kriteri 2) zaten departman verisinin ayrı var olmasını gerektiriyordu.

Kart görünümü (`employee-card-grid.tsx`): başlık altı ünvan satırının altına, org şemasındaki desenle aynı şekilde soluk bir departman satırı eklendi.

### 2) Çalışan rehberi (`web/src/components/employees/employee-directory.tsx`)

Kabul kriteri 3 (Bordro/Puantör'de sütun boş kalmasın): kök neden veri modeli değil **yetki** sorunuydu — FE, `organization.units.read` gerektiren ayrı bir org-unit listesiyle isim eşliyordu. `DirectoryEntryDto`'ya `TitleName`/`DepartmentName` eklenip backend'de (aynı `OrgPlacementLoader` ile) çözüldüğü için artık `employee.directory.view` yetkisi yeten her rol (organization.units.read'i olmasa bile) veriyi görüyor. Sütun `departmentName ?? titleName ?? "—"` gösteriyor; eski `nameById` lookup'ı yalnızca birim filtresi (`OrgUnitSelect`, hâlâ `organization.units.read` gerektiriyor — bu kartın kapsamı dışı) için kaldı.

### 3) Raporlar → Birim Bazında Cinsiyet Dağılımı (`Sense.Modules.Analytics.Application.Kpis.WorkforceKpiFeatures`)

Yeni KPI: `GENDER_RATIO_BY_DEPARTMENT` ("Departman bazında cinsiyet dağılımı"), mevcut `GENDER_RATIO_BY_UNIT`'in yanında. **Karar:** mevcut `GENDER_RATIO_BY_UNIT`'i olduğu gibi bırakıp yeni, gerçek departman kırılımlı bir KPI eklemeyi tercih ettim — ünvan bazlı kırılımı tamamen kaldırmak bilgi kaybı olurdu (bazı tenant'larda ünvan bazlı dağılım da anlamlı olabilir), asıl eksik olan "artık departman bazında da" çıkabilmesiydi. İki KPI da aynı "Kırılım tablosu"nda (`WorkforceKpiPanel.tsx`, `breakdownRows`) satır olarak beliriyor; tablo zaten "Gösterge" sütununa göre sıralanabildiğinden (TanStack Table client-side sort) kullanıcı iki kırılımı bu sütuna tıklayarak ayırabiliyor — ayrı bir filtre bileşeni eklenmedi (mevcut kırılım tablosunda hiçbir KPI için filtre yok, sadece sıralama var; bu kartın kapsamına yeni bir UI bileşeni eklemek YAGNI'ye aykırı olurdu).

Küçük hücre gizleme (G-10-21, `MinCellSize=5`) departman kırılımında da aynen uygulanıyor (`ShareOf` ortak fonksiyon, değişmedi).

Erişim kapsamı daraltılmış (unit-manager) roller için: hem `GENDER_RATIO_BY_UNIT` hem `GENDER_RATIO_BY_DEPARTMENT` `accessibleUnitIds`'e göre filtreleniyor (aynı mantık iki listeye de uygulandı).

Ayrıca aynı dosyada fark edilen bir ek karışıklık düzeltildi: "Birim bazında kadro" grafiği (`reports:kpis.charts.department`, `getDepartmentDistribution`) zaten **gerçek** departman verisi gösteriyordu ama başlığı jenerik "Birim" diyordu — başlık "Departman bazında kadro" olarak düzeltildi (kod/veri değişmedi, sadece i18n metni; tr/en/de üç dilde).

## Değişen Dosyalar

**Backend:**
- `src/Modules/Employee/Sense.Modules.Employee.Application/Employees/EmployeeDtos.cs` — `EmployeeListItemDto.DepartmentName`, `DirectoryEntryDto.TitleName/DepartmentName` eklendi.
- `src/Modules/Employee/Sense.Modules.Employee.Application/Employees/EmployeeQueries.cs` — `ListEmployeesHandler`, `GetEmployeeDirectoryHandler` (artık `OrgPlacementLoader` enjekte ediyor).
- `src/Modules/Employee/Sense.Modules.Employee.Contracts/EmployeeAnalyticsContracts.cs` — `IEmployeeAnalyticsQueries.GetGenderByDepartmentAsync` eklendi.
- `src/Modules/Employee/Sense.Modules.Employee.Infrastructure/Lookups/EmployeeAnalyticsQueries.cs` — yukarıdaki metodun implementasyonu (`IOrgUnitLookup` artık constructor'da).
- `src/Modules/Analytics/Sense.Modules.Analytics.Application/Kpis/WorkforceKpiFeatures.cs` — `KpiKeys.GenderRatioByDepartment`, `BuildGenderRatioByUnit` ortak yardımcıya çevrildi, handler her iki kırılımı da hesaplıyor.
- Testler: `tests/Modules/Sense.Modules.Employee.Tests/EmployeeIntegrationTests.cs` (DirectoryEntryDto yeni alanlar), `EmployeeAnalyticsQueriesTests.cs` (yeni `GetGenderByDepartmentAsync_...` testi + `NewQueries` yardımcı kurucu), `tests/Modules/Sense.Modules.Analytics.Tests/WorkforceKpiHandlerTests.cs` (restricted-scope testi güncellendi + yeni `GenderRatioByDepartment_...` testi).

**Frontend:**
- `web/src/services/employees.service.ts` — `EmployeeListItem.departmentName`, `DirectoryEntry.titleName/departmentName`.
- `web/src/pages/employees/employee-list.tsx` — "orgUnit" sütunu → "department" + "title" iki sütun.
- `web/src/components/employees/employee-card-grid.tsx` — ünvanın altına soluk departman satırı.
- `web/src/components/employees/employee-directory.tsx` — masaüstü tablo + mobil kart, backend alanlarını kullanıyor.
- `web/public/locales/{tr,en,de}/employees.json` — `columns.orgUnit` → `columns.department` + `columns.title`.
- `web/public/locales/{tr,en,de}/reports.json` — `kpi.GENDER_RATIO_BY_DEPARTMENT`, `charts.department` metni, `scopedNote` metni.

## Kapsam Dışı / Bilinçli Olarak Dokunulmadı

- `web/src/config/navigation.ts` — talimatta belirtildiği gibi dokunulmadı.
- Çalışan rehberinin birim **filtresi** (`OrgUnitSelect`, `organization.units.read` gerektiriyor) — kabul kriteri yalnızca sütunun boş kalmamasını istiyordu, filtrenin de yetkisiz kullanıcıya açılması ayrı bir karar/kart gerektirir (yetki genişletmesi, bu kartın kapsamı dışı).
- Mobil uygulama (`apps/senseik-mobile`) `titleName`/`departmentName` alanlarını zaten tüketiyordu (profil ekranı) — yeni alanlar sadece eklendiği için geriye dönük uyumlu, dokunulmadı.

## Test Planı ve Sonuç

- `EmployeeAnalyticsQueriesTests.GetGenderByDepartmentAsync_RollsUpTitleLevelAssignments_OntoTheirDepartmentAncestor` — iki farklı ünvan altındaki çalışanların aynı departman kovasına toplandığını, departmana doğrudan atanan ve şirket seviyesinde kalan (departmansız) çalışanların doğru işlendiğini doğruluyor.
- `WorkforceKpiHandlerTests.GenderRatioByDepartment_UsesDepartmentRolledUpCounts_UnderItsOwnKpiKey` ve güncellenen `RestrictedScope_...` testi — yeni KPI'nın kendi anahtarı altında, ölçekli/tam senaryolarda doğru davrandığını doğruluyor.
- `EmployeeIntegrationTests` — `DirectoryEntryDto` yeni alanları (ünvan/departman) mevcut senaryoda doğrulandı.
- Docker SDK konteyneriyle: `Sense.Modules.Employee.Tests` build temiz, filtrelenmiş çalıştırma (`EmployeeAnalyticsQueriesTests`+`EmployeeIntegrationTests`) 15/15 yeşil; tam paket 205/209 yeşil — kalan 4 hata `PaymentsIntegrationTests`'te önceden var olan, bu kartla ilgisiz `IUserProfileLookup`/`PaymentsService` DI eksikliği (senseik-017 handback notunda da kayıtlı). `Sense.Modules.Analytics.Tests` build temiz, 24/24 yeşil (yeni `GetGenderByDepartmentAsync`/`GenderRatioByDepartment` testleri dahil). Tam solüsyon build'i bir defa host'un bellek baskısı yüzünden 3 ilgisiz modülde (Leave/Identity/Workflow, "Cannot allocate memory") geçici hata verdi — dokunduğum projelerin izole build'i temiz.
- `node:24-alpine` konteyneriyle (host'ta pnpm/node yok; CLAUDE.md'nin Docker fallback'i kullanıldı) `pnpm install --frozen-lockfile` + `pnpm exec tsc --noEmit` (hatasız, çıktı yok) + `pnpm exec eslint src` (0 hata, 1 önceden var olan ve bu kartla ilgisiz uyarı: `employee-list.tsx:166` `items` useMemo bağımlılığı — dokunduğum satırlar 290-307 civarı, bu uyarı ondan önce de vardı).
- Manuel inceleme (kod okuması ile, ekran görüntüsü alınmadı): çalışan listesinde "Departman"/"Ünvan" ayrı sütunlar; rehberde departman görünüyor; raporlarda iki ayrı kırılım satırı (`GENDER_RATIO_BY_UNIT`, `GENDER_RATIO_BY_DEPARTMENT`) `WorkforceKpiPanel`'in kırılım tablosunda beliriyor.

## Oturum Notu — Ortam Sorunu

Bu kartın uygulanması sırasında Docker Desktop backend'i (`dockerDesktopLinuxEngine` named pipe) birkaç kez düşüp kendi kendine toparlandı — muhtemelen bu makinede eşzamanlı çalışan başka oturumların (bkz. `feedback-parallel-sessions.md` hafıza notu) yarattığı yük yüzünden. `docker version`/`docker ps` gibi en temel komutlar bile zaman zaman 10+ saniye askıda kaldı veya "failed to connect to the docker API" hatası verdi. Build/test adımları her komuttan önce `docker version` ile sağlık kontrolü yapılarak, gerektiğinde tekrar denenerek tamamlandı. Bir sonraki oturumda benzer bir şey yaşanırsa: sabırla tekrar dene, tüm solüsyonu değil sadece dokunulan proje(ler)i build et (bellek baskısı altında tam solüsyon build'i başarısız olabiliyor).
