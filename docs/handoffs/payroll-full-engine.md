# senseik-019 — Bordro Motoru Aşama 2–3 — UYGULAMA PLANI

**Durum: GATE 1 onayı bekliyor. Hiçbir dosya değiştirilmedi/oluşturulmadı.**

## ⚠️ Kart Tanımı Güncel Değil

Kart "Payroll modülü ❌ durumdan başlıyor" diyor — **artık doğru değil**. P0–P3 arası bir dilim zaten canlı:

- `PayrollCalculator.cs` — SGK taban/tavan, SGK işçi %14+%1, **kümülatif matrahla dilimli gelir vergisi** (zaten doğru kurulu), asgari ücret GV/DV istisnası, damga vergisi ‰7,59, 4 yemek yardımı varyantı, işveren SGK+işsizlik+employer cost, adım-adım trace.
- `PayrollLegislationSnapshot` + ADR 0008 ile tüm oranlar DB'den tarih aralıklı okunuyor (hardcode yok).
- Cross-module köprüler zaten var: `IWorkedTimeReader` (Attendance), `IAbsenceDaysReader` (Leave), `IExpensePayoutReader` (Expense) — null-fallback deseniyle.
- Excel export zaten var (S-14, cost center filtreli).
- Golden-set testleri mevcut.
- **e-Bildirge ve banka ödeme dosyası zaten "İlk Sürüm Dışı, P5+ backlog" olarak dokümante edilmiş** (`faz7-payroll-export-sozlesmesi.md` K-7) — senseik-019 bu ertelenmiş backlog'un karşılığı.

**Sonuç:** Sıfırdan motor kurmak değil, **mevcut P0–P3 dilimini büyütme**. Risk azalıyor (iskelet+test altyapısı+legislation mimarisi hazır) ama kapsam büyük.

## Bağımlılık Haritası (özet)

| İhtiyaç | Durum |
|---|---|
| FM ücret oranı, 270 saat sayacı | **YOK**, senseik-012'ye bağımlı (blocking) |
| Hafta tatili/resmi tatil, denkleştirme | **YOK**, senseik-014'e bağımlı (blocking) |
| SGK işyeri sicil/meslek kodu, teşvik | **KISMİ** — alan var (`IncentiveLawCode`), hesaba katılmıyor; senseik-010'a bağımlı |
| Sicil numarası | **KISMİ** — `ExternalPayrollCode` var; senseik-009'a bağımlı |
| Engelli vergi indirimi | **KISMİ** — `DisabilityDegree` var, indirim tutarı parametresi yok |
| Avans mahsubu | **KISMİ** — port hazır (`IExpensePayoutReader.OpenAdvanceBalance`), motora bağlanmamış |
| İcra/nafaka | **YOK**, sıfırdan tasarım (`GarnishmentOrder`) |
| Kümülatif GV matrahı devri | **YOK** — motor altyapısı var (`PriorCumulativeGvBase`), gerçek değer hesaplanmıyor |
| Maliyet merkezi | **VAR** ve kullanılıyor |
| PDF render | **KISMİ** — sözleşme var (`IDocumentRenderer`), implementasyon eksik |
| e-Bildirge/APHB, banka dosyası | **YOK**, bu kartın ana teslimatı |

## Alt-Aşamalandırma Önerisi (8 dilim, her biri ayrı kart/PR)

- **2a** — Kümülatif matrah devri + işveren teşviki (SGK 5 puan) — bağımsız, hemen başlanabilir.
- **2b** — Engelli vergi indirimi (GVK m.31) — matrah indirimi, `MinWageExemptionGv`'den ÖNCE uygulanmalı.
- **2c** — İcra/nafaka + avans mahsubu — yeni `GarnishmentOrder` kavramı, kesinti sırası netleştirilmeli.
- **2d** — Fazla mesai/hafta tatili/resmi tatil — senseik-012/014'ü bekler.
- **3a** — Resmi çıktılar (ücret bordrosu m.37, pusula PDF) — önce Shared'da `IDocumentRenderer` (QuestPDF) implementasyonu.
- **3b** — e-Bildirge/APHB — SGK resmi format şeması araştırılmalı; senseik-009/010 tamamlanmadan "taslak" üretebilir.
- **3c** — Banka ödeme talimat dosyası — format tenant/banka bazında değişir, MVP tek format.
- **3d** — Muhasebe entegrasyon fişi — tenant-özel hesap eşleme tablosu gerekebilir.

