# Kolay İK Video Bulguları ve Tasarım Kararları

Bu dizin, Kolay İK ürünü YouTube eğitim videolarından (89 video, 8 bölüm) çıkarılan yapılandırılmış bulguları ve SenseİK tasarım kararlarını içerir.

## Yöntem

1. **Video Transkriptleri**: YouTube otomatik altyazı + manuel düzeltme
2. **Bulgu Çıkarımı**: 8 bulgu ajanı tarafından part1–part8 parçalar halinde
3. **Parça Üretimi**: Modül başına ve sistemik temalar başına Türkçe plan parçaları
4. **Konsolidasyon**: Bu dizinde okuyucu dostu formatında

## Güvenilirlik Etiketleri

Her bulguda kaynağı belirtmek için dört etiket kullanılır:

- **[Ekranda]**: Kolay İK ürün eğitim videolarında doğrudan gösterildi
- **[Müşteri]**: Müşteri sohbeti veya podcast beyanı; pratik uygulamadan türetilmiş
- **[Mevzuat]**: İş hukuku (4857, TİS, vb.) veya yasal metinlerden türetilmiş
- **[Aday]**: SenseİK tasarımı için aday özellik; transkriptte doğrudan bulunmuyor veya doğrulanması gerekiyor

## Dizin Yapısı

```
docs/analysis/kolayik-video/
├── README.md (bu dosya)
├── 11.2-video-envanteri.md (transkript metajson ve kapsamı)
├── 11.3.01-kimlik-ve-erisim.md
├── 11.3.02-sirket-yapisi.md
├── 11.3.03-ozluk.md
├── 11.3.04-onay-surecleri.md
├── 11.3.05-izin-ve-fazla-mesai.md
├── 11.3.06-pdks-puantaj-vardir-takvim.md
├── 11.3.07-bordro.md
├── 11.3.08-harcama-avans-odemeler-zimmet.md
├── 11.3.09-dosya-belge-kvkk-vize.md
├── 11.3.10-performans.md
├── 11.3.11-ats-ishe-alim.md
├── 11.3.12-raporlama-analitik.md
├── 11.3.13-bildirim-ana-sayfa-duyuru-destek-ui.md
├── 11.3.14-egitim-onboarding-anket-ucret-faturalama.md
├── 11.3.15-veri-aktarim-merkezi.md
├── 11.4-terminoloji.md (alfabetik sözlük)
├── 11.5-acik-noktalar.md (belirsiz/doğrulanması gereken noktalar)
├── 11.6-mevzuat-sorulari.md (iş hukuku soru-cevapları)
├── 11.7-yol-haritasi.md (faz ve tesisler)
└── 11.8-tutarlilik-kararlari.md (çelişi çözmek için mimari kararlar)
```

## Her Bölümün İçeriği

Her bölüm (11.2–11.7) iki ana parçadan oluşur:

### 1. Plan Kararları (PLAN-BLOCK)

Tasarımı değiştiren veya genişleten öğeler:
- Yeni entity/alan
- Yeni kiracı ayarı
- Yeni permission anahtarı
- Yeni iş kuralı
- Seed verisi

Her madde **YENİ** / **FARKLI** / **DETAY** ile başlar ve güvenilirlik etiketi taşır.

### 2. Ayrıntılı Bulgular (B11)

Modül tasarımının tam açıklaması:
- Ekranlar ve menüler
- Alanlar ve veri yapısı
- İş kuralları ve hesaplamalar
- Akışlar ve süreçler
- Roller ve yetkiler
- SenseİK tasarım kararı (entity adları, permission biçimi, kiracı ayarları, seed değerleri)
- Açık noktalar (bu bölümde belirsiz olan şeyler, 11.5 konsolide listesine de dahil)

## Bölüm 11.8 — Tutarlılık Kararları

Transkriptler ve ürün kararlarında çelişen yapılar için SenseİK mimari kararları. 20 karar madde halinde listelenir (bkz. 11.8 dosyası).

Örnek:
- Permission anahtarı yazımı: `{modül}.{kaynak_çoğul}.{eylem}` (snake_case)
- Birim yetkilendirme konumu: Identity modülü
- Onay süreci ataması: Workflow modülü
- ...vb.

## Kaynak Dosyalar

Orijinal plan-fragments: `C:\Users\ozgur.celik\algosense\senseik-analiz-calisma\plan-fragments\`

Transkript bulgular: `C:\Users\ozgur.celik\algosense\senseik-analiz-calisma\transcripts\`

## Güncelleme Tarihi

15 Eylül 2026

## Hedef Kitle

- SenseİK mimarlık ekibi
- Ürün yöneticileri
- Geliştirici takımı
- Kolay İK ürün düzenine uygunluk doğrulama
