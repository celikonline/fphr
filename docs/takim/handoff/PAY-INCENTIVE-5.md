# PAY-INCENTIVE-5 handoff: web teşvik atama + geçiş sihirbazı + etiketler

## Ne yapıldı
- `services/payroll-incentives.service.ts` (SONA ekleme): `IncentiveAssignment` (IncentiveAssignmentDto), list(employeeId|periodId)/create(toplu)/update/delete, `LegacyProfileCode` + `listLegacyProfileCodes(asOf?)` + `convertLegacyProfileCode`. Opsiyonel alanlar `T | null | undefined`. Not: `PUT /incentive-assignments/{id}` notu her zaman üzerine yazar; düzenleme modalı mevcut notu geri gönderir.
- `services/payroll.service.ts`: `IncentiveSummaryItem`'a `name?`, `source?`, `isSmmmApproved?` (dilim 3 DTO'su).
- `components/employees/employee-incentives-card.tsx`: çalışan kartı (trace.view altında salt okunur; manage ile ekle / bitiş ayı düzenle / sil). Onaysız tanım atamasında "Hesaba dahil edilmez, yalnız bildirge kodu yazılır" ipucu (kind d hariç). `payroll-section.tsx`: kart satırı + profilde `incentiveLawCode` yanında "Eski alan, hesapta kullanılmıyor" ipucu. Atama DTO'su tanımın `isActive`, `validFrom`, `validTo` alanlarını da döndürür; ipucu atamanın ay kapsamı ve tanımın aktiflik/geçerlilik koşullarını hesapla aynı şekilde kontrol eder.
- `pages/payroll/incentive-assign-modal.tsx`: "Teşvik ata" (aktif tanım, EmployeePicker ile ≤500 çalışan, başlangıç/bitiş ay, not; MaxMonths varsa bitiş zorunlu; onaysız tanım ve "tanım geçerliliği seçilen aydan sonra başlıyor" uyarıları; sunucu hataları `getApiErrorMessage`), bitiş düzenleme ve silme onayı. Düğme çalışan kartında, teşvikler sayfasında ve dönem detayında kullanılır.
- `pages/payroll/period-incentives.tsx`: dönem detayında o aya uzanan atamaları listeler; bordro dönemi açıksa `payroll.incentive.manage` yetkisi olan kullanıcı atama ekleyebilir, bitiş ayını değiştirebilir veya atamayı silebilir. Kapalı dönem salt okunur kalır.
- `pages/payroll/incentive-legacy-convert.tsx` + `incentives.tsx` sekmesi "Eski profil kodları" (yalnız `payroll.incentive.manage`): önizleme tablosu, 5510/05510 birlikte dönüşür notu (genel not + satır notu), `calculationChanges` KIRMIZI uyarı + zorunlu onay kutusu (seçilen tanım önerilenden farklıysa istemci kuralı: onaylı ve kind a değilse değişir), başlangıç ayı önerisi = ilk açık dönem (yoksa bugünkü ay), sonuç toast'u (oluşan / atlanan sayısı). Dönüştürme yalnız SGK kategorili (vergi dışı) tanımları sunar.
- `company-profile.tsx`: kaynak `assignment` ve `isSmmmApproved === false` rozetine "onaysız" etiketi; anahtar `source-code`.
- i18n: `payroll.json` tr/en/de sonuna `incentiveAssignments`, `incentiveLegacy` blokları; `dirtyReasons.incentive_changed` (EKSİKTİ, eklendi); `company.json` sonuna `incentiveUnapproved`. Kontrol: warnings'te `incentive_not_approved/duplicate_assignment/profile_code_ignored` ve skip `incentive_parameter_missing` zaten vardı.

## Testler (yeni 26 + company-profile +1)
`employee-incentives-card.test.tsx` (8), `incentive-assign-modal.test.tsx` (7: 2 çalışanla toplu atama, sunucu hata mesajı, onaysız uyarısı, MaxMonths bitiş zorunluluğu + geçerlilik uyarısı), `incentive-legacy-convert.test.tsx` (7: kırmızı uyarı + onay şartı, 5510/05510 notu, sonuç bildirimi, ilk açık ay önerisi), `company-profile.test.tsx` (+1 onaysız rozeti). tsc ve eslint temiz.

## Açık konular
- Profil kartındaki "eski alan" ipucu için ayrı render testi yok (payroll-section bağımlılıkları ağır); mantık `hasEffectiveSgkAssignment` birim testinde.
- Dönem bağlamında (açık dönem ekranı) atama butonu eklenmedi (karar gereği); dönem ekranında atamalar salt okunur gösterilir.
- Riskli alan: yok.
