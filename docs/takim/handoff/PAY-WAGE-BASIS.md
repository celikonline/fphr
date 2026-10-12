# PAY-WAGE-BASIS: günlük ve saatlik ücretli bordro hesabı

Dal: `takim/bordro-gunluk-saatlik-ucret`. Riskli alan: para (bordro hesabı) - birleştirme öncesi insan onayı ve SMMM kontrolü gerekir.

## Yapılanlar

- `PayrollPeriodCalculationService.cs`: Daily ve Hourly profil artık atlanmıyor; Weekly/Yearly `wage_basis_not_monthly` ile atlanmaya devam ediyor. Maaş kaydının `PayFrequency` değeri tutarın anlamını belirler (Monthly/Daily/Hourly); profil `WageBasis` ile uyuşmazlık `wage_basis_mismatch` ile atlanır (Monthly profil + Weekly/Yearly maaş kaydı da artık bu nedenle atlanır, eskiden sessizce aylık sayılıyordu). Net ücretli günlük/saatlik çalışan `net_wage_basis_unsupported` ile atlanır.
- Ücretli gün = `paidDays30` - eksik gün (PDKS varsa `MissingDays`, yoksa Leave mazeretsiz devamsızlık), alt sınır 0; 0 ise `no_worked_days`.
- `Domain/Calculation/PeriodWage.cs` (yeni): `PeriodWage` kaydı ve `PeriodWageMath` (`DailyWorkingHours = 7.5`, `MonthlyWorkingHours = 225`, aylık karşılık, dönem brütü).
- `PayrollCalculationInput.Wage` (yeni, opsiyonel): doluysa `PayrollCalculator` brütü `PeriodGross` ile bulur ve ikinci kez oranlamaz; `ProratedPaidDays` yine SGK taban/tavan ve asgari ücret istisnasını ölçekler (servis bunu ücretli gün 30'dan azsa o gün sayısıyla doldurur). EARNINGS izine `wageBasis`, `unitRate`, `paidDays`, `paidHours` eklendi.
- Fazla mesai, tatil ücreti ve sendika aidatı aylık karşılıkla (günlük x 30, saatlik x 225) hesaplanır.
- Çeviri: `SharedResource{,.en}.resx` (`payroll.skip.wage_basis_mismatch`, `payroll.skip.net_wage_basis_unsupported`); web `payroll.json` tr/en/de `preview.skipReasons.*`. Web'de atlama nedenlerini gösteren bir yer şu an yok (yalnız sayı gösteriliyor), bu yüzden yalnız anahtar eklendi.
- `faz3-durum-takibi.md` değişiklik geçmişine not eklendi.

## Basitleştirmeler / SMMM'ye sorulacaklar

- Saatlik = saat ücreti x 7,5 x ücretli gün (aylık 225 saat); PDKS gerçek çalışılan saat kullanılmıyor.
- Ücretli gün kuralı: kıst günü - eksik gün. PDKS `MissingDays` kısmi ayda işe girişten önceki günleri de içeriyorsa çift düşüm olabilir (PDKS tarafında doğrulanmadı).
- Günlük/saatlik çalışanda hafta tatili/resmî tatil ücretin içinde sayılıyor (30 gün esası).
- Net günlük/saatlik net→brüt çözümü yapılmıyor.
- Haftalık ve yıllık kapsam dışı.

## Testler

`tests/Modules/Sense.Modules.Payroll.Tests/PayrollPeriodCalculationServiceTests.cs` sonunda yeni testler: tam ay günlük, tam ay saatlik, ay ortası giriş günlük (çift oranlama yok + SGK tavanı 112500), devamsızlıklı günlük, PDKS'li saatlik, tüm gün eksik, Weekly/Yearly atlama, uyuşmazlık (3 durum), net günlük, günlük fazla mesai, `PeriodWageMath`.

## Kapı sonucu

Aşağıda ana rapora bakın.
## Düzeltme: kısmi ayda PDKS eksik günlerinin çift düşümü

- Sorun: `AttendanceMonthlySummaryReader` ayın tamamına bakıyordu; işe giriş öncesi iş günleri `MissingDays`'e girip `paidDays30`'dan ikinci kez düşülüyordu.
- `IAttendanceMonthlySummaryReader.GetAsync` ve `IWorkedTimeReader.GetAsync`'e opsiyonel `DateOnly? from, DateOnly? to` eklendi (CA1068 yüzünden `CancellationToken`'dan önce; null/null = tam ay, eski davranış). Pencere ayın içine kırpılır; satırlar, çalışma günleri, tatil/hafta tatili sayıları ve kilit sorgusu pencereye göre hesaplanır (`AttendanceMonthlySummaryReader.cs`).
- `AttendanceWorkedTimeReaderAdapter` pencereyi iletir. `PayrollPeriodCalculationService` yalnız kısmi ayda `EmploymentProration.EffectiveStart/EffectiveEnd` penceresini geçer; tam ayda pencere verilmez (`EffectiveEnd` yeni eklendi).
- Mevcut çağıranlar (Analytics, Attendance onayı) değişmedi; yalnız imza sırası yüzünden `cancellationToken:` adlı argümana çevrildi. Mevcut testlerde yalnız `null, null` argümanları eklendi.
- Yeni testler: Attendance `Monthly_summary_with_a_window_ignores_working_days_outside_it`; Payroll kısmi ay + PDKS (15-2=13 gün), tam ayda pencere yok, kısmi ay + Leave devamsızlığı, kısmi ay + prim, günlük için tatil ücreti, günlük için sendika aidatı.
- Not: Leave mazeretsiz devamsızlık okuyucusu hâlâ ayın tamamına bakıyor; işe giriş öncesi devamsızlık kaydı olmayacağı varsayıldı.


