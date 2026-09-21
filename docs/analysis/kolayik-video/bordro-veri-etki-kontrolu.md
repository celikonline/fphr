# Bordroda hangi veri hangi hesaplamayı etkiler? - kod kontrolü

**Kaynak:** Kolay İK, "Bordroda hangi veri hangi hesaplamayı etkiler?" (7 başlık).
**Amaç:** Dokümanın her iddiasını koddaki gerçek davranışla karşılaştırmak. Tarih: 2026-09-21.
**Durum işaretleri:** ✅ uyumlu · ⚠️ uyuşmazlık / doğrulama gerekir · ❌ eksik · ➖ kapsam dışı.

Bu belge bir doğrulama notudur; yasal parametre veya hesap kuralı değiştirmez. Yasal değerlerde SMMM onayı şarttır.

## Özet

| # | Doküman iddiası | Durum | Not |
|---|---|---|---|
| 1 | AGİ kaldırıldı; medeni durum/çocuk sayısı net maaşı etkilemez | ✅ | AGİ hesaba eklenmedi (bilinçli). Kapanış listesinde yalnız parametre kontrolü olarak bırakılmalı |
| 2 | Engellilik GV indirimi uygular | ✅ | `DisabilityMonthlyTaxReductionDegree1-3`, senseik-019 2b |
| 3 | Engellilik işveren SGK payını da etkiler | ⚠️ | 5 puanlık işveren indirimi (`EmployerDiscountRate`) profildeki `IncentiveLawCode` doluysa uygulanır (senseik-019 2a). Engelli işçiye özgü tam işveren payı teşviki (kota/işyeri koşullu, devlet karşılar) otomatik değil; oran ve koşullar SMMM ile netleşmeden eklenmedi - engelli çalışanın profiline uygun teşvik kanun kodu girilerek indirim uygulanabilir |
| 4 | İşçi SGK %14, işsizlik işçi %1, işveren işsizlik %2 | ✅ | Seed: `Payroll.Sgk.EmployeeRate`, `Payroll.Unemployment.*` |
| 5 | İşveren SGK indirimsiz **%20,75** | ⚠️ | Seed **%20,5** (`Payroll.Sgk.EmployerRate = 0.205`). Fark 0,25 puan. İş kazası/meslek hastalığı primi işyerinin tehlike sınıfına bağlı olduğundan tek oran yaklaşıktır; SMMM ile doğrulanıp işyeri bazlı parametreye dönmeli |
| 6 | Net ücret brütleştirilir (net→brüt) | ⚠️ | **Bordro için bağlandı** (2026-09-21): `NetToGrossSolver` gerçek hesaplayıcıyı ikiye bölme ile çalıştırıp çalışana net kalan tutarı sabitler (dilim, kümülatif matrah, istisnalar dahil). **Çıkış hesabı** günlük ücreti hâlâ net kaydı olduğu gibi kullanıyor |
| 7 | Fazla mesai brüt, SGK ve vergileri değiştirir | ⚠️ | **Fazla mesai bağlandı** (2026-09-21): Leave'in onaylı %25/%50 dakikaları brüte eklenir, nakde çevirme olayı bordroyu "değişti" işaretler. **Hafta/resmî tatil ücreti bağlandı** (2026-09-22): Attendance'ın aylık özeti tatil takviminde çalışılan günleri ve programda olmayan çalışılmış Pazar günlerini sayar; her biri `HolidayPayCalculator` ile +1 günlük ücret olarak brüte eklenir (aynı gün için ayrıca onaylı fazla mesai varsa çift sayılabilir, puantaj onayında kontrol edilmeli) |
| 8 | Prim/ikramiye (net veya brüt) brüt, matrah ve vergiyi değiştirir | ⚠️ | **Brüt ek kazanç bağlandı** (2026-09-21): `PayrollAdjustment` ile açık dönemde prim/komisyon girilir, aynı ayın brütüne eklenir. **Net girilen prim** (brütleştirme) henüz yok |
| 9 | Ücretsiz izin SGK gününü ve vergiyi azaltır; yıllık izin azaltmaz | ✅ / ⚠️ | Devamsızlık günleri `IAbsenceDaysReader` ile okunur; yıllık izin kesinti yapmaz. Tüm izin türlerinin eşlemesi ayrıca test edilmeli |
| 10 | Yemek/yol/sosyal yardım kısmen SGK, GV, DV istisnası taşır | ⚠️ | Yemek: GV/SGK günlük istisna uygulanır. **Yol yardımı bağlandı** (2026-09-21): profilde nakit günlük tutar tutulur, nakit ise çalışılan gün kadar vergiye ve SGK ya tabi kazanç olarak eklenir; ayni (kart, servis) para ödemez. Ayni yol yardımının tutar bazlı istisnası ve çocuk/aile yardımı istisna tutarları modellenmedi |
| 11 | Kesintiler: icra yasal net üzerinden, özel kesintiler ele geçen net üzerinden | ✅ | `GarnishmentCalculator` İİK m.83 tavanı; avans ve sendika aidatı sonra |
| 12 | Kıdem: SGK ve GV yok, yalnız damga vergisi | ✅ | Çıkış hesabı varsayılanı: yalnız damga |
| 13 | İhbar: GV ve damga vergisi | ✅ | Çıkış hesabı varsayılanı: GV + damga, SGK yok |
| 14 | Kıdem/ihbar için uygun SGK çıkış kodu gerekir | ⚠️ | `SgkExitCodeRules` Employee'de var; çıkış hesabı fesih türünden hak ediş çıkarıyor, SGK kodu ile çapraz kontrol yok |
| 15 | Ücretsiz doğum/askerlik izni kıdeme sayılmaz (işveren kuralına bağlı) | ⚠️ | Kıdem günü `EmploymentPeriod` bazlı; ücretsiz iznin kıdemden düşülmesi ayrıca doğrulanmadı |
| 16 | Giydirilmiş brüt (yan haklar dahil) esas alınır | ⚠️ | İhbar ve izin günlük ücreti çıplak brütten hesaplanıyor; giydirilmiş ücret seçeneği yok (örnek ikale PDF'inde de fark görülmüştü) |

## Doğrudan sonuçlar

1. **Net maaşlı çalışan artık bordroya giriyor** (net→brüt bağlandı). Yine de atlanan çalışanı görmek için kapanış listesine "bordrosuz aktif çalışan" uyarısı eklendi. Çıkış hesabındaki günlük ücret hâlâ net kaydı brüt sayıyor.
2. ~~Hafta ve resmî tatil ücreti bordroya girmiyor~~ - bağlandı (2026-09-22; fazla mesai, brüt/net ek ödeme ve nakit yol yardımı da bağlı). Aşağıdaki not tarihsel: Bu, dokümanın 4, 5 ve 7. bölümlerinin tamamını etkiler ve bordro doğruluğunu doğrudan bozar; kapanış planında "Faz 0.2 / 0.3" olarak öncelikli.
3. **İşveren SGK oranı %20,5 mi %20,75 mi?** Yasal parametre olduğu için SMMM onayı olmadan değiştirilmedi.

## Uygulamayla ilişkisi

- Çıkış hesabı (`ExitSettlement`) varsayılan vergi işaretleri bu dokümanla uyumludur (kıdem: yalnız damga, ihbar: GV + damga).
- Kapanış kontrol listesine eklenecek otomatik kurallar: net maaşlı çalışan atlanmadı, işveren SGK oranı doğrulandı (manuel), yol yardımı işlendi.
- Banka dosyası ve beyanname özeti mevcut bordro çıktılarını kullanır; yukarıdaki eksikler giderilene kadar bu çıktılar da eksik tutarı yansıtır.
