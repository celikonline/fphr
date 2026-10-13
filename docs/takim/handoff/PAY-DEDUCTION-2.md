# PAY-DEDUCTION-2: kesinti web arayüzü

Backend: PAY-DEDUCTION-1. Yeni kütüphane yok, migration yok.

## Yapılan
- `payroll-pay-items.service.ts`: `PayItemAssignment.direction?` ve `PayItemLine.direction?` eklendi (sona; eksikse earning). Tanım DTO'sunda `direction` zaten vardı; Create/Update isteği `direction` kabul ediyor (PayItemsController) ve form artık gerçek yönü gönderiyor.
- `pay-item-form.tsx`: yön seçimi açıldı (Kazanç/Kesinti). Düzenlemede mevcut yön `item.direction`'dan alınır ve PUT'ta gönderilir (kesinti sessizce kazanca dönmez). Kesintide vergi bayrakları + tutar türü gizlenir, açıklama (`deductionNote`) gösterilir; gövde: `direction: "deduction"`, `amountBasis: "gross"`, üç bayrak `false`. Kazanç gövdesi değişmedi. Düzenlemede yön alanında "atanmışsa değiştirilemez" ipucu; 409 `pay_item_direction_locked` mesajı mevcut `getApiErrorMessage` ile Alert'te görünür.
- `pay-items.tsx`: "Yön" sütunu + rozet; kesintide vergi/prim sütunu "—".
- `pay-item-direction.tsx` (yeni): `DirectionBadge` ve `usePayItemDirections` (atama DTO'sunda direction yoksa tanım listesinden id ile türetir, yoksa earning).
- `period-pay-items.tsx` + `employee-pay-items-card.tsx`: atama tablosunda "Yön" sütunu/rozeti. Hesaplanan kalemler tablosu: Yön sütunu, kesinti tutarı "−" önekli ve kırmızı (`data-testid="deduction-amount"`), kazançlar aynı. Toplam satırı yoktu, eklenmedi.
- `pay-item-assign-modal.tsx`: kesinti tanımları artık seçilebilir (eskiden yalnız earning filtreleniyordu); seçenek etiketine "(Kesinti)" eki; kesinti seçilince kırmızı ipucu.
- i18n tr/en/de `payroll.json`: `payItems.directionLockedHint`, `deductionNote`, `deductionAssignHint`, `directionColumn` payItems sonuna eklendi; `payItems.form.directionHint` metni ("yalnız kazanç" eskimişti) yerinde güncellendi (anahtar sırası değişmedi).

## Testler
- `pay-items.test.tsx`: eski "only offers the earning direction" testi yeni davranışa çevrildi (silinmedi) -> "offers earning and deduction directions"; yeni: kesinti seçilince kilit + gönderilen gövde, mevcut kesintide yön korunur/PUT'ta gider, liste rozeti + "—".
- `period-pay-items.test.tsx`: atama yönü (tanım listesinden / DTO'dan), kesinti satırı ayrı biçim + direction eksikse earning, modalda kesinti ipucu.
- `employee-pay-items-card.test.tsx`: yön rozeti.
- Vitest payroll+employees: 151/151 yeşil; `tsc --noEmit`, eslint temiz.

## Açık konular
- Atamalı tanımın yönünü istemci bilmez; 409 sunucudan gelir (kabul edilen tasarım).
- `period-pay-items.tsx` / test dosyası CRLF; korundu.
- Çalışma ağacındaki `faz3-durum-takibi.md` ve `PayrollPeriodCalculationService.cs` değişiklikleri bu işe ait değil (önceden vardı).
