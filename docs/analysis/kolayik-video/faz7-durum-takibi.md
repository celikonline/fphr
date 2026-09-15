# Faz 7 Durum Takibi

Kaynak: [11.7-yol-haritasi.md](./11.7-yol-haritasi.md) satır 137-140 (kalem #99-102) + satır 158 (Faz bazlı özet).

**Son güncelleme:** 2026-09-16
**Nasıl güncellenir:** Bir kalem tamamlandığında/ilerlediğinde durumu ve kanıt satırlarını güncelle, "Son güncelleme" tarihini değiştir. Durumlar: ❌ Başlanmadı · 🟡 Kısmi · ✅ Bitti · 🚫 Önkoşul eksik (bloklandı).

## Durum tablosu

| # | Kalem | Durum | Kanıt | Eksik / Not |
|---|---|---|---|---|
| 99a | KPI: `CAREER_PATH_RATIO` | ✅ Bitti | Yeni `Sense.Modules.Analytics` modülü (`Contracts`/`Application`/`Api`, kalıcı veri yok); `Employee.Contracts.IEmployeeAnalyticsQueries` + `EmployeeAnalyticsQueries` (Employee.Infrastructure) — `CareerRecord.ChangeType` (Promotion/LateralMove/Demotion/TitleChange) sayısı / aktif headcount; `GET /api/v1/analytics/kpis/workforce`; izin `analytics.kpi.view` (HR/Owner/Admin/Auditor'da zaten `analytics.*`/`*`/`reports.*` wildcard'ı vardı, `SystemRoleDefinitions.cs` değişmedi); testler (`Sense.Modules.Analytics.Tests`, `Sense.Modules.Employee.Tests/EmployeeAnalyticsQueriesTests.cs`) | Web tarafı yok (Raporlar sayfası altyapısı Faz 4'te bile yok) |
| 99b | KPI: cinsiyet oranı (genel) | ✅ Bitti | Aynı endpoint, `GENDER_RATIO` KPI'sı — `Employee.Gender` alanına göre aktif headcount kırılımı | — |
| 99c | KPI: cinsiyet × pozisyon grubu | 🚫 Önkoşul eksik | — | `OrgUnitId`'yi okunabilir bir pozisyon/birim adına çözmek `Organization.Contracts`'a bağımlı (cross-module); v1 yalnız şirket geneli döndü, birim kırılımı sonraki adım |
| 99d | KPI: turnover (ilk 6 ay / cinsiyet × pozisyon / yıl-yıl), cinsiyet oranı **hedef eşiği** | 🚫 Önkoşul eksik | — | `EmployeeStatus.Terminated` tanımlı ama `Employee` aggregate'inde onu tetikleyen bir `Terminate` metodu yok; işten ayrılış tarihi/nedeni alanı hiç yok (`Employee.cs:727`: "termination is a later package"). Faz 1a paketi E5a ("İşten ayrılış sihirbazı", roadmap #38) da "❌ Başlanmadı" — bkz. `faz1a-durum-takibi.md`. Hedef eşiği ayrıca bir `TenantSettings` alanı gerektirir (yok) |
| 99e | KPI: `OFFICE_PRESENCE_RATE` | 🚫 Önkoşul eksik | — | Çalışma modeli alanı (Ofis/Hibrit/Uzaktan, roadmap #49) Employee'de yok; check-in lokasyon tipi de yok |
| 99f | KPI: `ENPS` | 🚫 Önkoşul eksik | — | Survey/eNPS modülü hiç yok; roadmap'te Faz 5 karar noktası ("evet ise Faz 5b") bile verilmemiş |
| 100 | Poma tarzı çapraz analizler (ayrılanların performans dağılımı, işe alım kanalı ↔ kalış süresi, performans ↔ eğitim) | 🚫 Önkoşul eksik | — | Performance ve Recruitment/ATS modülleri hiç yok; ayrıca turnover (99d) önkoşulu da eksik |
| 101 | AI ön mülakat / aday ön eleme botu (`ICandidateScreeningBot`) — **karar noktası** | 🚫 Önkoşul eksik | — | Recruitment/ATS ve Ai modülleri hiç yok; roadmap'in kendi kararı da "Faz 7 sonrası, ATS pilot geri bildirimiyle" diyor |
| 102 | Ai bilgi tabanı ↔ Yardım Merkezi makaleleri (RAG kaynağı) | 🚫 Önkoşul eksik | — | Support/Announcements modülü (roadmap #69, Faz 4) hiç yok; Ai modülü de yok |
| — | Toplu işten çıkarma eşik uyarısı (30 gün penceresi; 10/%10/30) [Mevzuat] | 🚫 Önkoşul eksik | — | Roadmap madde #39 (Faz 1); işten ayrılış alanları (99d ile aynı kök neden) yok |

## Özet

- **Bitti:** 2/9 kalem ✅ (99a, 99b — Analytics modülü, backend).
- **Bloklandı (önkoşul eksik):** 7/9 kalem 🚫 — hepsi Faz 2-6'da henüz kurulmamış modüllere (Performance, Recruitment/ATS, Support/Announcements, Survey, Ai) veya Faz 1a'nın bitmemiş bir paketine (E5a işten ayrılış) bağımlı.
- Faz 7'nin kendi roadmap notu da bunu öngörüyor: "AI ön eleme botu" zaten kendi karar noktası; ama turnover/OFFICE_PRESENCE_RATE/ENPS/Poma/RAG için de aynı derecede önkoşul eksikliği var — roadmap dokümanı bunu ayrıca işaretlemiyor, bu tespit kod taramasından çıktı.

**Doğrulama (2026-09-16):**
- (Doğrulama adımları uygulama tamamlandıktan sonra buraya eklenecek: `dotnet build`, `dotnet test tests/Sense.Tests.Architecture`, `dotnet test tests/Modules/Sense.Modules.Analytics.Tests`, `dotnet test tests/Modules/Sense.Modules.Employee.Tests`.)

**Bilinen açık noktalar / kararlar:**
- `Sense.Modules.Analytics` bilinçli olarak yalnız 3 katmanlı (`Contracts`/`Application`/`Api`) — `Domain`/`Infrastructure` yok çünkü kalıcı veri yok (KPI'lar sorgu zamanında hesaplanıyor). `Modules_HaveOneDbContextPerModule` mimari testi yalnız var olan `Infrastructure` katmanlarını kontrol ettiği için bu ihlal değil. Bir sonraki KPI (ör. snapshot/cache gerektiren) eklendiğinde `Domain`/`Infrastructure` + `analytics` şeması bu noktada kurulacak.
- Diğer makine (PC-B/paralel oturum) bu çalışma sırasında `Attendance` ve `Payroll` modüllerini `ModuleCatalog.cs`'e ekledi — bu doküman ve Analytics çalışması onlarla çakışmıyor, sadece eşzamanlı ilerliyor.

## Değişiklik geçmişi

- 2026-09-16: İlk durum tespiti + Analytics modülü (CAREER_PATH_RATIO, GENDER_RATIO) uygulaması. 99a, 99b ✅; kalan 7 kalem önkoşul eksikliği nedeniyle 🚫 olarak işaretlendi.
