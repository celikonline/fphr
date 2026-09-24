# Faz 7 Durum Takibi

Kaynak: [11.7-yol-haritasi.md](./11.7-yol-haritasi.md) satır 137-140 (kalem #99-102) + satır 158 (Faz bazlı özet).

**Son güncelleme:** 2026-09-18 (backend inceleme: CAREER_PATH_RATIO formülü düzeltildi, fazla mesai N+1 sorgusu giderildi, birim raporları izni gerçek uçlara bağlandı; web tarafı da bu turda tamamlandı)
**Nasıl güncellenir:** Bir kalem tamamlandığında/ilerlediğinde durumu ve kanıt satırlarını güncelle, "Son güncelleme" tarihini değiştir. Durumlar: ❌ Başlanmadı · 🟡 Kısmi · ✅ Bitti · 🚫 Önkoşul eksik (bloklandı).

## Durum tablosu

| # | Kalem | Durum | Kanıt | Eksik / Not |
|---|---|---|---|---|
| 99a | KPI: `CAREER_PATH_RATIO` | ✅ Bitti | Yeni `Sense.Modules.Analytics` modülü (`Contracts`/`Application`/`Api`, kalıcı veri yok); `Employee.Contracts.IEmployeeAnalyticsQueries` + `EmployeeAnalyticsQueries` (Employee.Infrastructure) — `CareerRecord.ChangeType` (Promotion/LateralMove/Demotion/TitleChange) sayıları; `GET /api/v1/analytics/kpis/workforce`; izin `analytics.kpi.view` (HR/Owner/Admin/Auditor'da zaten `analytics.*`/`*`/`reports.*` wildcard'ı vardı, `SystemRoleDefinitions.cs` değişmedi); testler (`Sense.Modules.Analytics.Tests`, `Sense.Modules.Employee.Tests/EmployeeAnalyticsQueriesTests.cs`) | Web: `/app/reports` → "İK göstergeleri" sekmesi (`web/src/components/reports/workforce-kpi-panel.tsx`, 2026-09-18) — yalnız gerçek KPI/izin/FM verisi, sahte seri yok. **G-10-20 (2026-09-18):** oran formülü 11.3.12'ye çekildi — terfi / (terfi + yatay geçiş); ilk sürüm yanlışlıkla dört değişiklik türünün toplamını aktif headcount'a bölüyordu, kademe düşürme/unvan değişikliği artık paya girmiyor (yalnız kırılımda görünür) |
| 99b | KPI: cinsiyet oranı (genel) | ✅ Bitti | Aynı endpoint, `GENDER_RATIO` KPI'sı — `Employee.Gender` alanına göre aktif headcount kırılımı | — |
| 99d1 | KPI: turnover (genel oran) | ✅ Bitti | E5a'nın tamamlanmasıyla (`Employee.Terminate`, `TerminationDate/TerminationType`) önkoşul karşılandı: `IEmployeeAnalyticsQueries.GetTerminationCountsAsync` (dönem içi `TerminationDate` sayımı) → `TURNOVER_RATE` = dönem içi ayrılan / aktif headcount (yaklaşık — `CAREER_PATH_RATIO` ile aynı yöntem, tarihsel dönem-başı headcount yerine "şimdiki aktif headcount") | Ayrılış türüne göre (gönüllü/gönülsüz) kırılım TK-020 ile eklendi (2026-09-23, `fcf329bb`); tarihsel headcount sorgusu yok |
| 99d2 | KPI: turnover ilk 6 ay | ✅ Bitti (basitleştirilmiş) | `EARLY_TURNOVER_RATE` = dönem içi ayrılanların kıdemi <182 gün olanların payı (`TerminationCounts.WithinFirstSixMonths`) | Roadmap'in kastettiği kohort bazlı "yeni işe alınanın ilk 6 ayda ayrılma oranı" değil — dönem içindeki ayrılışların kıdem dağılımı bu; kohort bazlı versiyon işe alım kohortu + takip penceresi takibi gerektirir |
| 99c1 | KPI: cinsiyet × pozisyon (birim) grubu | ✅ Bitti | `Organization.Contracts.IOrgUnitLookup` (zaten vardı, `ListActiveUnitsAsync`) + yeni `IEmployeeAnalyticsQueries.GetGenderByOrgUnitAsync` (Employee'de `OrgUnitId` zaten denormalize alan) → `GENDER_RATIO_BY_UNIT` KPI'sı, dimension = "{BirimAdı} / {Cinsiyet}" (birimsiz çalışan "Unassigned"). Analytics.Application artık Organization.Contracts'a da referans veriyor | "Pozisyon" bu kod tabanında ayrı bir kavram değil — en derin `OrgUnit` kullanılıyor (roadmap'in kendi modeliyle tutarlı, bkz. `EmployeeOrgAssignment`) |
| 99c2 | KPI: turnover × pozisyon (birim) grubu, cinsiyet oranı **hedef eşiği** | 🚫 Önkoşul eksik | — | Turnover'ın birim kırılımı ayrı bir sorgu gerektirir (henüz eklenmedi — `GetTerminationCountsAsync` birim bilgisi döndürmüyor). Hedef eşiği ayrıca bir `TenantSettings` alanı gerektirir (yok) |
| 99e | KPI: `OFFICE_PRESENCE_RATE` | 🚫 Önkoşul eksik | — | Çalışma modeli alanı (Ofis/Hibrit/Uzaktan, roadmap #49) Employee'de yok; check-in lokasyon tipi de yok |
| 99f | KPI: `ENPS` | 🚫 Önkoşul eksik | — | Survey/eNPS modülü hiç yok; roadmap'te Faz 5 karar noktası ("evet ise Faz 5b") bile verilmemiş |
| 100 | Poma tarzı çapraz analizler (ayrılanların performans dağılımı, işe alım kanalı ↔ kalış süresi, performans ↔ eğitim) | 🚫 Önkoşul eksik | — | Performance ve Recruitment/ATS modülleri hiç yok (turnover artık hazır ama çapraz analiz için performans/işe alım verisi gerekiyor) |
| 101 | AI ön mülakat / aday ön eleme botu (`ICandidateScreeningBot`) — **karar noktası** | 🚫 Önkoşul eksik | — | Recruitment/ATS ve Ai modülleri hiç yok; roadmap'in kendi kararı da "Faz 7 sonrası, ATS pilot geri bildirimiyle" diyor |
| 102 | Ai bilgi tabanı ↔ Yardım Merkezi makaleleri (RAG kaynağı) | 🚫 Önkoşul eksik | — | Support/Announcements modülü (roadmap #69, Faz 4) hiç yok; Ai modülü de yok |
| — | Toplu işten çıkarma eşik uyarısı (30 gün penceresi; 10/%10/30) [Mevzuat] | ✅ Bitti (Faz 1a'da) | — | Faz 7'nin bu kalemi aslında Faz 1a'da (`CollectiveDismissalEvaluator`, roadmap #39) çözülmüş — ayrılış alanları artık var; bkz. `faz1a-durum-takibi.md` #39. Analitik/rapor tarafı (bu doküman kapsamında ayrı bir iş değil, aynı önkoşula dayanıyordu) turnover KPI'sı ile birlikte kapandı |

## Özet

- **Bitti:** 6/10 kalem ✅ (99a, 99b, 99c1, 99d1, 99d2 — Analytics modülü; toplu çıkarma uyarısı zaten Faz 1a'da bitmişti).
- **Bloklandı (önkoşul eksik):** 4/10 kalem 🚫 — 99c2 (turnover birim kırılımı + hedef eşiği) hariç hepsi Faz 2-6'da henüz kurulmamış modüllere (Performance, Recruitment/ATS, Support/Announcements, Survey, Ai) bağımlı.
- Bu turda `IOrgUnitLookup`'ın zaten var olduğu görüldü (Organization.Contracts, Employee'nin org birimi doğrulaması için kullandığı sözleşme) — Analytics'e cross-module referans olarak eklendi, `GENDER_RATIO_BY_UNIT` KPI'sı bunun üzerine kuruldu.

**Doğrulama (2026-09-16):**
- `dotnet build Sense.slnx`: 0 hata (Attendance/Payroll/Recruitment modülleri de dahil, tüm çözüm).
- `dotnet test tests/Modules/Sense.Modules.Analytics.Tests`: geçti (handler birim testleri, turnover + birim kırılımı dahil).
- `dotnet test tests/Modules/Sense.Modules.Employee.Tests --filter EmployeeAnalyticsQueriesTests`: geçti (Postgres/Testcontainers; tenant izolasyonu, kariyer/turnover/birim sayımları).
- `dotnet test tests/Sense.Tests.Architecture`: 38/39 — kalan 1 hata (`UpdateMyProfileCommand -> employee.profile.self.edit`, 4 segmentli izin anahtarı) bu çalışmayla ilgisiz, başka bir oturumun self-servis profil düzenleme özelliğinden.

**Doğrulama (2026-09-18, G-10-18/19/20 + web tarafı):**
- `dotnet build src/Sense.Api/Sense.Api.csproj`: 0 hata (tüm modüller dahil; ilk denemede eşzamanlı başka bir oturumun `Announcements` modülündeki geçici derleme hatası tam çözümü bloke etmişti, o oturum düzeltince tekrarlanıp doğrulandı).
- `dotnet test tests/Modules/Sense.Modules.Analytics.Tests`: 22/22 geçti (kariyer oranı formülü + fazla mesai toplu maaş sorgusu testleri dahil).
- `dotnet test tests/Modules/Sense.Modules.Identity.Tests --filter "UnitManagerGrants|SystemRoleDefinitions"`: 15/15 geçti (birim raporları izin genişlemesi + HRSpecialist analytics.\* dahil).
- `dotnet test tests/Sense.Tests.Architecture`: 52/52 geçti (`Modules_TalkOnlyThroughContracts` dahil - Identity.Application'ın yeni Analytics/Reporting.Contracts referansları ihlal değil).
- `pnpm exec tsc --noEmit` (web): temiz.

**Bilinen açık noktalar / kararlar:**
- `Sense.Modules.Analytics` bilinçli olarak yalnız 3 katmanlı (`Contracts`/`Application`/`Api`) — `Domain`/`Infrastructure` yok çünkü kalıcı veri yok (KPI'lar sorgu zamanında hesaplanıyor). `Modules_HaveOneDbContextPerModule` mimari testi yalnız var olan `Infrastructure` katmanlarını kontrol ettiği için bu ihlal değil. Bir sonraki KPI (ör. snapshot/cache gerektiren) eklendiğinde `Domain`/`Infrastructure` + `analytics` şeması bu noktada kurulacak.
- `TURNOVER_RATE`/`EARLY_TURNOVER_RATE` bilinçli basitleştirmeler: dönem-başı tarihsel headcount yerine "şimdiki aktif headcount" (CAREER_PATH_RATIO ile tutarlı); "ilk 6 ay" kohort bazlı değil, dönem içi ayrılışların kıdem dağılımı. Daha kesin versiyonlar (tarihsel headcount sorgusu, işe alım kohortu takibi) ayrı bir iş olarak ele alınmalı.
- Test yazarken bulunan gerçek hata: `EmployeeAnalyticsQueries` sorguları `ModuleDbContext`'in tenant `HasQueryFilter`'ına dayanıyor (ambient `ITenantContext`), bu yüzden testte sorgu çağrısının da `TenantContextSetter.BeginScope(tenantId)` içinde yapılması gerekiyor — yalnız seed sırasında scope açmak yetersiz. Prod'da etkisi yok (tek ambient tenant, request boyunca sabit).
- Diğer makine (PC-B/paralel oturum) bu çalışma sırasında `Attendance`, `Payroll`, `Recruitment` modüllerini ekledi ve Faz 1a'nın E5a paketini (işten ayrılış, `Terminations/`) tamamladı — bu doküman ve Analytics çalışması onlarla çakışmadı, sadece eşzamanlı ilerledi.

## Değişiklik geçmişi

- 2026-09-16: İlk durum tespiti + Analytics modülü (CAREER_PATH_RATIO, GENDER_RATIO) uygulaması. 99a, 99b ✅; kalan 7 kalem önkoşul eksikliği nedeniyle 🚫 olarak işaretlendi.
- 2026-09-16: E5a (işten ayrılış) başka bir oturumda tamamlanınca turnover önkoşulu karşılandı; `TURNOVER_RATE`/`EARLY_TURNOVER_RATE` KPI'ları eklendi (99d1, 99d2 ✅). Toplu işten çıkarma uyarısının da Faz 1a'da zaten bitmiş olduğu görülüp ✅ olarak düzeltildi. Tam doğrulama: build + Analytics/Employee testleri + Architecture testleri (38/39, kalan hata ilgisiz).
- 2026-09-16: `GENDER_RATIO_BY_UNIT` eklendi (99c1 ✅, 99c ikiye ayrıldı: 99c1 bitti, 99c2 turnover birim kırılımı + hedef eşiği hâlâ bloklu). `IEmployeeAnalyticsQueries.GetGenderByOrgUnitAsync` + `Organization.Contracts.IOrgUnitLookup` (zaten mevcuttu) ile birim adı çözümleme.
- 2026-09-18: Web tarafı (paralel oturum): `/app/reports` → "Raporlar" (kategori kataloğu, Reporting modülünün ilk gerçek provider'ları) + "İK göstergeleri" sekmesi (`workforce-kpi-panel.tsx`) — yalnız gerçek KPI/izin/FM verisi, hiçbir sabit/sahte seri yok. "Web tarafı yok" notu bu satırdan kaldırıldı.
- 2026-09-18 (backend inceleme, `docs/analysis/analitik-raporlama-po-degerlendirmesi-2026-09-18.md`): **G-10-20** `CAREER_PATH_RATIO` formülü 11.3.12'ye çekildi (terfi/(terfi+yatay geçiş), önceki sürüm dört türün toplamını aktif headcount'a bölüyordu — testler güncellendi). **G-10-19** S-17 fazla mesai maliyetinde çalışan başına ayrı maaş sorgusu (N+1) `IEmployeeSalaryLookup.GetCurrentSalariesAsync` toplu okumasıyla tek sorguya indirildi. **G-10-18** birim yöneticisinin "kendi birim raporları" anahtarı (`UnitManagerGrants.UnitReportsRun`) hiçbir uç tarafından tüketilmiyordu (ölü izin) — artık `analytics.kpi.view`/`reports.catalog.view`/`reports.executions.generate`/`reports.my.executions`'ı da veriyor, böylece Reporting/Analytics'in gerçekte kontrol ettiği izinlerle eşleşiyor. Uygulamalar başlatıcısındaki "Analitik" kutusu kendine değil `/app/reports`'a yönlendiriyor artık.
- 2026-09-18 (aynı inceleme, devamı): **G-10-21** küçük hücre gizleme (11.3.12) eklendi — her KPI kırılımında (kariyer, turnover, cinsiyet, birim×cinsiyet) 0 < N < 5 olan hücrenin `Count`/`Share`'i API'den hiç çıkmıyor (`IsSuppressed=true`), yalnız boyut adı görünüyor; sıfır hücre gizlenmiyor. Eşik şimdilik sabit (`MinCellSize=5`), tenant bazlı ayar backlog'da. Web tarafı gizli hücreleri "Gizli (< 5)" gösteriyor. HRSpecialist rolüne `analytics.*` eklendi (daha önce yalnız `reports.*` vardı, KPI panosu görünmüyordu).
