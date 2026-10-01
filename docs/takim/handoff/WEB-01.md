# WEB-01 — çalışma kaydı

## 2026-10-01 ilk teslim

- Kullanıcının SensePROJECT pano görseli referans alındı. SenseİK renkleri korundu; uygulama kabuğu beyaz, kenarlıklı ve kompakt düzene geçirildi.
- `/app` rol ve gerçek veriye bağlı odak eylemi, iş akışı ve şirket bağlamı bölümleriyle düzenlendi. Var olan widget izin/modül kapıları korundu.
- Giriş, parola sıfırlama, davet ve ilk kurulumda büyük gradyan ve yuvarlak kart dili kaldırıldı.
- Ayrı Docker önizlemesi: `http://127.0.0.1:13004/app` (`senseik-web-preview`, yalnız loopback).
- İlgili 22 kabuk/ana sayfa testi ve 13 kimlik/kurulum testi geçti. TypeScript, ESLint (0 hata, 1 mevcut uyarı), Vite ve Docker build geçti. 375/768/masaüstü görsel denetiminde yatay taşma bulunmadı.
- Proje çapı `test-kapisi.mjs` koşusu 15 dakikayı aşmasına ve sonuç üretmemesine rağmen süreç etkin kaldı; koşu durduruldu. Yeşil kapı kanıtı henüz yok.

## Kalan işler

- Liste, detay, form, ayarlar ve özel ekran ailelerini aynı düz şablonla bitir.
- Proje test kapısını tamamlayıp geçerli kanıt al.
- Bağımsız gözden geçiriciye kabul ölçütlerini doğrulat.
