# Web görsel standart — Alt proje 1: Temel (tema + kabuk)

**Tarih:** 2026-09-28 · **Dal:** `a/gorsel-standart-temel` · **Kaynak standart:** AlgoSense `frontend-standards` (ALGOSENSE_AI_TEAM)

Kullanıcı "tam yeniden tasarım" seçti ve kararları ajana bıraktı. Tam yeniden tasarım beş alt projeye bölündü; bu belge yalnız 1. alt projeyi kapsar.

| # | Alt proje | Durum |
|---|---|---|
| 1 | Temel: tema token'ları, font çifti, kalite tabanı, uygulama kabuğu (ray + üst bar) | bu belge |
| 2 | Giriş / onboarding yüzeyleri | sonra |
| 3 | Ana sayfa (hero = tez) | sonra |
| 4 | Yoğun liste ekranları (data-table desenleri) | sonra |
| 5 | Kalan sayfalar (1–4 desenleriyle toplu geçiş) | sonra |

## Konu ve iş

- **Konu:** SenseHR — İK/bordro SaaS'ı; kullanıcı her gün ekranda olan İK uzmanı ve çalışan.
- **Kabuğun tek işi:** "Neredeyim, nereye gidebilirim" sorusunu bakmadan cevaplamak; içerik sayfalarına sahneyi bırakmak.
- **Sabit kısıt:** Kurumsal renkler (Kurumsal Mavi `#008CF0`, Kurumsal Lacivert `#280087`, yardımcı/patlangaç renkleri) marka kılavuzundan gelir — yeni renk icat edilmez, mevcut tuple'lardan seçilir. Kullanıcı palet seçimi (7 palet) çalışmaya devam eder.

## Pass 1 — Tasarım planı

**Renk (dominant + tek vurgu):**

| Token | Hex | Rol |
|---|---|---|
| Gece Laciverti (navy-8) | `#1C005E` | **Dominant** — navigasyon rayı ve marka bloğu (açık tema) |
| Derin Lacivert (navy-9) | `#100036` | Ray, koyu tema |
| Kurumsal Mavi (brand-6) | `#008CF0` | **Tek vurgu** — eylem, odak, seçili durum (palete göre değişir) |
| Zemin 3 (gray-0) | `#F0F5FA` | Sayfa atmosferi (çerçevenin dışı) |
| Yazı 1 | `#0A0028` | Mürekkep |
| Buz (navy-1) | `#DBD4EB` | Ray üstünde pasif metin/ikon |

