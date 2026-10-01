# WEB-01 — çalışma kaydı

## 2026-10-01 ilk teslim

- Kullanıcının SensePROJECT pano görseli referans alındı. SenseİK renkleri korundu; uygulama kabuğu beyaz, kenarlıklı ve kompakt düzene geçirildi.
- `/app` rol ve gerçek veriye bağlı odak eylemi, iş akışı ve şirket bağlamı bölümleriyle düzenlendi. Var olan widget izin/modül kapıları korundu.
- Kullanıcı geri bildirimiyle girişin sol marka panelindeki mor-mavi gradyan, nokta dokusu ve arka plan şekilleri korundu. Form, parola sıfırlama, davet ve ilk kurulum düz yüzey düzeninde kaldı.
- Ayrı Docker önizlemesi: `http://127.0.0.1:13008/app` (`senseik-web-preview`, yalnız loopback).
- İlgili 22 kabuk/ana sayfa testi ve 13 kimlik/kurulum testi geçti. TypeScript, ESLint (0 hata, 1 mevcut uyarı), Vite ve Docker build geçti. 375/768/masaüstü görsel denetiminde yatay taşma bulunmadı.
- Proje çapı `test-kapisi.mjs` koşusu 15 dakikayı aşmasına ve sonuç üretmemesine rağmen süreç etkin kaldı; koşu durduruldu. Yeşil kapı kanıtı henüz yok.
- Kullanıcının ek görsel kararı sonrası giriş panelinin önceki CSS gradyanı, nokta dokusu ve şekilleri geri alındı. `login.test.tsx` 5/5, TypeScript, ilgili ESLint ve Docker build geçti; `/login` Docker önizlemesinde görsel olarak doğrulandı.
- Sol menü ikonları, Uygulamalar menüsüyle aynı `navIconColor` kaynağını kullanıyor. Koyu temada iki menü de okunabilir açık tonu seçiyor. İlgili 13 test, TypeScript, ilgili ESLint ve Docker build geçti; iki menü `/app` önizlemesinde karşılaştırıldı.
- Kullanıcı geri bildirimiyle ana sayfanın sabit 2:1 bölüm ve iş akışı alt sütunları kaldırıldı. İş ve şirket kartları kendi tam genişlikli bölümlerinde içerik yüksekliğine göre dengeli akıyor. İlgili 11 test, TypeScript, ESLint ve Docker build geçti; 1280 px önizlemede sol/orta/sağ kart konumları ve yatay taşma doğrulandı.

## Kalan işler

- Liste, detay, form, ayarlar ve özel ekran ailelerini aynı düz şablonla bitir.
- Proje test kapısını tamamlayıp geçerli kanıt al.
- Bağımsız gözden geçiriciye kabul ölçütlerini doğrulat.
