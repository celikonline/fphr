# Handoff: Quick Input (senseik-035) — Masraf/Avans Talebi pilotu

- **Card:** `docs/agent-board/cards/senseik-035-quick-input-expense-form.json`
- **Branch:** `a/quick-input-expense-form`
- **Scope:** serbest metin → form alanı, sadece Masraf/Avans Talebi ("Fiş ekle" alt formu: kategori, tutar, tarih, açıklama). İzin/çalışan formları kapsam dışı (başka ajanların dokunduğu dosyalar).

## Parser tasarımı

`web/src/lib/quick-input/` — saf TypeScript, framework/API bağımsız, hiçbir ağ çağrısı yok (KVKK: hiçbir şey tarayıcıdan çıkmıyor). Dört dosya + barrel:

- `parse-date.ts` — `parseDate(text, referenceDate?)`. Tanınan kalıplar: `dün`, `bugün`, `yarın`, `geçen hafta` (referans tarihten -7 gün), `dd.mm.yyyy`, `dd/mm/yyyy`, `"dd Ay yyyy"`, `"dd Ay"` (Türkçe ay adları tablosu). Yıl verilmeyen isimli tarihlerde, sonuç referans tarihten ileride çıkarsa bir yıl geriye kayıyor (fişler geçmişe ait olur varsayımı — "20 Aralık" 18 Eylül referansına göre gelecek yıl gibi görünürse bir önceki yılın Aralık'ı seçilir). Geçersiz takvim günleri (ör. 31 Şubat) `null` döner, yuvarlanmaz.
- `parse-amount.ts` — `parseAmount(text)`. Sayı + para birimi işareti (`TL`/`TRY`/`₺`/`lira`, case-insensitive) birlikte geçmeli; sadece sayı (para birimi işareti yoksa) asla eşleşmez. Türkçe/Avrupa biçimli gruplama (`1.250,75`) ve tek ayraçlı ondalık (`450,50` / `450.50`) ayrıştırılıyor. Para birimi her zaman `"TRY"` döner (form zaten sadece TRY gönderiyor).
- `parse-keyword.ts` — `parseKeyword(text, dictionary)` / `findKeywordMatch(...)`. Sözlükteki en uzun (en spesifik) eşleşen ifadeyi seçiyor (ör. "akşam yemeği" > "yemek"). Kelime sınırı kontrolü elle yapılıyor çünkü JS'in `\b`'si Türkçe harfleri (ç ğ ı ö ş ü) kelime karakteri saymıyor — bu olmasaydı "öğle yemeği" gibi ifadelerin başındaki sınır hiç eşleşmezdi. Aynı elle-sınır kontrolü, "taksi"nin "taksitli" (avans taksidi) içinde yanlışlıkla eşleşmesini de engelliyor.
- `parse-expense-quick-input.ts` — `parseExpenseQuickInput(text, categoryKeywords?, referenceDate?)`: üçünü birleştirip `{ categoryCode?, amount?, currency?, receiptDate?, description }` döndürüyor. `receiptDate` `DatePicker`/API ile aynı sözleşmede (`"yyyy-MM-dd"`, `toDateOnly` ile). `description`, eşleşen tarih/tutar/kategori aralıkları metinden çıkarılıp boşluk/noktalama toparlanarak elde ediliyor. Varsayılan sözlük sadece bilinen iki tohum kategorisini (`food`, `travel`) kapsıyor; UI kendi tenant'ının canlı kategori listesini (`kod` + çevrilmiş etiket) bu sözlüğe ekleyerek çağırıyor.

Her üç birincil ayrıştırıcı, hem "sadece sonuç" (`parseDate`/`parseAmount`/`parseKeyword`) hem "sonuç + span" (`findDateMatch`/`findAmountMatch`/`findKeywordMatch`) varyantını export ediyor — ikincisi `description` alanını hesaplamak için kullanılıyor, ama form-bağımsız birincil API kart şartnamesindeki imzalarla birebir (`parseDate(text)`, `parseAmount(text)`, `parseKeyword(text, dictionary)`).

**Güvenlik ilkesi (tüm modülde tutarlı):** emin olunmayan alan her zaman `undefined`/`null` kalır; hiçbir zaman "en yakın tahmin" doldurulmaz. Regex eşleşmesi yoksa, ya da kelime sınırı tutmuyorsa (ör. "taksii" yazım hatası), sonuç boş kalır.

## UI entegrasyonu

`web/src/pages/payments.tsx` → `ExpenseClaimDialog`: "Fiş ekle" kartının üstüne ayrı bir kart eklendi — "Hızlı giriş" `Textarea` + "Doldur" `Button` (Sparkles ikonu). `handleQuickInputFill`:

1. `categoryKeywords` = canlı `categoriesQuery.data`'dan üretilen sözlük (her `category.code` → `DEFAULT_EXPENSE_CATEGORY_KEYWORDS[code]` + kodun kendisi + çevrilmiş etiket).
2. `parseExpenseQuickInput(quickInputText, categoryKeywords)` çağrılıyor.
3. Sonuçta dolu olan alanlar ilgili state'e yazılıyor (`categoryCode` → `categoryId` eşlemesi `categoriesQuery.data` üzerinden id'ye çevriliyor); boş kalan alanlara dokunulmuyor, kullanıcı elle tamamlıyor.
4. Metin kutusu doldurma sonrası temizleniyor (fiş eklendiğinde de sıfırlanıyor).

