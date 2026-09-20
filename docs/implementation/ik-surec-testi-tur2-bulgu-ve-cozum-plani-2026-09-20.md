# İK süreç testi — 2. tur: bulgular, çözüm planı ve durum (2026-09-20)

Bu belge, [ilk tur raporunun](ik-surec-testi-bulgu-raporu-2026-09-20.md) devamıdır. Amaç: (1) ilk turdaki bulguların **güncel
canlı sistemde hâlâ açık olup olmadığını** doğrulamak, (2) çalışan ve izin süreçlerini bu kez **arayüzden uçtan uca** denemek,
(3) yeni bulguları listelemek, (4) çözüm planı yapıp düzeltmeleri uygulamak.

## 1. Yöntem ve ortam

- Ortam: compose yığını (`localhost:13000` web, `:18080` API), 55 aktif çalışanlı demo kiracı, `ik@demo.local` (İK).
- Arayüz turu: giriş, çalışan listesi/arama, **Yeni çalışan ekle** (boş gönderim, geçersiz TCKN, yinelenen e-posta, başarılı
  kayıt), yeni çalışanın izin sekmesi, **izin talebi** diyaloğu (boş gönderim, tür seçimi, belge zorunlu tür, tarih seçici),
  onay kutusu, harcama kategorileri.
- API turu: ilk raporun bulguları canlı API'ye karşı yeniden yürütüldü (arama, tatil takvimi, izin türleri, belge zorunluluğu,
  işten çıkış, doğrulama mesajları, avans, fazla mesai).
- **Önemli çıkarım:** ilk rapor öğleden önce yazıldı; sonrasında başka oturumlar `HEAD`'de birçok bulguyu düzeltti (izin belge
  zorunluluğu, tatil takvimi 2026–2031, bitmemiş ay puantaj kapatma reddi, HR onay komutu, bordro uyarısı, fazla mesai uyarıları).
  Bu yüzden ilk rapordaki durumlar aşağıda güncel duruma çevrildi.

## 2. Yeni bulgular (2. tur)

