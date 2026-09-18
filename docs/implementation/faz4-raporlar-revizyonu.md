# Raporlar bölümü revizyonu (roadmap #70 / #44) — 2026-09-18

Kaynak analiz: [11.3.12-raporlama-analitik.md](../analysis/kolayik-video/11.3.12-raporlama-analitik.md) (Raporlar menüsü, bakiye raporu, Kendin oluştur, birim yöneticisi kısıtları). Durum: [faz4-durum-takibi.md](../analysis/kolayik-video/faz4-durum-takibi.md) #70, [faz1b-durum-takibi.md](../analysis/kolayik-video/faz1b-durum-takibi.md) #44.

## Neden revize edildi

- `web/src/pages/reports.tsx` Kolay İK'daki **Raporlar** menüsünü değil, Poma/"Dijital Veri Uzmanı" anlatımını taklit eden bir analitik panosuydu: sabit kodlu devir trendleri, kuşak/kıdem/çalışma modeli dağılımları, ayrılma nedenleri, "yapay zeka içgörüleri" ve "+4% bu ay" gibi rozetlerin tamamı **uydurma veriydi**; gerçek KPI gelmezse 48 kişi / %8,4 gibi varsayılanlar gösteriliyordu. Poma'nın videolarda ekranı yok ([Aday] etiketli), gerçek ihtiyaç değil.
- Gerçek Kolay İK akışı (sol menü **Raporlar** → kategori → rapor türü → **Birimler/Ay/Yıl** → oluştur → indir; **Son oluşturduklarım**) sayfanın en altında tek bir `Select` + buton olarak duruyordu ve katalogda yalnızca CSV üreten eksik belge raporu vardı.

## Ne yapıldı

**Sözleşme (`Sense.Modules.Reporting.Contracts`)**
- `IReportProvider` → `CategoryKey` (İdari / Çalışan / İzinler / Finans, `ReportCategories`) ve `Parameters` (`ReportParameterDescriptor{Key, Kind(OrgUnits|Year|Month|DateRange|Boolean), IsRequired}`). Anahtarlar `ReportParameterKeys` (`orgUnitIds`, `year`, `month`, `from`/`to`, `includeTerminated`); okuma/doğrulama yardımcıları `ReportParameters` (Ay/Yıl → aralık semantiği dahil).
- Reporting komut işleyicisi `ReportParameterValidation` ile zorunlu/biçim doğrulaması yapar (`report_execution.parameter_required|parameter_invalid`); katalog kategori sırasına göre döner ve parametre tanımlarını taşır.
- Kapsam kuralı: Reporting çalışan görünürlüğünü bilmez; **her sağlayıcı** `IDataScopeService.GetVisibleEmployeeIdsAsync(rapor izni)` ile kendi satır kümesini süzer, Birimler filtresi `IOrgUnitLookup.GetSelfAndDescendantIdsAsync` ile alt birimleri kapsar.

