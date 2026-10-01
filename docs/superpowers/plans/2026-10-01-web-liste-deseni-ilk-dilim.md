# SenseİK Web Liste Deseni İlk Dilim Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Çalışan, bordro dönemi ve günlük puantaj listelerinde aynı kompakt başlık, araç çubuğu ve sonuç düzenini kurmak.

**Architecture:** Sayfaların sorgu, yetki ve rota mantığı korunur. `list-page.module.css` yalnız yerleşim ve yüzey geometrisini paylaşır; her ekran kendi filtrelerini ve veri görünümünü tutar. `DataTable` geniş sütunları kendi sınırında kaydırır.

**Tech Stack:** React 19, TypeScript, Mantine 9, TanStack Table, CSS modules, Vitest, Vite.

**Spec:** `docs/superpowers/specs/2026-10-01-web-tam-gorsel-donusum-design.md` — “Yoğun liste ve arama”.

## Global Constraints

- SenseİK paleti, Onest/Bricolage yazı tipleri, açık/koyu tema ve küçük köşe geometrisi korunur.
- API, yetki, filtre, sıralama, seçim, sayfalama ve rota sözleşmeleri değişmez.
- Yeni metin `web/public/locales/{tr,en,de}/employees.json` içinde birlikte eklenir.
- 375 px'te sayfa gövdesi yatay taşmaz; tablo kendi sınırında kayar. Grid/flex çocukları `min-width:0` alır.
- Giriş marka paneli ve `/app` ana sayfası bu dilimin dışındadır.

## Tasarım kararı

- **Renk:** beyaz `#FFFFFF` yüzey, lacivert `#280087` kimlik, mavi `#008CF0` eylem, koyu metin `#283646`, çizgi `#C8CDD2`; koyu tema mevcut Mantine token'larını kullanır.
- **Yazı:** başlık Bricolage Grotesque, satır ve denetim Onest, sayı `tabular-nums`.
- **Seçenek A:** başlık ve filtreler ayrı çizgili bantlar, sonuç tek düz panel. **Seçenek B:** her filtre ve tablo için bağımsız kartlar. Referanstaki pano hiyerarşisi ve kalabalık filtreler nedeniyle A seçildi.
- **İmza:** uygulama kabuğuyla hizalanan ince çizgili sonuç paneli; renk yalnız eylem ve durumlarda kullanılır. Kart ızgarası veya dekoratif gradyan eklenmez.

## Review Focus

1. Filtre temizleme seçimi ve sayfayı sıfırlar; mevcut sunucu filtresiyle “tüm eşleşenleri seç” yanlışlıkla korunmaz.
2. Çalışan tablosunun 10 sütunu 375 px'de yalnız panel içinde kayar.
3. Bordro dönemi mobil kartları ve masaüstü tablosu aynı hedefe gider.
4. Günlük puantajın URL tarih/durum/sayfa parametreleri görsel düzenle değişmez.
5. Yetkisi olmayan kullanıcıda ekran yine `NoAccess` gösterir.

---

### Task 1: Ortak liste yüzeyi ve çalışan listesi

**Files:**
- Create: `web/src/components/ui/list-page.module.css`
- Modify: `web/src/pages/employees/employee-list.tsx`
- Modify: `web/public/locales/tr/employees.json`
- Modify: `web/public/locales/en/employees.json`
- Modify: `web/public/locales/de/employees.json`

**Interfaces:**
- Produces: CSS module exports `page`, `header`, `heading`, `actions`, `toolbar`, `results`.
- Consumes: mevcut `EmployeeGrid` filtreleri, `DataTable`, `EmployeeCardGrid` ve `PaginationFooter`.

- [ ] **Step 1: Mevcut çalışan listesinin arama/filtre/seçim akışını tara.** `employee-list.tsx` içindeki `filter`, `hasActiveFilters`, `selection`, `changePageSize` ve `DataTable` bağlarını doğrula.
- [ ] **Step 2: Düz liste yüzeyini yaz.** `list-page.module.css` içinde başlık çizgisi, sarılan araç çubuğu ve kendi içinde kayan sonuç paneli oluştur; 580 px altında 12 px sayfa boşluğu ve dikey başlık/eylem düzeni kullan.
- [ ] **Step 3: Çalışan sayfasını yüzeye bağla.** Mevcut başlık eylemleri, filtreler, toplu seçim ve tablo/kart/pager yerlerini koruyarak `page`, `header`, `toolbar`, `results` sınıflarını uygula.
- [ ] **Step 4: Uygulanan filtreyi temizleme eylemi ekle.** `hasActiveFilters` doğruysa arama, birim, seviye, durum, hesap, çalışma/sözleşme türü ve eksik bilgi filtrelerini başlangıç değerlerine döndür. Üç dilde “Filtreleri temizle” metnini ekle; var olan `filter` değişimi sayfa/seçimi sıfırlasın.
- [ ] **Step 5: Doğrula ve commit et.** `pnpm --dir web exec tsc --noEmit`, ilgili ESLint ve Vitest; tarayıcıda liste/grid, filtre temizleme ve dar genişlik; commit.

### Task 2: Bordro ve günlük puantaj temsilcileri

**Files:**
- Modify: `web/src/pages/payroll/periods.tsx`
- Modify: `web/src/pages/attendance/daily-list.tsx`
- Reuse: `web/src/components/ui/list-page.module.css`

**Interfaces:**
- Consumes: Task 1'in CSS sınıfları; mevcut `DataTable` ve `DailyGrid` veri/URL durumları.
- Produces: aynı başlık → denetim → sonuç okuma sırası.

- [ ] **Step 1: Bordro dönemlerinde başlığı ve tablo/mobil kart alanını ortak yüzeye taşı.** İç içe kart kenarını kaldır; mobil bağlantı ve durum rozeti korunur.
- [ ] **Step 2: Günlük puantajda tarih/birim denetimlerini araç çubuğuna, sekme ve sonuçları sonuç akışına yerleştir.** URL parametreleri, sayaç ve elle kayıt işlemleri aynı kalır.
- [ ] **Step 3: Doğrula ve commit et.** `pnpm --dir web exec tsc --noEmit`, ilgili ESLint ve `daily-list.test.tsx`; 375/768/1440 px ile açık/koyu tema denetimi; commit.

### Task 3: Sonraki liste dilimlerinin envanteri

**Files:**
- Create: `docs/implementation/web-liste-ekranlari-envanteri.md`
- Modify: `docs/takim/handoff/WEB-01.md`

**Interfaces:**
- Produces: rota bazında “ortak desen uygulandı / özel desen gerekli / sırada” kaydı; sonraki dilimlerin somut dosya listesi.

- [ ] **Step 1: `web/src` rota tanımlarından liste ekranlarını çıkar.** Çalışan, bordro ve puantaj temsilcilerini işaretle; işe alım, talepler, raporlar ve diğer liste rotalarını dosya/izin ile kaydet.
- [ ] **Step 2: Sıradaki dilimleri belgeye yaz.** Her aile için kullanılan tablo veya özel grid, filtre kaynağı ve mobil davranışı belirt; istisnaları görünür kıl.
- [ ] **Step 3: WEB-01 çalışma kaydını güncelle ve commit et.** İlk dilim kanıtı ile kalan ekran sayısını kaydet.