| ID | Önem | Bulgu | Durum |
|---|---|---|---|
| N-1 | **Yüksek** | **Çalışan arama tamamen 500 veriyor.** `HEAD`'deki "Türkçe karakter" düzeltmesi `EF.Functions.Unaccent(...)` ifadesini sorgunun dışında (istemci tarafında) çağırıyordu; herhangi bir `q` değeri `InvalidOperationException` ile 500'e düşüyordu. Mevcut `q:` testleri de kırıktı. | **Çözüldü** — ifade sorgu içine alındı, kelime bazlı (sıra bağımsız) eşleşme eklendi; canlıda doğrulandı (`ayse`, `yilmaz`, `ali sahin`, `Demir Ayşe`, `ibrahim`). |
| N-2 | Orta | Yinelenen iş e-postası/TCKN hatası yalnızca geçici toast; alanda işaret yok. | **Çözüldü** (web). |
| N-3 | Orta | Doğrulama mesajlarında alan adı İngilizce/ham (`'First Name' boş olmamalı`, `'Amount'`, `'Duration Minutes'`). Kök neden iki parçalı: (a) `field.*` çeviri anahtarları eksik, (b) `x => info(x).FirstName` gibi delegeli kurallarda FluentValidation çözümleyiciye `member=null` geçiriyordu, çeviri hiç denenmiyordu. (Bu, ilk rapordaki D-4'ün kök nedenidir.) | **Çözüldü** — çözümleyici ifade gövdesinden üye adını okuyor; 34 yeni `field.*` anahtarı (tr/en); canlıda doğrulandı (`'Ad' boş olmamalı`, `'Tutar' …`, `'Süre (dakika)' …`). |
| N-4 | Orta | `unaccent` PostgreSQL eklentisi yalnızca docker-compose `init.sql` ile kuruluyor; EF migration'ı yok. Yönetilen/temiz/test veritabanında arama patlar. | **Çözüldü** — `EnableUnaccentExtension` migration'ı (`CREATE EXTENSION IF NOT EXISTS unaccent`). |
| N-5 | Orta | Belge zorunlu izin türünde (Hastalık) form ipucu "bekleyen talebe **sonradan** eklenebilir" diyor; oysa sunucu belgesiz talebi reddediyor (`leave.errors.document_required`). Metin ile davranış çelişiyor. | **Çözüldü** (tr/en/de metin). |
| N-6 | Düşük | İzin talebi diyaloğunda tarih seçici yalnız seçiciyle çalışıyor (klavyeyle yazma yok); bitiş tarihi başlangıçtan otomatik dolmuyordu. Belge alanı tarayıcının ham `input[type=file]`'ı idi. | **Çözüldü** (bitiş otomatik dolar, temalı `FileInput`). Klavyeyle tarih girişi açık (küçük). |
| N-7 | Düşük | Giriş formunda Enter tuşu gönderimi tetiklemedi (tarayıcı otomasyon aracıyla gözlendi; aracın kısıtı olabilir). | **Doğrulanmadı** — gerçek klavyeyle teyit edilmeli. |
| N-8 | Düşük | Yeni çalışan diyaloğundaki "Birim" listesinde pozisyon adları (`Finans Uzmanı`, `Muhasebe Uzmanı`) birim olarak listeleniyor; Ayşe Demir ve Ahmet Yılmaz'ın Departman sütunu "—". | Açık (gözlem; org yapısı verisi/tasarımı). |
| N-9 | **Yüksek (süreç)** | Bu turda `docker compose build` çalışma dizinindeki **başka oturumun yarım Identity değişikliğini** (`setup_completed_at` sütunu, migration yok) imaja gömdü ve giriş ~10 dk 500 verdi. Temiz `HEAD` dışa aktarımı + yalnızca kendi dosyalarımla yeniden derlenip düzeltildi. | Düzeltildi; **süreç kuralı:** paylaşımlı çalışma dizininde imaj derlerken `git archive HEAD` + kendi dosyaların yöntemini kullan (bkz. §6). |

## 3. İlk tur bulgularının güncel durumu

Durum kodları: **Ç** = bu turda çözüldü · **H** = `HEAD`'de başka oturumca çözülmüş · **A** = açık · **K** = ürün/hukuk kararı bekliyor.

| ID | Konu | Durum | Not |
|---|---|---|---|
| Y-1 | Denetim kaydı boşlukları | **A** | Aşağıda plan (§4). İşaretli ama bağlanmamış modüller: Expense, Organization, Documents, Recruitment, Performance, Training, Onboarding, Announcements, Assets, OccupationalSafety, Integrations. Bağlı: Identity, Employee, Leave, Workflow. Attendance/Payroll/izin defteri işaretsiz. |
| Y-2 | Bordro kümülatif GV matrahı | **H** | `fdb551bc` uyarı ekledi; devir matrahı girişi (asıl çözüm) hâlâ açık → **K**. |
| Y-3 | Bitmemiş ay puantaj onayı | **H/K** | Bitmemiş ay reddediliyor (`MonthNotComplete`). Tamamlanmış ayda çözülmemiş uyuşmazlıkla onay → **K**. |
| Y-4 | Onaysız bekleyen izin talepleri | **H** | HR onay komutu + seed senaryosu eklendi; mevcut 9 demo talep canlıda hâlâ "bekliyor". |
| Y-5 | Belge zorunlu izin belgesiz | **H** | Canlıda doğrulandı (400). Form metni N-5 ile düzeltildi. |
| Y-6 | Tatil takvimi 2027+ | **H** | Canlıda doğrulandı: 2026–2031 kayıtlı. |
| O-1 | Fazla mesai kuralları | **H (uyarı)** | 11 saat ve geçmiş tarih **uyarı** olarak eklendi, engel değil → **K**. |
| O-2 | İzin gün sayımı açıklaması | **H?** | `870bb83a` (doğrulanmadı). |
| O-3 | ASCII arama | **Ç** | Bkz. N-1. |
| O-4 | Onay kutusu bilgisi | **Ç (kısmen)** | Tarih biçimi (web) ve çalışan **açıklaması** (API `LeaveApprovalSummary.Description` + web) tamam. Kalan bakiye ve ekip çakışması açık (§4). |
| O-5 | Bildirim içerikleri | **A** | |
| O-6 | Çıkış tarihi üst sınırı / taslak silme | **Ç (kısmen)** | Çıkış en fazla 6 ay ileri. Taslak kaydı silme yolu **A**. |
| O-7 | Askerlik belgesi cinsiyet/uyruk duyarsız | **K** | Tenant'ın belge tipi ekranı koşul atamaya zaten izin veriyor; varsayılan tohum politikası kararı gerekir. |
| O-8 | Avans sınırları | **Ç (kısmen)** | Talep tarihi −30/+90 gün penceresi. Maaş katı üst limiti → **K**. |
| O-9 | IBAN'ın "Eksik bilgi" sayılması | **K** | |
| O-10 | Çalışan onaylı izin iptal talebi | **K** | |
| O-11 | Eksi bakiye/uzak tarih düzeltmesi uyarısı | **A** | Sert red önerilmez (İK düzeltmesi meşru); uyarı+onay diyaloğu önerilir. |
| O-12 | Ad alanında HTML | **Ç** | `<`, `>`, kontrol karakterleri reddedilir; gerçek adlar etkilenmez. |
| O-13 | Harcama kategorileri ekranı | **Ç** | Çeviri, `%undefined` yok; canlıda doğrulandı. |
| O-14 | Onay sonrası gecikme | **A** | Outbox gecikmesi; arayüzde iyimser güncelleme/yeniden sorgu önerisi. |
| D-1, D-2, D-3, D-5, D-7 | Ham anahtarlar, çeviri, `lang` | **Ç** | Web. |
| D-4 | Doğrulama mesajı alan adları | **Ç** | Bkz. N-3. |
| D-6 | Girişte demo ipucu | **A** | `VITE_SHOW_DEMO_CREDENTIALS` bayrağı ile kapatılabilir hale getirilmeli. |
| D-8 | Geliştirme metinleri | **Ç (kısmen)** | `navigation.json`/`coming-soon-page.tsx` başka oturumun dosyaları; "Faz 4" metni orada. |
| D-9 | Demo veri artıkları | **K** | Silme kararı bekleniyor; bu turun artığı: `TEST UI Denemeci` (Terminated). |
| D-10, D-11 | Ortak dosya rol kısıtı; API ham `leave.type.*` adı | **A** | D-11: web çeviriye bağımlı, mobil için ayrı karar. |
| D-12 | Erişilebilirlik | **Ç (kısmen)** | Alan hataları, kapat butonu aria-label. Kenar çubuğu adsız butonlar `app-layout.tsx`'te (başka oturum). |
| D-13 | İSG tarih biçimi/uyarı | **Ç** | |
| D-14, D-15 | Ana sayfa "Bugün" hafta sonu; rol açıklaması | **A** | |

## 4. Çözüm planı (kalan işler, sırayla)

1. **Y-1 Denetim kaydı kapsamı (Yüksek).** Her hedef modül için: `DbContext`'e `AuditLogEntry` eşlemesi
   (`AuditLogEntryConfiguration` önce uygulanır) + modül DI'ında `AuditLogInterceptor` + `ModuleAuditLogSource<T>`
   + EF migration (`<schema>.audit_log_entries`) + canlıda migration. Sıra: **Expense** (avans/harcama), **Attendance**
   (`MonthlyTimesheetApproval` → `IAuditLogged`), **Payroll** (`PayrollPeriod`), **Organization** (tatil takvimi, birim),
   **Documents**. İzin bakiye düzeltmesi için `LeaveLedgerEntry`'nin tümünü işaretlemek (sistem tahakkuk gürültüsü) yerine
   düzeltme komutundan açık denetim yazımı önerilir.
2. **O-4 devamı:** onay özetine `RemainingBalance` ve `TeamOverlap` (Leave.Application'da `ResolveTeamOverlapAsync` mantığı
   Workflow.Contracts sınırından geçirilmeli).
3. **Y-2 devamı:** ilk bordro döneminde "önceki kümülatif matrah" girişi / yıl ortası geçiş sihirbazı (mali müşavir teyidiyle).
4. **O-6 devamı:** Taslak çalışan için silme/iptal komutu; **O-11:** eksi bakiye/uzak tarih uyarı diyaloğu.
5. **D-6:** demo ipucu bayrağı (`VITE_SHOW_DEMO_CREDENTIALS`, Dockerfile ARG + compose arg); **D-14/D-15/D-10** küçük düzeltmeler.
6. **Karar bekleyenler (§5):** politika kararları çıkınca ilgili doğrulamalar eklenecek.

## 5. Kendi verdiğim kararlar (kullanıcı yokken)

| Karar | Gerekçe |
|---|---|
| İşten çıkış tarihi en fazla **6 ay** ileri | İhbar süreleri haftalar mertebesinde; işe giriş (12 ay) için kural zaten var. Çıkışta çalışan anında `Terminated` olduğundan daha uzun süre yanlış kayıt riski. |
| Avans talep tarihi **−30 / +90 gün** | 2020 tarihli yazım hatalarını yakalar; ileri tarihli ödeme planına izin verir. Tutar üst sınırı politika kararı olduğundan eklenmedi (mevcut 10 M sanity sınırı duruyor). |
| Ad/soyad: yalnız `<`, `>` ve kontrol karakteri reddi | Yabancı/eski adlarda rakam, kesme, tire meşru; kara liste minimum risk, mevcut test ve verileri bozmaz. |
| Arama: kelime bazlı AND + aksan katlama | "Demir Ayşe", "ali sahin" gibi doğal aramalar çalışsın. |
| Onay özetine yalnız **açıklama** eklendi | Bakiye/çakışma modül sınırını aşan iş; ayrı işe bırakıldı. |
| O-7, O-1, O-8 tutar, O-9, O-10, Y-2 devir, D-9 silme: **karar bekliyor** | Ürün/hukuk/mali politika; kod değişikliği tahmine dayanmamalı. |

## 6. Doğrulama kayıtları

- Testler: Employee 323/323, Expense 37/37, Shared.Infrastructure (çeviri eşliği) 34/34, Architecture 58/58; web `tsc` temiz,
  ESLint temiz, Vitest 711/711 (ajan turunda). Yeni testler: arama (aksan/sıra), işten çıkış tarih sınırı, avans tarih penceresi,
  ad karakter kuralı, web tarafında 9 yeni test dosyası.
- Canlı doğrulama (yeni imajlar): arama 200, alan adları yerelleşmiş, ad işareti 400, avans tarihi 400, uzak çıkış 400,
  harcama kategorileri ekranı düzgün.
- **Dağıtım notu:** api/worker imajları `git archive HEAD` + yalnız bu turun dosyalarıyla derlendi (başka oturumun yarım
  Identity değişikliği dışarıda). Employee migration'ı canlı veritabanına elle kaydedildi (`employee.__ef_migrations_history`;
  eklenti zaten kurulu). Bu turun değişiklikleri **commit edilmedi** (çalışma dizininde başka oturumların yarım işleri var).
