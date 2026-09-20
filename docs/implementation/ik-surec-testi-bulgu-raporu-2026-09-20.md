# İK süreç bazlı test bulgu raporu (2026-09-20)

Kapsam: İK uzmanı gözüyle canlı portal (`http://localhost:13000`, API `:18080`) üzerinde uçtan uca süreç testi. Amaç,
ekranların ve arkasındaki iş kurallarının bir İK operasyonunda nasıl davrandığını görmek; kod düzeltmesi bu turun konusu değil.
Önceki tur ([teslim-oncesi-test-raporu-2026-09-19.md](teslim-oncesi-test-raporu-2026-09-19.md)) ağırlıklı olarak API/DI
sağlığına baktı; bu tur arayüz, iş kuralı ve süreç tutarlılığına odaklanır. Önceki raporda zaten "bilinen" olan açıklar burada
yalnız "yeniden doğrulandı" olarak geçer.

## 1. Yöntem ve ortam

- Hesaplar: `ik@demo.local` (İK Yöneticisi, ana test hesabı), `owner`, `bordro`, `yonetici`, `calisan`, `denetci` (`Demo123!`).
  İK, Hesap Sahibi ve Çalışan rolleri arayüzden de giriş yapılarak denendi; diğerleri API oturumuyla.
- Yöntem: ekran gezintisi (≈40 rota, masaüstü 1366 px ve mobil 375 px, TR/EN), süreç senaryoları (izin, fazla mesai, onay,
  işe giriş/çıkış, puantaj, avans, bordro, rapor), yetki/veri kapsamı denetimi, doğrulama sınır değerleri.
  Kural doğrulamalarının çoğu izin `preview` ucu ve arayüzün kullandığı API çağrılarıyla yapıldı.
- Ortam: 55 aktif çalışanlı demo kiracı, `dev` dalı, tarih 20 Eylül 2026 (Pazar).
- Konsol/ağ: 40 rota gezildi; **konsol hatası ve 4xx/5xx yok**. Mobilde yatay taşma yok.

## 2. Özet

| Önem | Adet | Ana başlıklar |
|---|---|---|
| Yüksek | 6 | Denetim izi boşlukları, bordro kümülatif matrah, puantaj onayı, onaysız kalan talepler, belge zorunluluğu, tatil takvimi |
| Orta | 14 | Fazla mesai kuralları, izin gün sayımı açıklaması, arama, onay kutusu içeriği, silme/düzeltme yolları, belge/avans/IBAN kuralları |
| Düşük | 15 | Çeviri/ham anahtar, doğrulama mesajları, erişilebilirlik, demo veri artıkları |

Çalışan kartı, izin bakiye hesabı, yetki kapsamı ve onay kuralları büyük ölçüde sağlam çıktı (bölüm 5). En büyük risk,
"sessiz yanlış sonuç" üreten yerlerde: bordro kümülatif matrahı, ay bitmeden puantaj onayı ve denetim kaydı yazılmayan işlemler.

## 3. Bulgular

### 3.1 Yüksek

**Y-1 · Denetim · Bazı kritik işlemler denetim kaydı üretmiyor.**
Son 40 dakikada yapılan işlemlerin denetim kaydına yalnız İzin, Çalışan, Fazla mesai ve Onay nesneleri düştü
(`GET /identity/audit-log`, 435 kayıt; kaynak türleri: User, Approval*, LeaveRequest, Employee, OvertimeRequest,
EmployeeLeaveProfile, Role, LeaveRuleSet…). **Kayıt düşmeyenler:** izin bakiyesi düzeltmesi (7 defter kaydı), aylık puantaj
onayı, bordro dönemi açma ve hesaplama, avans talebi oluşturma/reddi, çalışma takvimi ataması. Ayrıca arayüzde "Detaylar"
sütunu her satırda `••••••••`.
Öneri: bu işlemler için audit yazımı; alan bazlı değişiklik özeti (önceki raporda zaten "bilinen sınırlama").