**Yeni ortak parçalar**
- `Employee.Contracts.IEmployeeReportReader` / `EmployeeReportRow`: raporların tekrar ettiği kimlik + yerleşim kolonları (Ad, Soyad, kimlik no, işe giriş, durum, güncel kariyer kaydının seviye→birim eşlemesi). İzin/kapsam uygulamaz; çağıran görünür kümeyi verir.
- `Organization.Contracts.OrgUnitHierarchy`: kiracının her hiyerarşi seviyesi için dinamik kolon ("kariyer bilgileri şirket yapınıza göre dallanır"); açık atama önce, sonra en derin birimin üst zinciri.
- `Sense.Shared.Infrastructure.Export.ReportText`: Excel başlık/sayfa/enum metinleri `SharedResource{,.en}.resx` `report.col.* / report.sheet.* / report.enum.{Enum}.{Üye}` anahtarlarından, istek kültürüyle.
- `LeaveBalanceEngine.CarryOverLoss`: Kolay İK "Devir kayıpları" kolonu (ledger'dan türetilir; sınırsız devirde 0).

**Hazır raporlar (Kolay İK karşılığı → anahtar, kategori, izin)**
| Kolay İK | Anahtar | Kategori | İzin | Filtreler | Sağlayan |
|---|---|---|---|---|---|
| Çalışan izin bakiye raporu (11 kolon) | `leave.balance.summary` | İdari | `leave.balances.read` | Birimler, işten ayrılanlar | Leave |
| İzin raporu | `leave.requests` | İzinler | `leave.requests.read` | Tarih aralığı (zorunlu), Birimler, işten ayrılanlar | Leave |
| Çalışan listesi raporu | `employee.list` | Çalışan | `employee.employees.read` (+`employee.sensitive.view` → kimlik no/doğum tarihi kolonları) | Birimler, işten ayrılanlar | Employee |
| Finans raporu (maaş yarısı) | `finance.salaries` | Finans | `employee.salary.view` | Birimler | Employee |
| Eksik belge raporu (SenseİK) | `documents.missingDocuments` | İdari | `documents.requirements.view` | — | Documents (CSV → Excel) |

**Web (`/app/reports`)**
- Kategori grupları → rapor kartları → yanda parametre formu (birim çoklu seçim ağaç girintili, yıl/ay, tarih aralığı + Bu ay/Geçen ay/Bu yıl, anahtar) → **Rapor oluştur** → indirme otomatik başlar; altta **Son oluşturduklarım** (dosya adı, hata mesajı, indir). E-postadaki bağlantı `/app/reports?execution=<id>` sayfada dosyayı sunar.
- **İK göstergeleri** ayrı sekme (`components/reports/workforce-kpi-panel.tsx`): yalnız `GET /analytics/kpis/*` ve birim dağılımı; veri yoksa "veri yok". Grafiklerde tek eksen, kimlik renkleri sabit (cinsiyet), büyüklük grafikleri tek renk, ≥2 seri için legend.
- Yerelleştirme tr/en/de `reports.json` yeniden yazıldı; katalog etiketleri `catalog.<anahtar>.label/description` (noktalı anahtar iç içe yol).

**Testler**
- `Sense.Modules.Reporting.Tests`: parametre doğrulama, boş/trim davranışı, katalog sıralaması (+ mevcut 24).
- `Sense.Modules.Leave.Tests`: `LeaveBalanceCarryOverLossTests` (domain), `LeaveReportProviderTests` (Postgres; Excel'i ClosedXML ile geri okur).
- `Sense.Modules.Organization.Tests.OrgUnitHierarchyTests`.
- Web: `reports.service.test.ts` (`groupReportCatalog`).

## Yapılmayanlar / sonraki adımlar (analizdeki sıra)

1. **Kendin oluştur** (`reporting.custom.build`): alan kataloğu (`IReportFieldCatalogContributor`, Genel/Kişisel/İzinler/Ödemeler/FM grupları), satır tanesi kuralı, ad-hoc `ReportExecution`. Birim yöneticisine hiçbir zaman verilmez.
2. **Asenkron üretim + e-posta eki**: `ReportExecution` durum makinesi hazır; Hangfire `reports` kuyruğu, `ReportExecutionCompleted` → `report.ready` bildirimi (ek ≤ `Notifications:MaxAttachmentMb`), `ResultRetentionDays`. Şu an dosya satırda `bytea`, üretim istek içinde.
3. **`leave.balance.detail`** (25 kolon; Ay/Yıl → aralık kolonları `LeaveBalanceEngine.Replay`); PDF çıktısı (`IDocumentRenderer`, geniş rapor yatay sayfa).
4. **Finans raporunun ödemeler yarısı** (avans/harcama/FM ödemesi) — Expense modülü `IReportProvider` katkısı veya `finance.salaries`'e Contracts üzerinden ek kolonlar.
5. **Birim yöneticisi köprüsü**: `UnitAuthorization.CanViewUnitReports` → `reports.catalog.view`/`reports.executions.generate` @Department; `SystemRoleDefinitions` Manager satırındaki `reports.team.*` deseni gerçek anahtarlarla eşleşmiyor (hot file, PC-A kararı).
6. Çalışma takvimi raporu (Attendance), İzin raporunda **İmzalı** kolonu (#25), özel alan kolonları (`CustomFieldDefinition.IsReportable`).
