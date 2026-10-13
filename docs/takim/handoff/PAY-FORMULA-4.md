# PAY-FORMULA-4 handoff: web ödeme türü tanımları ekranı

## Ne yapıldı
- `web/src/services/payroll-pay-items.service.ts`: tanım listesi/oluştur/güncelle/aktif-pasif, `formula-catalog`, `validate-formula` çağrıları ve tipleri (opsiyonel alanlar `T | null | undefined`; missing = null).
- `web/src/pages/payroll/pay-items.tsx`: liste (kod, ad, tutar türü, vergi bayrakları, durum), "Yeni ödeme türü", satır başına düzenle + aktif/pasif anahtarı, boş durum yönlendirmesi, "pasifleri göster". Sayfa `payroll.trace.view` veya `payroll.payitem.manage` ister; yazma düğmeleri yalnız `payroll.payitem.manage` ile görünür.
- `pay-item-form.tsx`: oluşturma/düzenleme modalı. Kod düzenlemede kilitli (PUT gövdesinde `code` yok). Yön yalnız "Kazanç" (Kesinti devre dışı + açıklama). Net seçilince SGK/GV/DV işaretli ve kilitli, gönderimde üçü true. Varsayılan değer, geçerlilik tarihleri (YYYY-MM-DD).
- `formula-editor.tsx`: metin alanı + katalogdan değişken/fonksiyon rozetleri (tıklayınca imleç konumuna, odakta değilse sona ekler) + "Doğrula" (katalog örnek değerleriyle `validate-formula`). Geçerliyse kullanılan değişkenler + örnek sonuç; geçersizse sunucu mesajı + 1 tabanlı konumda vurgulanan karakter. İstemcide ayrıştırıcı yok. Değişken açıklamaları `payItems.variables.<ad>`.
- Rota `payroll/pay-items` (`App.tsx`, sona), menü `payItems` (`config/navigation.ts`, izin `payroll.payitem.manage`, sona; `navigation.json` tr/en/de `apps.payItems`).
- i18n tr/en/de `payroll.json` sonuna `payItems` bloğu (14 değişken açıklaması dahil). Sunucu hata mesajları `getApiErrorMessage` ile (ProblemDetails `detail`).

## Testler
`pay-items.test.tsx` (10): liste, null alan toleransı, boş durum, oluşturma, düzenlemede kod kilidi, Net -> bayrak kilidi, yalnız kazanç, aktif/pasif, izinsiz kullanıcı yazma kontrollerini görmez (+ boş durumda). `formula-editor.test.tsx` (5): katalogdan ekleme, geçerli doğrulama + örnek değerler, hatalı + konum vurgusu, eksik alan toleransı, boş formül/düzenlemede sonuç temizleme. i18n eşlik testleri yeşil.

## Notlar / açık konular
- Formül alanı `autosize` kullanmaz (jsdom'da Mantine Autosize `document.fonts` yüzünden çöküyor); `rows={3}`.
- Menü girdisi `payroll.payitem.manage` ister (iş kartı); yalnız `payroll.trace.view` olan kullanıcı menüde göremez ama URL ile liste salt okunur açılır.
- Tarih alanları yerel `<input type="date">` (yeni kütüphane eklenmedi).
- Atama ve dönem dökümü ekranları PAY-FORMULA-5'te.
- Riskli alan: yok.