**Y-2 · Bordro · Önceki dönemler yokken kümülatif GV matrahı sıfır kabul ediliyor; uyarı yok.**
Bordro uzmanı ile 2026-08 dönemi açılıp çalıştırıldı (Ocak–Temmuz dönemleri yok). Ahmet Yılmaz (141.000 brüt) için net
105.264,34 çıktı: bu değer, tek başına %15 diliminde ve asgari ücret istisnasıyla birebir uyuşuyor. 2026 dilimleriyle gerçek
Ağustos'ta kümülatif matrah ≈ %27 dilimine girer. Sonuç: yıl ortasında sisteme geçen bir müşteri, devir matrahı girecek yer
olmadığı için yüksek maaşlılarda sistematik eksik vergi hesaplar. 55 çalışan `calculatedCount=55, skippedCount=0` ile
"başarılı" döndü. Ek: gelecek dönem (2027-03) de açılabildi (üst sınır yok).
Öneri: ilk dönemde "önceki kümülatif matrah" girişi (veya yıl ortası geçiş sihirbazı); önceki dönem yoksa çalıştırmada uyarı.

**Y-3 · Puantaj · Bitmemiş ay, çözülmemiş uyuşmazlıkla onaylanabiliyor; sayılar tutarsız.**
Aylık Puantaj Eylül 2026'da her çalışan için "22 gün uyuşmazlık" görünürken **Eksik gün = 0**, Çalışılan gün = 0. Ay 30 gün
sürecekken 20. günde `POST /attendance/timesheet/monthly/employees/{id}/approve?periodMonth=2026-09-01` **200** döndü
(Test Yaş). Başlamamış ay doğru şekilde reddediliyor (`period_not_started`); devam eden ay ve çözülmemiş uyuşmazlık
engellenmiyor. Toplu "Tümünü Onayla" için aynı davranış beklenir (denenmedi).
Öneri: çözülmemiş uyuşmazlıkta veya ay bitmeden onayı engelle/uyar; "Eksik gün" ile "uyuşmazlık" tanımlarını hizala.

