# WEB-01 — çalışma kaydı

## 2026-10-01 ilk teslim

- Kullanıcının SensePROJECT pano görseli referans alındı. SenseİK renkleri korundu; uygulama kabuğu beyaz, kenarlıklı ve kompakt düzene geçirildi.
- `/app` rol ve gerçek veriye bağlı odak eylemi, iş akışı ve şirket bağlamı bölümleriyle düzenlendi. Var olan widget izin/modül kapıları korundu.
- Kullanıcı geri bildirimiyle girişin sol marka panelindeki mor-mavi gradyan, nokta dokusu ve arka plan şekilleri korundu. Form, parola sıfırlama, davet ve ilk kurulum düz yüzey düzeninde kaldı.
- Ayrı Docker önizlemesi: `http://127.0.0.1:13005/app` (`senseik-web-preview`, yalnız loopback).
- İlgili 22 kabuk/ana sayfa testi ve 13 kimlik/kurulum testi geçti. TypeScript, ESLint (0 hata, 1 mevcut uyarı), Vite ve Docker build geçti. 375/768/masaüstü görsel denetiminde yatay taşma bulunmadı.
- Proje çapı `test-kapisi.mjs` koşusu 15 dakikayı aşmasına ve sonuç üretmemesine rağmen süreç etkin kaldı; koşu durduruldu. Yeşil kapı kanıtı henüz yok.
- Kullanıcının ek görsel kararı sonrası giriş panelinin önceki CSS gradyanı, nokta dokusu ve şekilleri geri alındı. `login.test.tsx` 5/5, TypeScript, ilgili ESLint ve Docker build geçti; `/login` Docker önizlemesinde görsel olarak doğrulandı.

## Kalan işler

- Liste, detay, form, ayarlar ve özel ekran ailelerini aynı düz şablonla bitir.
- Proje test kapısını tamamlayıp geçerli kanıt al.
- Bağımsız gözden geçiriciye kabul ölçütlerini doğrulat.
