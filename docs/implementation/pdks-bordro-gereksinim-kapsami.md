# PDKS, izin ve bordro gereksinim kapsamı

Güncelleme: 14 Ekim 2026. Bu belge, paylaşılan gereksinim listesinin kod ve ekran karşılıklarını gösterir.

## Gereksinim karşılıkları

| Gereksinim | Uygulama karşılığı | Kontrol noktası |
| --- | --- | --- |
| Brütten nete | Aylık, günlük ve saatlik ücret, kısmi ay, vergi ve SGK hesap izi | Bordro dönemi → Deneme bordrosu → Çalışan hesap izi |
| Netten brüte | Dönemin gerçek ücretli gün/saat hedefi üzerinden brütleştirme | Kısmi ay ve ücretsiz izinde net hedefin aynı oranda azalması |
| Yan ödeme formülleri | Yemek, yol, prim, komisyon ve ikramiye şablonları; tarihli toplu çalışan ataması | Formül editörü → örnek hesap → dönem kalemleri |
| Farklı teşvik modelleri | 11 hazır model; tarih, azami süre, çalışan uygunluğu, kategori çakışması ve SMMM onayı | Teşvik tanımı → çalışan ataması → dönem teşvik izi |
| Ek kazanç/kesinti formülleri | Brüt/net kazançlar; sabit ve brüt yüzdesinden kesinti şablonları; kalan net sınırı | Kesintiler brüte eklenmez, vergilendirilmez ve kalan neti aşamaz |
| Aylık/günlük/saatlik ücret | Ücretsiz/kısmi gün izin ile ücretli süre, ücretli izin ve puantaj onayı | Saatlik hesapta eksik veya eskimiş onay hesaplamayı engeller |
| Kıdem/ihbar | Ayrılış tarihindeki maaş; net ücretin brütleştirilmesi; düzenli yan haklar; tavan ve hesap izi | Çalışan kartı → işten ayrılış → tahmin/mahsuplaşma |
| Yıllık izin takibi | Tarihli bakiye; onaylı kullanım; ay sınırını aşan izin dağıtımı; izin–PDKS ayrımı | İzin defteri → aylık puantaj → ayrılış tarihindeki bakiye |

## Çalışma akışı

`/app/payroll/management` çalışma merkezi üç aşamayı birleştirir:

1. Personel kartında ücret geçmişi ve tarihli bordro profilini hazırlayın. İzin defteri ve PDKS ekranında kayıtları inceleyin.
2. Kazanç/kesinti formüllerini ve tarihli çalışan atamalarını düzenleyin. Teşvik için çalışan/işyeri uygunluğunu beyan edip SMMM onayını tamamlayın.
3. Dönemden deneme hesabını açın; uyarı/atlama nedenlerini giderin. Hesaplama, hesap izi, önceki ay karşılaştırması ve kapanış kontrol listesini kullanın.

Kapalı dönemlerin hesap izlerinde kullanılan formül, değişkenler ve mevzuat referansları saklanır. Tanım değişikliği açık dönemleri yeniden hesaplanacak olarak işaretler; geçmiş hesap izlerini değiştirmez.

## Bu pakette kapatılan boşluklar

- Fiili çalışma günü sıfır olan ücretli izin ayı maaş hesabından atlanmaz. Yemek ve nakit yol gibi çalışmaya bağlı haklar fiili gün sayısını kullanır.
- Ücretsiz izin, izin türünün `IsPaid` ayarından okunur. Rapor izninde bordro profilinin `SickDaysReducePay` tercihi uygulanır. Saatlik/yarım gün izinlerin kesirleri korunur.
- PDKS kaydı veya puantaj onayı olmayan, yalnız izin verisi bulunan ayda diğer günler devamsızlık sayılmaz. İzin kesintisi korunur; aylık/günlük ücretin alternatif hesabı uyarıyla gösterilir, saatlik ücret için veri/onay gerekir. Aylık puantaj bu kaynağı etiketler.
- PDKS eksik günleri ile onaylı izinler aynı gün için iki kez düşülmez. Kısmi izin yalnız kendi süresini mazur gösterir; fiilen çalışılan diğer saatler izinle çakışma sayılmaz.
- Ayı aşan izinler, talebin sayılan gün dağılımından dönemlere ayrılır. Kümülatif payların farkı, komşu dönemlerin toplamını saklanan talep toplamına eşit tutar.
- Maaş/profil/formül/teşvik girdisi eksikse, henüz eski bordro olmasa da `calculation_blocked` kapanış engeli oluşur. Deneme hesabı bu bayrağı değiştirmez.
- Maaş kaydının tutarı, para birimi ve ödeme düzeni doğrulanır. TRY dışındaki kayıtlar dönüştürülmeden hesaplanmaz; kullanıcıya neden gösterilir.
- Açık bordroların değişiklik taraması tüm ücret esaslarında puantajı ve devamsızlık alternatifini karşılaştırır. Puantaj, izin veya onay değişikliği eski bordroyu yeniden hesaplanacak hale getirir.
- Aylık puantaj ve matris, sonradan değişen kayıt/izin ve çözülmemiş anomaliler varsa eski onayı güncel kabul etmez.
- APHB taslağının prim günü fiili yemek/çalışma günü yerine ücretli süreye dayanır; kısmi günler bildirimde yukarı yuvarlanıp 30 ile sınırlandırılır.

## Teşvik modelleri

5510 genel sektör, 5510 imalat, 6111, 4447 md.50 ve tarihsel 17103/27103 modellerine ek olarak 5746/4691 SGK, 4857 md.30 SGK ve 5746/4691 gelir vergisi modellerinin parametreleri tamamlandı.

