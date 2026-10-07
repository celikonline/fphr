# SenseİK web — tam görsel dönüşüm

**Tarih:** 2026-10-01 · **Taban:** `dev` · **Kapsam:** `web/` · **Kaynak:** AlgoSense `frontend-standards`

## Amaç ve kararlar

SenseİK'nin web arayüzünün bütün erişilebilir ekranları aynı tasarım dilini ve kalite tabanını taşıyacak. Kullanıcı kurumsal mavi/lacivert kimliğin korunmasını ve önce web'in dönüştürülmesini seçti. İlk uygulama `/app` ana sayfasıdır; ayrı Docker önizlemesinde gösterildikten sonra diğer ekranlara devam edilir. Expo mobil uygulaması ayrı, daha önce onaylanmış mobil tasarım belgesinde kalır.

Bu çalışma bir görsel ve etkileşim dönüşümüdür. API sözleşmeleri, yetkiler, iş kuralları, rota adresleri, sorgu anahtarları ve mevcut kullanıcı verisi korunur. Bir ekranda bunları değiştirmek gerekiyorsa bağımsız iş olarak ele alınır.

2026-10-01 envanterinde `web/src/pages` altında 106 test dışı TSX sayfa dosyası vardır. Ulaşılabilen rotalar ve bunların alt bileşenleri dönüşüm kapsamındadır; dosya sayısı tek başına tamamlanma ölçütü değildir. Rota envanteri rol ve modül görünürlüğünü de kaydeder.

Üç yol değerlendirildi: yalnız temayı değiştirmek bütün ekranların bilgi düzenini çözmez; her sayfayı bağımsız tasarlamak ortak bileşenlerin ayrışmasına yol açar; **ekran ailesi desenlerini kurup temsilci ekranlarla doğrulamak** hem tutarlılığı hem mevcut davranışı korur. Seçilen yol üçüncüsüdür.

## Mevcut temel ve görsel yön

2026-09-28 tarihli [temel tasarım](2026-09-28-web-gorsel-standart-temel-design.md) içindeki Mantine 9, kurumsal paletler, Onest gövde, Bricolage Grotesque başlık ve açık/koyu/sistem teması korunur. Kullanıcının 2026-10-01 tarihinde verdiği SensePROJECT pano görseli, geometrinin yeni kaynağıdır: düz beyaz uygulama yüzeyi, kompakt üst çubuk, ince çizgili açık sol menü, açık mavi seçili satır, sıkı boşluklar ve çok küçük köşe yarıçapları. Önceki lacivert gezinme rayı, kavisli **özlük sekmesi**, büyük yuvarlatılmış kabuk ve gradyan ana sayfa odağı bu kararla kalkar. SenseİK'nin renk kimliği, metinleri ve yetki yapısı korunur.

- Renk, tipografi, yarıçap ve bileşen görünümleri `web/src/lib/mantine-theme.ts` üzerinden; ek yüzey değişkenleri `web/src/styles/index.css` üzerinden gelir. Kullanıcının yedi kurumsal palet seçimi çalışmaya devam eder.
- Mantine bileşenleri kullanılır. Tailwind yalnız yerleşim ve boşluk içindir. Bileşene özgü durum stili gerekiyorsa yanındaki CSS module Mantine değişkenlerini ve açık/koyu değerleri kullanır.
- Sayfa üstü görsel öncelik, ekranın asıl işine verilir. Başlık, açıklama, birincil eylem ve ikincil denetimler aynı hiyerarşiyle yerleşir. Çok sayıda eşit ağırlıklı istatistik kartı varsayılan açılış düzeni olmaz.
- Durum renkleri anlam taşır; yalnız renk farkıyla bilgi verilmez. Tarih, süre, para ve sayısal sütunlarda tutarlı biçimlendirme ve hizalama kullanılır.
- Metin kullanıcı dilindedir: düğme sonucu söyler, hata düzeltme yolunu gösterir, boş durum uygun eyleme götürür. Yeni metin gerekiyorsa `tr`, `en`, `de` kaynakları birlikte güncellenir.

## Ekran desenleri

### 1. Giriş, davet ve ilk kurulum

Mevcut marka paneli ve kimlik akışları ortak bir görsel aileye bağlanır: formun tek ana eylemi, alanların açıklaması, hata ve MFA adımları açık bir okuma sırasındadır. `login`, `forgot-password`, `reset-password`, `invite-accept` ve `setup-wizard` hem dar hem geniş ekranda aynı etkileşim kurallarını kullanır. Kullanıcının 2026-10-01 ek kararına göre **giriş sayfasının sol mor-mavi gradyanlı marka arka planı, nokta dokusu ve köşe şekilleri korunur**; düz uygulama şablonu giriş formu ve diğer ekranlarda geçerlidir. Dekor formu gölgelemez; hareket azaltma tercihinde dekoratif animasyon durur. İK işe alıştırma ekranı (`pages/onboarding.tsx`) kimlik kurulumu değil, aşağıdaki iş ekranı desenleriyle ele alınır.

### 2. Ana sayfa

