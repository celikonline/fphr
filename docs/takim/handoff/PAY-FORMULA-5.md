# PAY-FORMULA-5 handoff: web atama ve dönem dökümü

## Ne yapıldı
- `web/src/services/payroll-pay-items.service.ts` (sona ekleme): `listPayItemAssignments({employeeId}|{periodId})`, `createPayItemAssignments`, `updatePayItemAssignment`, `deletePayItemAssignment`, `listPeriodPayItemLines` + tipler (`PayItemAssignment`, `PayItemLine`...). Opsiyonel alanlar `T | null | undefined` (missing = null).
- `pages/payroll/pay-item-assign-modal.tsx`: paylaşılan "Ödeme türü ata" modalı (aktif kazanç tanımlarından seçim, değer opsiyonel + tanım varsayılanı ipucu, başlangıç/bitiş ayı `<input type="month">`), çok çalışan: `EmployeePicker` ile tek tek ekleyerek liste (<=500; EmployeeMultiPicker "tümünü seç" modu id üretmediği için kullanılmadı), `fixedEmployee` ile çalışan kartında tek çalışan; silme onay modalı; sunucu hata mesajı `getApiErrorMessage`.
- `pages/payroll/period-pay-items.tsx` + `period-detail.tsx` (PeriodAdjustments'ın altına tek satır): atama kartı (liste, düzenle = değer/bitiş ayı PUT, sil = onaylı DELETE) ve "Hesaplanan ödeme türü kalemleri" kartı (`periods/{id}/pay-item-lines`, boşsa açıklayıcı metin). Düğmeler yalnız `status === "open"` ve `payroll.payitem.manage` ile görünür.
- `components/employees/employee-pay-items-card.tsx` + `payroll-section.tsx` (tek satır): çalışanın atamaları (`?employeeId=`); yönetim izniyle ekle/sil (modal paylaşılır).
- `period-preview.tsx`: deneme bordrosu yanıtındaki `skipped[].reason` ve `warnings[].code` çevrilmiş etiketlerle (mevcut `preview.skipReasons`, `warnings` blokları) sayılarıyla listelenir; bilinmeyen kod ham gösterilir.
- `payroll.json` tr/en/de sonuna `assignments`, `payItemLines`, `previewDetails` blokları. `skipReasons` (pay_item_formula_failed, net_wage_basis_unsupported, wage_basis_mismatch), `warnings` (pay_item_negative_result, pay_item_duplicate_assignment), `dirtyReasons.pay_item_changed` PAY-FORMULA-3'ten zaten vardı.

## Testler
`period-pay-items.test.tsx` (10), `employee-pay-items-card.test.tsx` (4), `period-preview.test.tsx` (1). i18n eşlik testleri yeşil.

## Açık konular
- Atama kartı `skipped`/`warnings` listesinde çalışan adı yok (yanıt yalnız employeeId taşıyor); nedenler çalışan sayısıyla gruplanır.
- `dirtyReasons.pay_item_changed` etiketini gösteren bir ekran bu işte yok (mevcut web'de dirty reason gösterimi bulunmadı); etiket hazır.
- Toplu atamada "filtreyle tümünü seç" yok (sunucu id listesi ister, en çok 500).
- Ay alanları yerel `<input type="month">`.
- Riskli alan: yok.