**Tip (Inter kaldırılır):**
- Display: **Bricolage Grotesque Variable** — başlıklar (`Title`), marka yazısı, üst bardaki tarih. 800 ağırlık, hafif negatif harf aralığı.
- Gövde: **Onest Variable** — tüm metin, form, tablo; sayılarda `tabular-nums`. Gövde 400; ray etiketleri 500.
- Kontrast: marka 800 ↔ selam satırı 300. Ayrı mono yüz yok (design-log'da IBM Plex/JetBrains Mono çok kez kullanıldı; Onest tabular rakamları yetiyor).
- İkisi de fontsource ile gömülü, `latin-ext` alt kümesi Türkçe için dahil.

**Layout — iki seçenek:**

```
A) Lacivert ray (SEÇİLDİ)              B) Açık ray + lacivert üst bant
┌────────┬─────────────────────┐       ┌──────────────────────────────┐
│SenseHR │ Merhaba · 28 Eylül  │       │▓▓ SenseHR ▓▓ Merhaba ▓▓ 🔍 ▓▓│
│        ├─────────────────────┤       ├────────┬─────────────────────┤
│ Ana   ◖│                     │       │ Ana    │                     │
│ Onay   │   içerik (beyaz)    │       │ Onay   │   içerik            │
│ İK   › │                     │       │ İK   › │                     │
└────────┴─────────────────────┘       └────────┴─────────────────────┘
```

A seçildi: Lacivert tek dominant yüzeyde toplanıyor, içerik beyazda sakin kalıyor. B'de koyu bant her sayfanın başlığıyla yarışıyor ve imza öğesi (aşağıda) kurulamıyor.

**İmza — "özlük sekmesi":** Seçili ray öğesi, içerik yüzeyinin renginde bir dosya sekmesine dönüşür ve ters köşe kıvrımlarıyla içerik alanına bağlanır. Sayfa, raydan "çekilmiş bir özlük dosyası" gibi okunur. Bu öğe süs değil, bilgi taşır: yalnız bulunduğun yeri gösterir. Cesaret burada harcanır; geri kalan her şey sessizdir.

## Pass 2 — AI-default kontrolü ve revizyonlar

| Default | Bu plan |
|---|---|
| 1 Krem + serif + terracotta | Yok — soğuk Zemin, grotesk |
| 2 Siyaha yakın zemin + asit vurgu | Yok — içerik açık; koyu olan yalnız ray, vurgu kurumsal mavi |
| 3 Gazete düzeni | Yok — radius korunur |
| 4 Beyaz üstünde mor gradyan / büyük sayı hero | Ray düz renk, gradyan yok; hero bu alt projede yok |
| 5 Sahte 01/02/03 | Yok |

**Revize edilenler:**
1. **Grup başına renkli ikonlar rayda kaldırıldı.** İlk taslakta korunuyordu. Ancak lacivert üstünde mavi/lacivert ikonlar AA kontrastını tutmuyor, ayrıca "cesaret tek yerde" ilkesiyle çelişen dekor. Rayda ikonlar Buz tonunda, seçili olanlar vurgu renginde. Uygulama başlatıcı menüsü (açık zemin) renkli ikonlarını korur.
2. **Mono yüz eklenmedi** (yukarıda gerekçe).
3. **"Koyu sidebar her SaaS'ta var mı?" sorusu:** Var. Fark iki yerde: renk, markanın kendi Lacivert'i; imza, İK'ya özgü özlük sekmesi. Gradyan ve cam efekti eklenmedi.

## Kapsam (dosyalar)

- `web/package.json`, `web/src/main.tsx`: `@fontsource-variable/inter` → `@fontsource-variable/bricolage-grotesque` + `@fontsource-variable/onest`.
- `web/src/lib/mantine-theme.ts`: `fontFamily` (Onest), `headings.fontFamily` + ağırlıklar (Bricolage), `respectReducedMotion: true`, `focusRing: "auto"` korunur.
- `web/src/styles/index.css`: ray token'ları (`--sense-rail-*`), sayfa atmosferi, `font-variant-numeric: tabular-nums` (tablolar).
- `web/src/layouts/app-layout.module.css` + `app-layout.tsx`: marka bloğu ve ana ray yeni `.rail` sınıfına geçer; `.sidebar` ayarlar alt menüsü için açık kalır; özlük sekmesi yalnız `.rail` içinde.
- `web/src/layouts/sidebar-nav.tsx`: rayda grup ikon renkleri devre dışı (açık yüzeylerde değişmez).
- Davranış, izinler, çeviri anahtarları, test id'leri değişmez.

## Kalite tabanı

- Klavye odağı görünür (rayda açık halka, içerikte Mantine varsayılanı).
- `prefers-reduced-motion`: Mantine `respectReducedMotion`; kendi geçişlerimiz de bu sorguya bağlı.
- AA kontrast: ray pasif metin `#DBD4EB` / `#1C005E` ≈ 11:1; seçili sekme mürekkep/beyaz.
- 375px: mobil ikon rayı aynı lacivert; `body.scrollWidth <= innerWidth`.
- Açık ve koyu tema ikisi de ele alınır.

## Doğrulama

- `pnpm exec tsc --noEmit`, `pnpm exec eslint src`, `pnpm exec vitest run` (web), `pnpm exec vite build`; test kapısı `node .claude/takim/betikler/test-kapisi.mjs`.
- Worktree'den çalıştırılan Vite dev sunucusunda (API proxy → compose `:18080`) ekran görüntüsü: açık/koyu tema, genişletilmiş/daraltılmış ray, ayarlar sayfası, 375px.

## design-log satırı (ders-kaydet PR'ına girecek)

`2026-09-28 | SENSEIK web (alt proje 1: tema + kabuk) | Gece Laciverti ray #1C005E (koyu #100036) dominant + Kurumsal Mavi #008CF0 tek vurgu + Zemin #F0F5FA + mürekkep #0A0028 + buz #DBD4EB | Bricolage Grotesque (800 display) + Onest (400 gövde, tabular rakam) | "özlük sekmesi": seçili ray öğesi içerik renginde ters köşeli dosya sekmesi olarak sayfaya bağlanır`