- Ar-Ge/teknokent SGK desteğinde işveren SGK hissesinin yarısı ve 31.12.2028 bitişi kullanılır. Engelli istihdamında asgari ücret matrahı ve 14857 bildirge kodu kullanılır. Kaynak: [SGK güncel teşvik listesi](https://sgk.gov.tr/Content/Post/c52f99f0-da6c-4029-9b9a-a0d6f96a6a42/Guncel-Sigorta-Primi-Tesvik-Destek-ve-Indirimleri-2026-08-04-10-29-55).
- 5746 ve 4691 gelir vergisi teşviklerinde 1.08.2025 itibarıyla asgari ücretin 40 katına kadar brüt ücret sınırı uygulanır. 5746 başlangıç oranı %80'dir; eğitim ve desteklenecek program koşullarına göre %90 veya %95'e düzenlenmelidir. Bu tarihleri kapsayan hazır modellerde 40 kat sınırı kaldırıldığında veya değiştirildiğinde onay/hesap engellenir. Sınır vergi tutarına uygulanmaz; uygun brüt ücretin matrahı üzerinden hesaplanır. Kaynak: [GİB 2026 ücret geliri rehberi](https://intvrg.gib.gov.tr/hazirbeyan/assets/pdf/DUYURU_UNIVERSAL_2026_2026_Ucret_Geliri.pdf).
- Hazır parametreler çalışan/işyeri uygunluğunu otomatik kanıtlamaz. Uygunluk beyanı, teşvik süreleri ve SMMM onayı mevcut akıştan tamamlanır. Eğitim oranı ve Ar-Ge/TGB kapsamındaki ücret ayrımı kullanıcı tarafından doğrulanır.

## Veri geçişi

`20261013223943_AddRegularBenefitsToPayrollProfiles` düzenli yan hakların aylık brüt toplamını ekler.

`20261014025312_CompletePayrollLeaveAndTaxRelief` JSON parametre modelini günceller ve yalnız değiştirilmemiş, boş, onaysız sistem teşvik taslaklarına hazır parametreleri koyar. SMMM onayı vermez; kullanıcı tarafından düzenlenmiş parametreleri değiştirmez. Veri dönüşümü geri alınırken tenant ayarları silinmez.

## Yerel kurulum

14 Ekim 2026'da yerel `senseik` veritabanının yedeği alındı; bekleyen migration'lar `Sense.Migrator migrate` ile uygulandı. API, web ve worker güncel kaynaklarla Docker üzerinden yeniden derlenip başlatıldı. API `/health` ve web HTTP yanıtları 200, API/web Docker sağlık durumları sağlıklı.

Yeni ekran: [Bordro yönetim merkezi](http://localhost:4000/app/payroll/management). Tarayıcıda menü, işlem bağlantıları ve açık dönem kartı görüldü; yatay taşma bulunmadı. İlk yüklemede eski uygulama önbelleği yeni yolu 404 gösterdi; sayfa yenilendiğinde güncel ekran açıldı.

## Kabul senaryoları

| Senaryo | Beklenen sonuç |
| --- | --- |
| Tam ay ücretli izin, fiili gün 0 | Maaş hesaplanır, gün bazlı yemek/yol 0 olur |
| Aylık veya günlük net ücret, 2,5 gün ücretsiz izin | Net temel hedef 27,5/30 ücretli süreyi kullanır |
| Saatlik ücret, ücretli yarım gün izin | Onaylı izin süresi normal saat kısa çalışmasından ayrılır |
| Saatlik ücret, puantaj sonrası izin/kayıt değişikliği | Yeniden onay gerekir; eski bordro açık dönemde değişmiş görünür |
| Ay sınırını aşan izin | Ay payları toplamı saklanan talep günü toplamına eşittir |
| Eksik maaş/profil veya sıfıra bölünen formül | Çalışan atlanır ve gerçek hesapta kapanış engeli oluşur |
| Kesinti kalan netten büyük | Negatif net oluşmaz; kesinti sınırı kullanıcıya bildirilir |
| Teşvik çakışması, süresi bitmiş atama veya onay geri alınması | Çakışma engellenir; geçersiz teşvik uygulanmaz |
| Gelir vergisi teşvikinde 40 kat üstü ücret | Yalnız sınır içindeki ücretin istisna sonrası vergisine destek uygulanır |
| Ayrılıştan sonra maaş artışı | Geçmiş ayrılışın hesabı ayrılış tarihindeki maaşı kullanır |
| Yan hak eklenen kıdem/ihbar | Giydirilmiş ücret kullanılır; kullanılmayan izin çıplak günlük ücreti korur |

Derleme ve web tür kontrolleri teknik uyumu gösterir. Bu paket için otomatik test çalıştırılması yapılmadı; tablo, bordro uzmanının örnek çalışanlarla kabul kontrolünde kullanacağı senaryoları tanımlar. Mevzuatın işyeri/çalışan özel koşulları ve resmî bildirim kabulü SMMM doğrulaması gerektirir.

## Kapsam sınırları

Aynı ay içinde birden fazla maaş değişikliğinin segmentlere ayrılması, yeniden işe giriş geçmişinin bütün özel durumları, aynı ayın tüm ayrılış ödemeleriyle ortak kümülatif vergi mahsuplaşması ve resmî SGK servisinden otomatik uygunluk sorgusu bu hesaplama modelinde bulunmaz. Aylık/günlük ücrette PDKS verisi yoksa mevcut devamsızlık/tam ay alternatifi korunur ve uyarı gösterilir; saatlik ücrette bu alternatif kullanılmaz. APHB çıktısı mevcut uygulamadaki taslak statüsündedir.