Ana sayfa onaylar, yaklaşan izinler, duyurular ve diğer mevcut widget'ları doğrudan dengeli kart gruplarında gösterir. Kullanıcının ek kararına göre “Bugünün odağı” paneli ile “Gündeminiz” ve “Çevrenizde olanlar” bölüm başlıkları görünmez; kartlar korunur. Yeni veri kaynağı veya sahte metrik eklenmez. Widget'lar bağımsız yüklenir; birinin hatası kalan içeriği kapatmaz.

### 3. Yoğun liste ve arama

Çalışan, bordro, puantaj, işe alım, talepler, raporlar ve benzeri ekranlar ortak bir **sayfa başlığı → araç çubuğu → sonuç → sayfalama** düzeni kullanır. `DataTable` sıralama, sütun seçimi, seçili satırlar ve mevcut sunucu sayfalamasının ortak temeli olur. Filtrelerin uygulanmış durumu görünür ve temizlenebilir kalır. Dar ekranda kritik alanlar kart/satır biçimine dönüşür veya tablo kendi kapsayıcısında kayar; sayfa gövdesi yatay taşmaz.

### 4. Detay, form ve ayarlar

Detaylarda önce kimlik/durum ve en önemli eylem, ardından ilgili bilgi grupları görünür. Formlarda alan başlığı, açıklama, zorunluluk, doğrulama ve kaydetme geri bildirimi Mantine'nin ortak prop'larıyla tutarlı kurulur. Ayarlar merkezi ve ikinci gezinme mevcut yetki/arama davranışını korur. Takvim, organizasyon şeması, rapor görüntüleyici ve bordro karşılaştırması gibi özel görünümler bilgi yapısına göre düzenlenir; sırf ortak şablona uysun diye tabloya çevrilmez. Her ekranda yüklenme, boş, hata ve yetki yok durumları tasarlanır.

## Kapsamlı geçiş sırası

| Aşama | Çıktı | Geçiş ölçütü |
|---|---|---|
| 1. Tema ve kabuk | Görsel referansa göre düz, kompakt ortak yapı | Ana sayfa önizlemesinde genişlik ve tema regresyonu kontrol edilir |
| 2. Ana sayfa — ilk teslim | Dengeli kart düzeni ve ayrı Docker önizlemesi | Mevcut widget verileri, izinleri ve hata yalıtımı korunur; kullanıcı sonucu Docker'da görür |
| 3. Giriş ve ilk kurulum | Beş kimlik/kurulum ekranı için ortak desen | Giriş, MFA, davet ve parola akışları aynı davranışla çalışır |
| 4. Yoğun listeler | Paylaşılan tablo/filtre/sayfalama deseni ve temsilci ekranlar | Çalışan + bordro + puantaj örneklerinden sonra aynı desen diğer listelere uygulanır |
| 5. Kalan rotalar | Detay, form, ayarlar, özel ve herkese açık ekranlar | Rota envanterindeki her erişilebilir ekran incelenmiş ve doğrulanmıştır |

Her aşama küçük, incelenebilir değişikliklerle ilerler. Önce ortak desen ve bir temsilci ekran doğrulanır, ardından ilgili ekran ailesi tamamlanır. Aşama bitiminde güncel rota envanterinde kalan ekran ve bilinen istisnalar açıkça raporlanır.

## Etkileşim ve kalite tabanı

- Klavye odağı görünürdür; ikon düğmelerinin erişilebilir adı vardır. Modal/çekmece açma-kapama, odak yönetimi ve Escape davranışı korunur.
- `prefers-reduced-motion` gözetilir. Hareket yalnız durumu veya yön değişimini açıklamak için kullanılır.
- 375 px telefon, 768 px tablet ve 1440 px masaüstü; açık ve koyu tema; Türkçe ile uzun çeviri metinleri görsel olarak denetlenir. Gövde yatay taşmaz; geniş tablo/grafik kendi alanında kayar. Grid/flex çocuklarına gerektiğinde `min-width: 0` uygulanır.
- İlgili role ait izin yoksa eylem görünürlüğü ve rota koruması mevcut davranışla aynı kalır. Görsel düzen yetki kararının kaynağı olmaz.
- Gerçekçi fakat hassas olmayan örnek veriyle ekran görüntüsü alınır. Her ekran ailesinde en az normal, boş ve hata durumu gözden geçirilir. Otomatik testler yalnız görsel stilleri tekrar etmez; akış ve regresyon riskini doğrular.

## Doğrulama ve tamamlanma

Her aşamada `web/` için TypeScript, ESLint, ilgili Vitest testleri ve Vite build çalıştırılır; proje test kapısı gerektiren değişikliklerde ayrıca koşulur. Temsilci ekranlar tarayıcıda klavye ve farklı genişlik/temalarda incelenir. Davranış, izin, rota veya servis çağrısı değişikliği görülürse tasarıma göre düzeltilir veya ayrı bir iş olarak açıkça kaydedilir.

Tam dönüşüm ancak rota envanterindeki tüm erişilebilir web ekranları yeni desenlerle incelenip doğrulandığında, bilinen istisnalar karara bağlandığında ve test kapıları geçtiğinde biter. Mobil bu tamamlanma ölçütünün dışındadır.