**Sıralama:** 2a→2b→2c bağımsız, hemen başlanabilir. 2d senseik-012/014'ü bekler; paralelde 3a/3b/3c/3d üzerinde çalışılabilir (zaten hesaplanmış Payslip üzerinden türetiliyor).

## Golden-Set Test Stratejisi (7 senaryo, merge_gate "5+" istiyor)

Asgari ücretli tam ay · orta gelir dilim aşan (mevcut, regresyon çapası) · engelli (2. derece) · teşvikli işveren (5510) · fazla mesaili · yıl içi işe giren (devirli matrah) · icra kesintili+açık avans.

Ek: APHB için ayrı "format doğruluğu" test seviyesi (SGK resmi şemaya karşı validasyon).

## En Yüksek Risk Noktaları (öncelik sırası)

1. **Gelir vergisi kümülatif matrah hatası** — en yaygın/gizli, yıl sonu toplu telafi + ceza riski.
2. **APHB format hatası** — idari para cezası (5510 m.102).
3. **İcra/nafaka kesinti sırası/oranı hatası** — hukuki sorumluluk.
4. **Banka dosyası format hatası** — mali kayıp.
5. **Engelli indirimi/teşvik yanlış uygulanması** — eksik/fazla ödeme riski.

**Önerilen doğrulama:** Her alt-aşamanın golden-set'i kod incelemesinden önce bir muhasebe/bordro uzmanına (SMMM) el ile hesaplattırılıp imzalatılmalı ("GATE 2: golden-set uzman onayı almadan merge yok"). Her `LegislationParameter` satırına `Source` (kanun/madde/RG referansı) zorunlu tutulmalı.

## EF Migration ve Modül Kaydı

Payroll zaten kayıtlı — **yeni modül kaydı YOK**, sadece yeni migration'lar. Yeni entity gereken dilimler: 2a (önceki işveren devir kaydı), 2c (`GarnishmentOrder`), 3d (muhasebe eşleme tablosu). **ADR 0008 dikkat noktası:** `LegislationParameter` tablosu şu an Leave şemasında; ADR "Payroll (Faz 3) başladığında taşınabilir" diyor — bu kart tam o faz, taşıma kararı bu kartta mı ayrı kartta mı verileceği netleştirilmeli.

## Kritik Dosyalar
- `src/Modules/Payroll/Sense.Modules.Payroll.Domain/Calculation/PayrollCalculator.cs`
- `src/Modules/Payroll/Sense.Modules.Payroll.Domain/Calculation/PayrollCalculationInput.cs`, `PayrollLegislationSnapshot.cs`
- `src/Modules/Payroll/Sense.Modules.Payroll.Application/Periods/PayrollPeriodCalculationService.cs`
- `src/Modules/Payroll/Sense.Modules.Payroll.Application/Ports/IExpensePayoutReader.cs`
- `tests/Modules/Sense.Modules.Payroll.Tests/PayrollCalculatorGoldenSetTests.cs`
- `docs/adr/0008-legislation-parameter.md`, `docs/implementation/faz7-payroll-export-sozlesmesi.md`

---
*Kaynak: 2. Tur Test Bulgu Dokümanı § G-01 Aşama 2–3. Bağımlı: senseik-009, senseik-010, senseik-012, senseik-014. Agent board: `docs/agent-board/cards/senseik-019-bordro-tam.json`.*
