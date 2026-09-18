# senseik-011 + senseik-013 — Tazminat Hesabı + Ücret Kütüğü (Bordro Aşama 1) — UYGULAMA PLANI

## Implementasyon Notu

**Durum: GATE 1/GATE 2 onaylandı, her iki kart da uygulandı (dallar: `a/payroll-salary-registry`, `a/exit-severance-calc`).**

- **Kart 013** (`a/payroll-salary-registry`, tek commit): `SalaryRecordData`/`SalaryRecord`'a opsiyonel `OrgUnitId`
  eklendi (yeni Employee migration'ı `AddSalaryRecordOrgUnit`, `employee.salary_records.org_unit_id` nullable
  kolon); `AddSalaryRecordCommand`/`UpdateSalaryRecordCommand`/DTO'lar bu alanı taşıyor. `BankAccountType` kod
  değişikliği yapılmadı - plandaki karar (`EmployeePayrollProfile.BankAccountId` zaten "hangi hesaba maaş"
  sorusunu çözüyor) doğrulandı. `EmployeeMasterImportTemplate`'e banka bloğu eklendi
  (`bankIban`/`bankAccountType`/`bankName`/`bankCurrency`), maaş bloğuyla simetrik bütünlük kuralıyla (IBAN
  doluysa hesap türü zorunlu, IBAN `BankAccount.ParseIban` ile doğrulanır). Bu arada bir mevcut boşluk da
  düzeltildi: `IEmployeeRepository.ListByIdsWithPaymentsAndCustomFieldsAsync` adına rağmen `BankAccounts`
  koleksiyonunu yüklemiyordu (yalnızca `SalaryRecords`); artık üçü de yükleniyor, aksi halde ithalat satır
  işleyicisi güncelleme satırlarında banka hesabı ekleyemezdi.