**Y-4 · Onay akışı · 9 bekleyen izin talebinin onay adımı yok; kimsenin kutusuna düşmüyor.**
Kemal Esen, Fatma Çelik, Ahmet Yılmaz, Ali Şahin, Sevgi Gurhan, Test Yaş, Zeynep Arslan, Mehmet Kaya ve Betül Gundogdu'nun
"Bekliyor" talepleri (`/leave/team/requests?status=pending`) detayda **"Bu talep için onay süreci yok."** gösteriyor; onay
kutusu boş (İK, yönetici, sahip). İK yalnız **"İptal et"** yapabiliyor; **onayla/reddet yolu yok**. Ana sayfadaki "İzin Talepleri"
kartı bunları gösterip yanıltıyor (Kemal Esen'in 20 Eyl başlangıçlı talebi 2 haftadır bekliyor). Bunlar seed kaynaklı, ama
üründe de "onaysız kalan talebi İK'nın sonuçlandırması" boşluğu var.
Öneri: (a) seed'i onay adımıyla üret, (b) `leave.requests.bypass_approval` sahibine mevcut talebi onayla/reddet eylemi.

**Y-5 · İzin · Belge zorunlu "Hastalık izni" belgesiz kabul ediliyor** (yeniden doğrulandı, bilinen açık A5/A6).
`POST /leave/me/requests` (`sick`, `requiresDocument=true`) belgesiz **200/pending**; İK adına ekleme (`bypassApproval`) de
kabul ediyor. Önizleme de uyarı vermiyor.

**Y-6 · Tatil takvimi · Dini bayramlar yalnız 2026'ya "Tek sefer" girilmiş; 2027 için kayıt yok.**
Takvimde Ramazan/Kurban günleri 19–22 Mar ve 26–30 May 2026 olarak tek seferlik. 2027 Mart/Mayıs bayramları için satır yok;
Ocak 2027'den itibaren izin, fazla mesai gün türü ve bordro günleri bu günleri normal iş günü sayacak. İzin bakiye dönemleri
Haziran 2027'ye uzandığı için risk şimdiden başlıyor. Ayrıca "QA Takvim (ZZ)" adlı test takvimi duruyor.
Öneri: 2027–2028 bayram takvimi, yıl sonu uyarısı.

### 3.2 Orta

**O-1 · Fazla mesai kuralları.**
(a) Günlük 11 saat sınırı yok: 06:00–19:00 (780 dk) talebi kabul, yalnız "vardiyayla çakışıyor" uyarısı.
(b) Geçmiş tarih sınırı yok: 60 gün önceki talep kabul (izinde 7 gün kuralı var, tutarsız).
(c) Gün türü doğru algılanıyor (hafta içi / hafta tatili / resmi tatil) ama çarpan hepsinde 1,5; resmi tatil çalışması için
oran teyidi gerekir (bölüm 6).
(d) Her kabulde `overtime_consent_missing` uyarısı çıkıyor; yazılı onay olmadan kayıt açılabiliyor (bilinçli uyarı, kayıt engellenmiyor).

**O-2 · İzin gün sayımı · Pzt–Cum tam hafta 6 gün düşüyor; açıklama yok.**
Tam bir Pzt–Cum hafta (2–6 Kas 2026) `6 gün`, Pzt–Per `4`, tek Cuma `1`, Cuma→Cuma `7`. Cumartesi tam haftada ekleniyor
(mevzuat yorumuna dayalı, kasıtlı görünüyor). Sorunlar: arayüzde açıklama yok; onaycı 5 iş günü isteyen talebi "6 gün"
görüyor (Onay kutusu, 14–18 Ara talebi); seed'deki Ayşe Demir'in 20–24 Tem talebi 5 gün (tutarsız); bayram haftasında
26–30 Eki **4,5 gün** (3,5 iş günü + Cumartesi).
Öneri: kural İK ile teyit edilsin, önizlemede "Cumartesi dahil" notu gösterilsin.

**O-3 · Çalışan arama · Türkçe karakter duyarlı, ASCII yazımı bulmuyor.**
`ayse` → Ayşe Demir gelmiyor (yalnız e-postası `ayse.*` olanlar geliyor), `yilmaz` → Ahmet Yılmaz gelmiyor, `ali sahin` → sonuç
yok, `ıbrahim` ve `Demir Ayşe` → sonuç yok. Doğru yazımlar (`Ayşe Demir`) çalışıyor; tek kelime `sahin` → Ali Şahin geliyor
(tutarsız). Sonuç: İngilizce klavyeyle çalışan İK, kişiyi "yok" sanabilir.

**O-4 · Onay kutusu · Karar için gerekli bilgi eksik.**
Kartlar tarih/tür/gün gösteriyor ama **çalışanın açıklaması**, kalan bakiye ve ekip çakışması yok (API'de `teamOverlap` ve
`remainingBalance` mevcut). Aynı kartta karışık tarih biçimi: "20.09.2026" ve "24 Kas 2026". Ret gerekçesi zorunluluğu
ve boş ret uyarısı doğru çalışıyor.

**O-5 · Bildirim · Yaşam döngüsü olaylarında eksik/yetersiz.**
Onaylı izin İK tarafından iptal edilince bildirim düşmedi (gözlem: iptali yapan ve talep sahibi aynı kişiydi; farklı kişi
senaryosu ayrıca doğrulanmalı). Mevcut bildirimler ("İzin talebiniz oluşturuldu", "1. onay adımında reddedildi", "Onayınız
bekleniyor…") tarih, tür ve ret gerekçesi içermiyor. Bildirim ayarları yalnız 4 olay listeliyor.

**O-6 · Çalışan kaydı · Hatalı kaydı silme/düzeltme yolu yok.**
`Taslak` (yalnız ad-soyad) çalışan: `DELETE` 405, işten çıkarma 422, aktive uç 404 → kalıcı çöp kayıt; Taslak için "Eksik bilgi"
rozeti de yok (`hasMissingInfo=false`). İleri tarihli işten çıkışta üst sınır yok: **2031-09-21** çıkışı kabul edildi ve
çalışan anında `Terminated` oldu (işe giriş için 12 ay sınırı var). Anında `Terminated` bilinçli tasarım (PO G-3-09); sorun
üst sınırın olmaması.

**O-7 · Belge gereksinimleri · Cinsiyet ve uyruk duyarsız.**
Kadın çalışanlar (yeni açılan test kaydı, yabancı uyruklu Olena Kovalenko) için **"Askerlik durum belgesi" zorunlu ve "Eksik"**
üretiliyor; Ayşe Demir'in kartında da bu satır var. Koşullu tipler mevcut (18 yaş altı, yabancı, engelli…) ama bu tip koşulsuz.

**O-8 · Avans · Tutar/tarih üst sınırı yok.**
122.000 TL brüt maaşlı çalışan için **1.220.000 TL** avans ve **2020 tarihli** avans kabul edildi. Taksit 1–12 ve tutar > 0
doğrulaması var. Öneri: maaş katı üst limiti (ayar), tarih penceresi.

**O-9 · Çalışan verisi · 55 çalışanın hiçbirinde banka hesabı (IBAN) yok; "Eksik bilgi" bunu yakalamıyor.**
Çalışan kartı → Ödemeler: "Henüz banka hesabı yok" (55/55). Liste rozeti yalnız 13 çalışanda "Eksik bilgi (1–2)". Bordro yine de
55 kişiyi `skipped=0` ile hesapladı; banka ödeme çıktısı IBAN'sız kalır.

**O-10 · İzin süreci · Çalışan onaylı izni iptal edemez; iptal talebi akışı yok.**
Onaylı izinde `withdraw` → 409 (`not_pending`); iptal yalnız `leave.requests.cancel_approved` sahibi İK'da. Çalışan için
"iptal talebi" oluşturma yolu yok. (İK iptali doğru çalışıyor: gerekçe zorunlu, defter kaydı `usage −1` + `reversal +1`.)

**O-11 · İzin bakiyesi düzeltmesi · Eksi bakiye ve uzak tarih uyarısız.**
Bakiye 33 günken −40 düzeltmesi kabul edildi, bakiye **−7**'ye düştü; `effectiveDate=2030-01-01` kabul edildi. Sınır ±365 gün ve
gerekçe zorunluluğu çalışıyor. (Test düzeltmeleri geri alındı, net etki 0.)

**O-12 · Girdi denetimi · Ad/soyad alanlarında HTML/script kabul ediliyor.**
`<b>x</b> <script>alert(1)</script>` ad-soyad olarak kaydedildi ve listeye geldi (React kaçışlıyor, listede düz metin). Excel
dışa aktarımı, e-posta şablonu ve PDF gibi çıktılarda risk taşır. Öneri: ad alanlarında karakter beyaz listesi.

**O-13 · Harcama kategorileri ekranı bozuk.**
Ayarlar → Harcama kategorileri: ad sütununda ham anahtar (`expense.category.food`, `expense.category.travel`),
**"Varsayılan KDV oranı: %undefined"**, üst limit "—". API `nameKey`/`defaultTaxRate` dönüyor, sayfa farklı alan adı bekliyor.
Yalnız 2 kategori var (yemek, seyahat).

**O-14 · Onay sonrası durum gecikmesi.**
Ret/onay kararından hemen sonra izin talebi kısa süre `pending` görünüyor (Worker/outbox, saniyeler mertebesinde); ardından
`rejected/approved` oluyor. Arayüz karar sonrası hemen yenilerse kullanıcı eski durumu görebilir.

### 3.3 Düşük

| ID | Bulgu |
|---|---|
| D-1 | Raporlar: iki rapor türü ham anahtar başlıkla, açıklamasız listeleniyor: `leave.overtime.monthly`, `leave.reconciliation.form` (çeviri eksik). Rapor formu açılıyor. |
| D-2 | Bildirim ayarları: bir satırın etiketi ham `documents.expiry_reminder / reminder`. |
| D-3 | Denetim kaydı ekranı: kaynak adları İngilizce (`OvertimeRequest`, `ApprovalRequest`), tarih biçimi `20.09.2026 15:57:10` (diğer ekranlardan farklı). |
| D-4 | Doğrulama mesajları İngilizce alan adı içeriyor: "'First Name' boş olmamalı", "'Amount' değeri…", "'Installment Count'…", "'Days'…", "'Reason'…". "en fazla 2,0000 gün" biçim hatası. |
| D-5 | Kullanıcılar ve Roller listesinde "Roller" sütununda yerelleştirilmiş ad yerine ham kod: `Admin`, `Employee`, `Auditor`, `HRSpecialist`. |
| D-6 | Giriş: sayfada "Demo kullanıcıları: owner@…, ik@…, bordro@…" ipucu (canlıda kapatılmalı); boş gönderimde görünür alan hatası yok; Türkçe büyük `İ` ile yazılan e-posta (`İk@demo.local`) 401 dönüyor. |
| D-7 | Dil: EN seçilince `<html lang>` `tr` kalıyor; izin türü/rol adları API'den Türkçe geliyor. |
| D-8 | Kullanıcıya dönük geliştirme metinleri: "Etkinlik takvimi Faz 4 ile geliyor", "Eğitimler sonraki fazlarda bu sekmeye eklenecek". |
| D-9 | Demo veri artıkları (önceki QA ve manuel): "QA Test İzni (düzenlendi)", "QA Takvim (ZZ)", Ayşe Demir'in izin listesinde "QA …" talepleri, çalışan "Test Yaş", aday "fgbhfh fghfgh". Müşteri demosu öncesi temizlenmeli. |
| D-10 | Ortak dosyalar: `Bordro Hesaplama Şablonu.xlsx` yalnız `HR, Admin` rollerine kısıtlı; Sahip ve Bordro Uzmanı göremiyor. "Tür" sütunu ham MIME (`application/vnd.openxmlformats-…`). |
| D-11 | API/istemci: `/leave/me/leave-types` ve onay özetinde `leaveTypeName` çevrilmemiş anahtar (`leave.type.excuse`); web çeviriyor, mobil için risk. |
| D-12 | Erişilebilirlik: izin sayfasında adsız ikon butonları (6 adet; kenar çubuğu, talep diyaloğu kapat/gönder vb.). İzin talebi formunda boş gönderimde yalnız ilk alan (tür) hata gösteriyor, başlangıç/bitiş hatası yok. |
| D-13 | İSG: İzmir Office risk değerlendirmesi yenileme tarihi (2026-02-01) geçmiş; ekranda uyarı görünmüyor, tarihler ISO (`2024-01-01`) biçiminde. |
| D-14 | Ana sayfa: Pazar günü "Yaklaşan izinler → Bugün" ve Pazar başlangıçlı 1 günlük talep gösteriliyor (hafta sonu; iş günü/takvim günü karışıyor). |
| D-15 | `Sistem Yöneticisi` rol açıklaması "Faturalama hariç tüm yönetim yetkileri" ama izin kümesi Hesap Sahibi ile birebir aynı (201/201). Vize süreçlerinde onaylayıcı tanımlı olmadığı için çalışanlar vize talebi açamıyor (yapılandırma). |

## 4. Senaryo matrisi

| Süreç | Senaryo | Sonuç |
|---|---|---|
| Kimlik | Yanlış şifre / olmayan kullanıcı aynı mesaj (numaralandırma yok) | Geçti |
| Kimlik | 6 hesapla giriş, rol menüleri ve izinleri | Geçti (D-6) |
| Çalışan | Liste, arama, filtre, sayfalama, organizasyon şeması | Geçti (O-3) |
| Çalışan | Yeni çalışan: TCKN sağlama/tekrar, e-posta tekrarı (büyük/küçük harf), 15 yaş, işe giriş ≤ 12 ay, telefon, ad uzunluğu, boşluk kırpma | Geçti (O-12) |
| Çalışan | Otomatik izin profili, çalışma takvimi, 11 belge gereksinimi | Geçti (O-7) |
| Çalışan | İşten çıkış: tarih < işe giriş, SGK kod uyumu | Geçti (O-6) |
| Çalışan | Taslak/hatalı kaydı silme | Başarısız (O-6) |
| İzin | Bakiye/kıdem kademesi hesabı (55/55 çalışan), 50 yaş kuralı | Geçti |
| İzin | Çakışma, bakiye aşımı, geçmiş 7 gün, mazeret 2 gün, açıklama zorunlu, yalnız hafta sonu, ters tarih | Geçti |
| İzin | Cinsiyete bağlı türler (doğum/babalık), İK adına ekleme | Geçti |
| İzin | Resmi tatil ve arife yarım gün, bayram haftası | Kısmen (O-2) |
| İzin | Belge zorunlu tür | Başarısız (Y-5) |
| İzin | Onay → bakiye → defter, İK iptali + ters kayıt, geri çekme | Geçti |
| Onay | Kendi talebini onaylama, yetkisiz, başka yönetici, tekrar karar, ret gerekçe zorunlu | Geçti |
| Onay | Onaysız kalan talep (seed) | Başarısız (Y-4) |
| Fazla mesai | Çakışma, gelecek tarih, 0 dk, 24 sa+, geçersiz oran | Geçti |
| Fazla mesai | 11 saat sınırı, geçmiş tarih sınırı | Başarısız (O-1) |
| Puantaj | Başlamamış ay onayı engelli | Geçti |
| Puantaj | Devam eden ay / uyuşmazlık onayı | Başarısız (Y-3) |
| Bordro | Dönem aç/çalıştır, asgari ücret neti 28.075,50 | Geçti |
| Bordro | Önceki dönem yokken kümülatif matrah | Başarısız (Y-2) |
| Avans | Tutar > 0, taksit 1–12, para birimi doğrulaması | Geçti (O-8) |
| Yetki | Çalışan başkasının kart/maaş/bakiye/iptal → 403; yönetici ekip dışı maaş → 403 | Geçti |
| Yetki | Yönetici izinleri tümü `team` kapsamlı; çalışan rolünde yazma izni yok | Geçti |
| Rapor | Çalışan izin bakiye raporu oluşturma/indirme | Geçti (D-1) |
| Arayüz | 40 rota konsol/ağ hatası, mobil 375 px, EN dil | Geçti (D-7) |

## 5. Doğrulanan güçlü yanlar

- Yıllık izin hak edişi tüm çalışanlar için 14/20/26 kademelerine ve 50 yaş kuralına uyuyor; devreden, ileri tarihli ve
  kullanılabilir bakiye ilişkisi tutarlı (Ayşe Demir: 90 hak, 10 kullanılan, 3 ileri tarihli, 77 kullanılabilir).
- Onay kuralları sağlam: kendi talebini onaylama (`self_approval_not_allowed`), atanmamış onaycı, gerekçesiz ret, çift karar engelli.
- Çalışan yaş/TCKN/e-posta doğrulamaları ve otomatik onboarding (izin profili, çalışma takvimi, belge listesi) doğru.
- İK iptali ters kayıt yazıyor, bakiye doğru geri geliyor; asgari ücretli çalışanın net maaşı 2026 değeriyle (28.075,50) birebir.
- Yetki kapsamı: çalışan rehberde yalnız iletişim bilgisi görüyor, başkasının kartı/maaşı 403.

## 6. Karar/teyit bekleyenler (İK/hukuk/mali müşavir)

1. **Cumartesi kuralı (O-2):** 5 günlük haftada tam hafta izin 6 gün mü düşülsün; bayram haftasında Cumartesi eklensin mi?
2. **Resmi tatilde fazla mesai çarpanı (O-1):** genel tatil çalışması için 1,5 sabit oran mı, mevzuata göre ek ücretle (2×) mi?
   Hafta tatili çalışmasında da aynı teyit.
3. **İzin türü varsayılanları:** Doğum izni 168 gün (ücretli), Babalık izni 10 gün, "Doğum sonrası ücretsiz izin: limitsiz",
   "Yeni iş arama izni: talep başına 1 gün (saatlik)", "Hastalık izni: ücretsiz". Güncel mevzuat ve SGK ödeneğiyle karşılaştırılmalı.
4. **Günlük 11 saat sınırı** ve fazla mesai yazılı onayı olmadan kayıt açılması (O-1): engelleme mi uyarı mı?
5. **Avans üst sınırı** politikası (O-8) ve IBAN'ın "eksik bilgi" sayılması (O-9).
6. **Yıl ortası bordro geçişi (Y-2):** devir matrahı nasıl girilecek?

## 7. Test kalıntıları (temizlik gerekir)

Bu turda demo veritabanına yazılan kayıtlar; çoğu `TEST` önekli:

- Çalışan: **5 test çalışanı** — 4 `Terminated` (`TEST Denemeci` ×2, `<b>x</b> <script>…`, `Ayşe Kaya`) ve
  **1 `Draft` (`TEST Sadece Ad`, silinemiyor, O-6)**.
- İzin: Ayşe Demir listesinde 5 `TEST-…` talep (iptal/ret/geri çekildi, geçmiş kayıt kalıcı); Ahmet Celik için İK adına
  açılıp iptal edilen 3 `TEST` talebi. `Test Yaş` bakiye düzeltmeleri net 0 (7 defter kaydı).
- Fazla mesai: 10 `TEST-…` talebi geri çekildi.
- Avans: 3 talep (`TEST-avans`, `TEST-huge`, 2020 tarihli) Sahip tarafından reddedildi.
- Puantaj: `Test Yaş` için **Eylül 2026 onayı** (geri alma ucu yok).
- Bordro: **2026-08** dönemi açılıp çalıştırıldı (55 bordro) ve **2027-03** dönemi açıldı; silme/iptal ucu bilinmiyor.
- Rapor: 1 adet "İzin bakiye 2026-09-20.xlsx".

## 8. Kapsam dışı / yapılamayanlar

- İşe alım, performans, vize süreci, eğitim ve zimmet ekranları demoda boş (veri yok); yalnız ekran sağlığı ve listeleme
  kontrol edildi, iş akışı denenmedi.
- Belge yükleme, Excel içe aktarım (Veri aktarım merkezi) ve e-posta ile onay bağlantısı (`/approve/:token`) denenmedi.
- Tüm izin türlerinin bakiye ve gün hesabı tek tek denenmedi; yıllık, mazeret, hastalık, ücretsiz ve doğum türleri denendi.
- Bordro hesabı yalnız asgari ücret ve tek bir yüksek maaşta elle doğrulandı; SGK tavanı, engelli indirimi ve yemek istisnaları
  mali müşavir teyidi bekleyenler arasında (önceki rapor).
- Erişilebilirlik yalnız adsız butonlar ve `lang` özniteliği ile sınırlı; ekran okuyucu testi yapılmadı.