i18n anahtarları `expense:quickInput.{label,placeholder,fill}` üç locale'e de eklendi (`tr`/`en`/`de`); `src/i18n.locales.test.ts` (anahtar/interpolasyon paritesi) `expense.json` için 6/6 yeşil.

## 300 senaryolu golden-set — gerçek doğruluk

`web/src/lib/quick-input/quick-input-expense.golden.test.ts`: kombinasyon üreteci 20 tarih ifadesi × 15 kategori ifadesi = **300 benzersiz senaryo** üretiyor (tutar, her senaryoda 6 formatı sırayla döngüleyerek atanıyor — 300/6 = 50 tekrar/format). Her senaryonun beklenen sonucu (tarih/tutar/kategori) kod içinde tanımlı sabit tablolardan programatik hesaplanıyor, elle yazılmıyor.

- **20 tarih ifadesi:** 4 göreli (dün/bugün/yarın/geçen hafta) + 4×`dd.mm.yyyy` + 4×`dd/mm/yyyy` + 4×"dd Ay yyyy" + 4×"dd Ay" (yılsız — 2'si geçmiş yıl içinde kalıyor, 2'si gelecek yıla düşüp bir önceki yıla yuvarlanma mantığını tetikliyor).
- **15 kategori ifadesi:** yemek (5), ulaşım (5), konaklama (3), + 1 sözlük dışı kelime ("kırtasiye") + 1 kasıtlı yazım hatası/yakın-eşleşme ("taksii" — "taksi"nin kelime-sınırı testine takılması gerekiyor).
- **6 tutar biçimi:** "450 TL", "450,50 TL", "450.50TL", "1.250 TL", "300 lira", + 1 kurtarılamaz ifade ("yaklaşık bir masraf" — rakam/para birimi işareti yok).

**Sonuç: 300/300 senaryo doğru (%100 doğruluk), 0 "yanlış dolum".** Test iki ayrı iddiada bulunuyor: (1) hiçbir alan asla beklenenle çelişen bir değerle doldurulmuyor (`wrongFills` her zaman `[]` olmalı — sıfır tolerans, kartın "YANLIŞ dolum başarısızlık sayılır" şartı), (2) toplam doğru-alan-çıkarımı oranı ≥ %90 (`passCount >= 270`). Kasıtlı olarak çıkarılamayan alanlar (sözlük dışı kategori, para birimi işaretsiz tutar) parser'ın `undefined` döndürmesiyle zaten "beklenen sonuçla eşleşiyor" sayılıyor — yani "güvenli boş bırakma" senaryo başarısını bozmuyor, sadece gerçekten yanlış bir değer dolarsa test kırmızı oluyor.

