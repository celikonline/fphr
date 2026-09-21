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
| 3 | Engellilik işveren SGK payını da etkiler | ❌ | Engelli işçi işveren prim teşviki hesaplanmıyor; `EmployerDiscountRate` hiç uygulanmıyor (kodda TODO P2+) |
| 4 | İşçi SGK %14, işsizlik işçi %1, işveren işsizlik %2 | ✅ | Seed: `Payroll.Sgk.EmployeeRate`, `Payroll.Unemployment.*` |
| 5 | İşveren SGK indirimsiz **%20,75** | ⚠️ | Seed **%20,5** (`Payroll.Sgk.EmployerRate = 0.205`). Fark 0,25 puan. İş kazası/meslek hastalığı primi işyerinin tehlike sınıfına bağlı olduğundan tek oran yaklaşıktır; SMMM ile doğrulanıp işyeri bazlı parametreye dönmeli |
| 6 | Net ücret brütleştirilir (net→brüt) | ❌ | Net maaşlı çalışan **bordroya hiç girmiyor**: `NetSalaryNotSupported` ile atlanıyor. Çıkış hesabındaki günlük ücret de net kaydı olduğu gibi kullanıyor |
| 7 | Fazla mesai brüt, SGK ve vergileri değiştirir | ❌ | `OvertimePayCalculator` ve `HolidayPayCalculator` var ama dönem hesabına bağlı değil |
| 8 | Prim/ikramiye (net veya brüt) brüt, matrah ve vergiyi değiştirir | ❌ | Dönem bazlı ek kazanç kalemi yok. Yalnızca çıkış hesabında elle satır var |
| 9 | Ücretsiz izin SGK gününü ve vergiyi azaltır; yıllık izin azaltmaz | ✅ / ⚠️ | Devamsızlık günleri `IAbsenceDaysReader` ile okunur; yıllık izin kesinti yapmaz. Tüm izin türlerinin eşlemesi ayrıca test edilmeli |
| 10 | Yemek/yol/sosyal yardım kısmen SGK, GV, DV istisnası taşır | ⚠️ | Yemek: GV/SGK günlük istisna uygulanır. **Yol yardımı** profilde tutulur ama hesapta kullanılmaz. Çocuk/aile yardımı istisna tutarları modellenmedi |
| 11 | Kesintiler: icra yasal net üzerinden, özel kesintiler ele geçen net üzerinden | ✅ | `GarnishmentCalculator` İİK m.83 tavanı; avans ve sendika aidatı sonra |
| 12 | Kıdem: SGK ve GV yok, yalnız damga vergisi | ✅ | Çıkış hesabı varsayılanı: yalnız damga |
| 13 | İhbar: GV ve damga vergisi | ✅ | Çıkış hesabı varsayılanı: GV + damga, SGK yok |
| 14 | Kıdem/ihbar için uygun SGK çıkış kodu gerekir | ⚠️ | `SgkExitCodeRules` Employee'de var; çıkış hesabı fesih türünden hak ediş çıkarıyor, SGK kodu ile çapraz kontrol yok |
| 15 | Ücretsiz doğum/askerlik izni kıdeme sayılmaz (işveren kuralına bağlı) | ⚠️ | Kıdem günü `EmploymentPeriod` bazlı; ücretsiz iznin kıdemden düşülmesi ayrıca doğrulanmadı |
| 16 | Giydirilmiş brüt (yan haklar dahil) esas alınır | ⚠️ | İhbar ve izin günlük ücreti çıplak brütten hesaplanıyor; giydirilmiş ücret seçeneği yok (örnek ikale PDF'inde de fark görülmüştü) |

## Doğrudan sonuçlar

1. **Net maaşlı çalışan bordroda sessizce atlanıyor.** Kapanış kontrol listesindeki "bordro hesaplandı" kuralı yalnızca en az bir bordro olup olmadığına bakar; atlanan çalışanı yakalamaz. Önerilen kural: aktif ve maaş kaydı olan her çalışan için bordro var mı.
2. **Fazla mesai, ek ödeme ve yol yardımı bordroya girmiyor.** Bu, dokümanın 4, 5 ve 7. bölümlerinin tamamını etkiler ve bordro doğruluğunu doğrudan bozar; kapanış planında "Faz 0.2 / 0.3" olarak öncelikli.
3. **İşveren SGK oranı %20,5 mi %20,75 mi?** Yasal parametre olduğu için SMMM onayı olmadan değiştirilmedi.

## Uygulamayla ilişkisi

- Çıkış hesabı (`ExitSettlement`) varsayılan vergi işaretleri bu dokümanla uyumludur (kıdem: yalnız damga, ihbar: GV + damga).
- Kapanış kontrol listesine eklenecek otomatik kurallar: net maaşlı çalışan atlanmadı, işveren SGK oranı doğrulandı (manuel), yol yardımı işlendi.
- Banka dosyası ve beyanname özeti mevcut bordro çıktılarını kullanır; yukarıdaki eksikler giderilene kadar bu çıktılar da eksik tutarı yansıtır.