## Kararlar (2026-10-13, kullanıcı kararları ana oturuma bıraktı)

Gözden geçirici "İNSAN ONAYI GEREKLİ" dedi; kullanıcı kararları ana oturuma devretti ve `dev`'e birleştirme talimatı verdi.

1. **Profil Monthly + maaş kaydı Daily/Hourly/Weekly/Yearly → `wage_basis_mismatch` ile atlanır (korunur).** Eski davranış bu tutarı aylık brüt sayıyordu (yıllık maaş girilmişse 12 kat ödeme). Yanlış bordro üretmektense atlamak daha güvenli. Bilinen eksik: web atlama nedenini değil yalnız sayısını gösteriyor (ayrı iş).
2. **Kıst ayındaki aylık çalışanda PDKS penceresi kabul edildi.** İşe giriş öncesi / çıkış sonrası AttendanceDay satırları WorkedDays'e ve tatil/hafta tatili sayılarına girmez; istihdam dışı günler için mesai/tatil ücreti doğmamalı.
3. **225 saat (7,5 saat × 30 gün) esası korunur.** 4857 m.63 haftalık 45 saat → günlük 7,5 saat; aylık 225 saat yerleşik uygulama.
4. **SGK taban/tavanı ve asgari ücret istisnasının ücretli güne göre ölçeklenmesi korunur.** 5510 m.82'de alt/üst sınır günlük tutar × prim günüdür; eksik gün prim gününü düşürür.
5. **Leave mazeretsiz devamsızlığı tam ay okunmaya devam eder.** İşe giriş öncesi/çıkış sonrası tarihe devamsızlık kaydı girilmesi beklenmiyor; düşük risk, kabul edildi.

SMMM'nin yine de gözden geçirmesi önerilir (3-5), ama birleştirmeyi bekletmez.

## Ortam notu

Kapının pnpm adımları ilk koşularda pnpm 12.4.1'in yerel ikilisi kurulmadığı için düşüyordu; paketin kendi `install.js`'i çalıştırılarak onarıldı. Repo Node 24 bekliyor (`.nvmrc`), makinede yok; kapı Node 22 ile koşturuldu (varsayılan Node 20 ile vitest başlamıyor).