- **Kart 011** (`a/exit-severance-calc`, 013 commit'inin üstünde): planın §4 imzalarıyla birebir -
  `Employee.Contracts.IEmployeeSeniorityLookup` (yeni, `EmployeeSeniorityPurpose` mirror'lanmış enum) ve
  `Leave.Contracts.ILeaveBalanceLookup` (yeni, `LeaveBalanceService.ProjectAsync`'i sarmalıyor, Annual-coded
  tiplerin `CurrentBalance` toplamı) eklendi - ikisi de API-only, Sense.Worker'a kayıt gerekmiyor (arka plan
  tüketicisi yok). `Payroll.Domain.Calculation.SeveranceCalculator` (`IncomeTaxMath`/`PayrollCalculator` ile
  aynı stil: statik, saf, `TraceStep` izlenebilir) yeni bir `SeveranceCauseCategory` enum'u kullanıyor -
  `Employee.Domain.Employees.TerminationType`'tan daha ince taneli, çünkü İş K. m.25/II (ahlak/iyiniyet -
  kıdem ve ihbarın ikisini de sıfırlayan tek sebep) o enumda ayrı bir değer değil. Web tarafı bu farkı
  `lifecycle-actions.tsx`'teki `mapCauseCategory` ile kapatıyor: sihirbazın seçtiği `TerminationType`'ı
  `SeveranceCauseCategory`'ye eşliyor, yalnızca `employerJustCause_Art25` seçiliyken panelin kendi (tahmine
  özel, komutu etkilemeyen) checkbox'ıyla ahlak/iyiniyet ayrımını soruyor. Yeni legislation parametreleri
  `Severance.CeilingGross` (55.000,00 TL taslak) ve `Notice.WeeksBySeniorityTiers` (JSON dizi, İş K. m.17
  eşikleri: 183/548/1095 gün) `LegislationParameterSeed.All`'a (id 40-41) eklendi ve gerçek bir EF migration'la
  (`SeveranceLegislationParameters`, `dotnet ef migrations add` ile Docker SDK konteynerinde üretildi) seed'lendi
  - ADR 0008 desenine göre. Yeni salt-okunur query `GetSeveranceEstimateQuery` + `GetSeveranceEstimateHandler`
  (`Sense.Modules.Payroll.Application.Severance`), yeni `payroll.severance.estimate` izni (HR/PayrollSpecialist
  rollerine `payroll.*` joker karakteriyle zaten ulaşıyor, `SystemRoleDefinitions.cs`'e ayrı satır eklenmedi),
  yeni `GET /api/v{version}/payroll/employees/{employeeId}/severance-estimate` endpoint'i
  (`SeveranceEstimateController`). Günlük brüt ücret, çalışanın güncel maaş kaydından (`IEmployeeSalaryLookup`)
  `PayFrequency`'ye göre türetiliyor; **TODO**: Net tipi maaş kayıtları net-brüt dönüşümü yapılmadan olduğu gibi
  kullanılıyor (bu dilimin kapsamı dışında, kod içinde işaretli). Web: `SeveranceEstimatePanel`
  (`lifecycle-actions.tsx`) + `payroll-severance.service.ts`; mevcut `severancePaid`/`leaveSettledAtExit`
  checkbox'larına dokunulmadı, panel adım 1'in altına, o checkbox'ların hemen üstüne eklendi. Golden-set: 10 test
  (`SeveranceCalculatorGoldenSetTests.cs`, plan §8'deki 8 senaryo + adım sırası + bilinmeyen kategori testi),
  tamamı yeşil; ayrıca tüm Payroll modülü test paketi (60 test) yeşil.
- **Doğrulanmadı/kapsam dışı (plandaki gibi kaldı)**: kötüniyet/sendikal tazminat hesaba katılmadı; kıdem
  tazminatı tavanı değeri taslak (mali müşavirle doğrulanmalı, `PayrollDraft2026` çekincesiyle aynı); denetim
  kaydı/saklama eklenmedi (tahmin yalnızca ekranda, kalıcı kayıt yok - risk #6'daki karar).

---

**Durum: GATE 1 onayı bekliyor. Hiçbir dosya değiştirilmedi/oluşturulmadı.**

## ⚠️ Kart Metni Güncel Değil — Kritik Düzeltme

Kartlar ("hiçbir bordro fonksiyonalitesi çalışmıyor", "maaş kaydı hiç yok") **doğru değil**. Gerçek durum:

- **Payroll modülü iskelet DEĞİL** — `PayrollDbContext` (2 migration), `EmployeePayrollProfile` aggregate+CRUD API, `PayrollPeriod`/`Payslip`+hesap motoru, golden-set testleri; `ModuleCatalog.cs`/`Sense.Migrator`/`Sense.Worker`/`Sense.slnx`'te tam kayıtlı ve çalışıyor.
- **Kart 013'ün büyük kısmı ZATEN TESLİM EDİLMİŞ** — ama Payroll'da değil, **Employee modülünde**: `SalaryRecord`/`BankAccount` entity'leri (`Employee.Payments.cs`), tam CRUD API, tam web UI ("Ödemeler" sekmesi), migration, import şablonunda maaş bloğu (7 alan) zaten var.
- **Kart 011'in kıdem alt yapısı zaten var** — `Employee.EmploymentPeriods.cs`: `SeniorityPurpose.{Leave,Severance,JobSecurity}`, `CountedSeniorityDays` (kariyer ekranındaki "2485 gün" buradan geliyor), `SeverancePaid`/`LeaveSettledAtExit` bayrakları, `JobSecurityEvaluator`. **Ama hiçbir yerde TL tutarı hesaplanmıyor** — kart bu noktada doğru.

**Gerçek boşluklar (plan bunlara odaklanıyor):**
1. Payroll'da parasal kıdem/ihbar/izin-ücreti hesap motoru yok.
2. `EmployeeMasterImportTemplate`'te banka hesabı alanları YOK (sadece maaş bloğu var).
3. Maaşın lokasyon/pozisyon bazlı atanması yok.
4. Demo seed'de maaş/banka verisi yok.
5. Kıdem tazminatı tavanı/ihbar hafta tablosu legislation parametresi yok.
6. Leave.Contracts'ta "kalan izin bakizesi" portu yok (sadece yıllık kullanım/aylık puantaj var).

## Modül Sınırı Kararı (değiştirilmeyecek)

`SalaryRecord`/`BankAccount` **Employee** modülünde kalır (çekirdek kişi verisi). Payroll bunlara `IEmployeeSalaryLookup` (zaten var) üzerinden salt-okunur erişir. `EmployeePayrollProfile` Payroll'da kalır (hesaplama kuralı seçimi, tutar değil). Severance/ihbar/izin-ücreti motoru **Payroll.Domain**'de yaşamalı (mevzuat parametrelerine bağımlı).

## Plan

**Yeni entity GEREKSİZ** — `SalaryRecord`/`BankAccount` zaten var. Sadece: `SalaryRecordData`'ya opsiyonel `OrgUnitId` (lokasyon ataması); `BankAccountType` (vadesiz/vadeli/çek) ile kartın istediği "kullanım amacı" (maaş/geri ödeme) arasındaki fark PO'ya sorulmalı (`EmployeePayrollProfile.BankAccountId` zaten "hangi hesaba maaş" sorusunu çözüyor).

**Hesap motoru** — `Sense.Modules.Payroll.Domain.Calculation.SeveranceCalculator` (yeni, `IncomeTaxMath.cs` deseni). Girdi: `SeveranceInput` (kıdem günleri, ihbar ay sayısı, brüt günlük ücret, tavan, kullanılmamış izin günü, fesih türü). Yeni cross-module portlar:
- `Employee.Contracts.IEmployeeSeniorityLookup` (yeni) — `SeniorityPurpose`/`CountedSeniorityDays` bugün Contracts'a sızmamış.
- `Leave.Contracts.ILeaveBalanceLookup` (yeni) — "kalan bakiye" portu yok, `LeaveBalanceService.ProjectAsync`'i sarmalar.

Yeni legislation parametreleri: `Severance.CeilingGross` (tarih aralıklı), `Notice.WeeksBySeniorityTiers` (İş K. m.17: 2/4/6/8 hafta).

**Tetikleme:** Employee, Payroll'a bağımlı olamaz (modül sınırı). Payroll tarafı salt-okunur query: `GetSeveranceEstimateQuery` — sadece tahmin, mutasyon yok, outbox gerekmez.

**Sihirbaz entegrasyonu:** Yeni endpoint `GET /payroll/employees/{id}/severance-estimate`, `TerminateDialog`'a salt-okunur özet blok (`SeveranceEstimatePanel`). Mevcut `severancePaid`/`leaveSettledAtExit` checkbox'larına dokunulmaz.

**İçe aktarım:** Banka bloğu eksik — `bankIban/bankAccountType/bankName/bankCurrency` (4 alan) eklenmeli, "IBAN doluysa bütünlük kuralı" (maaş bloğuyla simetrik).

**Migration:** Yeni modül kaydı YOK (Payroll zaten kayıtlı). Employee (OrgUnitId), Leave (severance/notice parametreleri seed), Payroll (muhtemelen migration gerekmez — saf hesaplama, denetim kaydı istenirse ayrı karar).

## Golden-Set Test Senaryoları (8 senaryo önerisi)

0.5 yıl (hak yok) · 1.5 yıl standart · 6 yıl · 12 yıl tavan üstü (kırpılır) · 3 yıl kesikli dönemler (2485 gün örneği) · haklı fesih-işçi (kıdem ödenir, ihbar ödenmez) · ahlak/iyiniyet feshi (ikisi de ödenmez) · kullanılmamış izin ücreti.

## Riskler / Açık Sorular

1. **Paralel worktree çakışması** — `.claude/worktrees/` altında 10+ aktif worktree var, bazıları aynı sıcak dosyalara (`ModuleCatalog.cs`, `Sense.slnx`) dokunuyor olabilir — başlamadan önce kontrol edilmeli.
2. **Kart metni ↔ kod tutarsızlığı** — PO/agent-board sahibine bildirilmeli, kart açıklaması güncellenmeli.
3. **BankAccountType semantiği** — "hesap türü" (kod) vs "kullanım amacı" (kart) — netleştirilmeli.
4. **Kötüniyet/sendikal tazminat** — kapsam dışı (kart da öyle diyor).
5. **Tavan parametresinin güncelliği** — 6 ayda bir değişir, seed verisi güncel tutulmalı.
6. **Denetim/saklama** — tahmin sadece ekranda mı, kayıt altına da alınacak mı — kapsam netleşmeden migration eklenmemeli.

## Kritik Dosyalar
- `src/Modules/Employee/Sense.Modules.Employee.Domain/Employees/Employee.EmploymentPeriods.cs`
- `src/Modules/Employee/Sense.Modules.Employee.Application/Employees/TerminateEmployee.cs`
- `src/Modules/Payroll/Sense.Modules.Payroll.Domain/Calculation/PayrollLegislationSnapshot.cs`
- `src/Shared/Sense.Shared.Contracts/Legislation/ILegislationParameterReader.cs`
- `src/Modules/Employee/Sense.Modules.Employee.Application/Import/EmployeeMasterImportTemplate.cs`
- `web/src/components/employees/lifecycle-actions.tsx`
- `src/Modules/Employee/Sense.Modules.Employee.Contracts/IEmployeeSalaryLookup.cs`
- `src/Modules/Leave/Sense.Modules.Leave.Application/Balances/LeaveBalanceService.cs`

---
*Kaynak: 2. Tur Test Bulgu Dokümanı § F-06, G-01 Aşama 1. Agent board: `docs/agent-board/cards/senseik-011-tazminat-hesap.json`, `senseik-013-ucreti-kutugu.json`.*