%100 çıkmasının nedeni: golden-set'teki 300 kombinasyonun hepsi parser'ın tasarım kapsamındaki kalıplardan (kartta listelenen biçimler) oluşuyor; "sözlük dışı"/"yazım hatası" örnekleri de zaten *beklenen* sonuç olarak boş bırakılıyor, dolayısıyla onlar da "doğru" sayılıyor. Bu, parser'ın kapsamı içindeki her şeyi doğru çıkardığını ve kapsamı dışındaki her şeyi güvenli şekilde boş bıraktığını gösteriyor — ama kapsamın *kendisi* kartta tanımlı biçimlerle sınırlı (aşağıya bakın).

## Zayıf kalan / kapsam dışı ifade kalıpları (bilinçli sınırlamalar)

Bunlar golden-set'te test edilmedi çünkü kart bunları istemedi; ama gerçek kullanımda kullanıcılar deneyebilir ve parser bunları **güvenli şekilde boş bırakır** (asla yanlış doldurmaz), sadece dolum oranı düşer:

- **Yazıyla yazılan sayılar:** "dört yüz elli TL" → `parseAmount` rakam bekliyor, `null` döner.
- **Belirsiz göreli tarihler:** "geçen ay", "bu hafta", "önceki gün" gibi `parseDate`'de tanımlı değil → `null`.
- **Yazım hatası toleransı yok:** kural tabanlı/deterministik tasarım kasıtlı (KVKK + test edilebilirlik gerekçesi, kartın kendi notu); "taksii", "yemk" gibi yakın-yazımlar eşleşmiyor, bulanık eşleştirme (fuzzy matching) yok.
- **Tek fiş/kalem varsayımı:** metinde birden fazla fiş/tutar geçse bile (`parseAmount`/`parseDate`/`parseKeyword` hepsi "ilk eşleşen"i alıyor) sadece tek bir sonuç çıkarılıyor — "hızlı giriş" tek kalem eklemek için tasarlandı, çoklu kalem ayrıştırma kapsam dışı.
- **Sadece TRY:** diğer para birimleri (`EUR`, `USD` vb.) hiç tanınmıyor; form zaten sadece TRY gönderdiği için bu kasıtlı.
- **Varsayılan kategori sözlüğü dar:** `DEFAULT_EXPENSE_CATEGORY_KEYWORDS` sadece `food`/`travel` için anahtar kelime içeriyor; UI çalışma zamanında tenant'ın canlı kategori kod/adlarını ekliyor ama özel kategoriler için sadece kodun/adın kendisiyle eşleşebiliyor (eş anlamlı kelime yok) — tenant çok sayıda özel kategori eklerse dolum oranı düşebilir.

## Test / doğrulama

Docker (`node:24-alpine`, host'ta pnpm yok — bkz. proje hafızası) ile çalıştırıldı:
- `pnpm exec tsc --noEmit` — temiz.
- `pnpm exec eslint src` — temiz.
- `pnpm exec vitest run` — 416 test, 415 yeşil; tek kırmızı (`src/i18n.locales.test.ts` → `attendance.json` → `de` → `teamTimesheet.week/month` çeviri eksikliği) bu dalın dokunmadığı, önceden var olan bir sorun (bu PR'ın kapsamı dışı, dokunulmadı).
- `web/src/lib/quick-input/` altındaki 5 test dosyası (33 test) tek başına da yeşil: `parse-date.test.ts`, `parse-amount.test.ts`, `parse-keyword.test.ts`, `parse-expense-quick-input.test.ts`, `quick-input-expense.golden.test.ts`.
