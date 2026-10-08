# TK-bordro-bos-durum — handoff

Bordro boş durumları: yönlendirici metin + "Dönem aç" / "Hesapla" eylemleri + ilgili ekran bağlantıları (yalnız web, API değişmedi).

## Değişen dosyalar (web/)
- `src/services/payroll.service.ts` — `openPayrollPeriod(year, month)` (POST `/v1/payroll/periods`), `runPayrollPeriod(id)` (POST `/periods/{id}/run`), `PayrollPeriodRunSummary` tipi (`PayrollPeriodRunSummary` C# kaydıyla aynı alanlar).
- `src/pages/payroll/periods.tsx` — başlık yanında her zaman görünen "Dönem aç" düğmesi; boş durum hem DataTable `emptyMessage` hem dar ekran dalında `PeriodsEmptyState`.
- `src/pages/payroll/periods-empty-state.tsx` (yeni) — açıklama, "Dönem aç", /app/employees bağlantısı, `payroll.payslip.import` varsa /app/payroll/payslip-import bağlantısı.
- `src/pages/payroll/period-open-modal.tsx` (yeni) — Mantine Modal (NumberInput yıl + Select ay, varsayılan içinde bulunulan ay); başarıda `["payroll","periods"]` invalidate + `/app/payroll/periods/<id>`'ye yönlendirme; hata modal içinde `getApiErrorMessage`.
- `src/pages/payroll/period-detail.tsx` — satır yokken `PeriodEmptyState`.
- `src/pages/payroll/period-empty-state.tsx` (yeni) — açıklama; dönem `open` iken "Hesapla" (başarıda sorgu invalidate + toast: "N çalışan için bordro hesaplandı, M atlandı"), değilken bilgi metni; Çalışanlar bağlantısı.
- `public/locales/{tr,en,de}/payroll.json` — `periods.openAction/emptyState/openDialog`, `periodDetail.emptyState/run` (üç dilde). Eski `periods.empty` anahtarı korundu (artık kullanılmıyor).

## Testler (yeni)
- `src/pages/payroll/periods.test.tsx` (8): boş listede yönlendirme metni + bağlantılar; import yetkisi yokken PDF bağlantısı gizli; modal yıl/ay (2027/Mart) ile `openPayrollPeriod`'u çağırır ve detaya yönlendirir; varsayılan ay; dolu listede başlık düğmesi; sunucu hatası modalda; aralık dışı yılda (2023, bu yıl+2) gönder düğmesi kapalı; modal her açılışta içinde bulunulan aya sıfırlanır. Yıl testi dinamik (bu yıl + 1).
- `src/pages/payroll/period-detail.test.tsx` (2): açık dönemde "Hesapla" `runPayrollPeriod("p1")` çağırır + toast sayıları; açık olmayan dönemde düğme yok.

## Sonuçlar
- `pnpm exec vitest run src/pages/payroll`: 3 dosya, 11 test geçti (inceleme düzeltmesi sonrası; tsc ve eslint src/pages/payroll temiz). Tam test kapısı düzeltme sonrası yeniden koşturulmadı (üstteki kapı çıktısı düzeltme öncesi ağaca aittir).
- `pnpm exec tsc --noEmit`: temiz. `pnpm exec eslint src`: 0 hata, 2 önceden var olan uyarı (custom-report-builder.tsx, employee-list.tsx; bu işle ilgisiz).
- Test kapısı: YEŞİL — dotnet build, web build, lint, type-check çıkış 0; koşan test 5762 (taban 3683); kanıt `.claude/takim/kanit/58097cab16ace38d.json`.

## Notlar / açık konular
- Maliyet merkezi filtresi seçiliyken satır yoksa da dönem detayında boş durum + "Hesapla" görünür (filtre sonucu boş olabilir); davranış zararsız, ayrıştırılmadı.
- İnceleme düzeltmesi: yıl aralığı API ile hizalandı (`PayrollLimits.MinPeriodYear` = 2024 .. bu yıl + 1); aralık dışında gönder kapalı. Form `Modal` içeriği olarak bağlandığı için (kapanınca söküldüğünden) yıl/ay her açılışta sıfırlanır.
- Riskli alan: yok. Commit yapılmadı.
