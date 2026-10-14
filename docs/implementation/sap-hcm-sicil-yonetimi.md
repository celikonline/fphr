# SAP HCM yaklaşımıyla sicil yönetimi

Tarih: 2026-10-14. Bu belge sicil atama davranışının kapsamını tanımlar; SAP bağlantısı veya tam SAP HCM veri modeli eşdeğerliği vaat etmez.

## Kurallar

- Ana sicil tenant genelinde tekildir. Atanmış sicil değiştirilemez, temizlenemez ve ayrılmış/silinmiş kişinin sicili yeniden kullanılamaz. Şirket transferi ve mevcut yeniden işe alım sicili korur.
- Yeni yapılandırılmış atamalar tam sekiz ASCII rakamdır: `00000001`–`99999999`. Baştaki sıfırlar korunur.
- Aralıkların kodu, başlangıcı ve atama yöntemi sabittir. Bitişi yalnız artırılabilir. Pasif aralıklar dahil tenant içindeki aralıklar çakışamaz; aralıklar silinmez.
- Dahili (`internal`) aralıkta sistem numarayı kayıt sırasında verir. İstemci numara gönderemez. Harici (`external`) aralıkta kullanıcı veya dosya numarayı sağlar; biçim, aralık ve tekillik doğrulanır.
- Şirket ve işlem (`hire`/`import`) kuralı aralığı seçer. Şirket altındaki bir organizasyon birimi personel alanı olarak kurala bağlanabilir. Çalışanın biriminden yukarı yürünür; en yakın personel alanı kuralı şirket varsayılanından önceliklidir.
- Normal çalışan oluşturma sunucuda `hire` kullanır. Aktarım işleyicisi `import` kullanır; istemci işlem türünü değiştirerek kural atlayamaz.
- Önizleme numara ayırmaz. Eşzamanlı kayıt, aralık değişikliği ve aktarım veritabanı işlemi/kilitleri ile korunur.

## Veri geçişi

Mevcut `SicilNumarasi` kolonu daraltılmaz. Eski siciller sessizce sıfır eklenerek veya yeniden numaralandırılarak değiştirilmez; bordro ve terminal referansları korunur. Eski biçimli siciller yeni atamalar için örnek oluşturmaz, mevcut kayıtta kalır.

Hiç sicil aralığı/kuralı yapılandırılmamış tenant eski isteğe bağlı sicil davranışını sürdürür; arayüz bu durumu gösterir. Yapılandırma başladıktan sonra gerekli şirket/işlem kuralı yoksa yeni atama hata verir. Bütün numaraları kapsayan gizli varsayılan aralık oluşturulmaz.

Devralınan işletmenin uygun sicilleri harici aktarım aralığıyla alınır. Çakışan veya farklı biçimdeki eski siciller kaynak sistem adı ve eski sicil referansı olarak saklanır; dahili aktarım aralığı yeni ana sicili sağlar. Kaynak sistem + eski sicil tenant içinde tekildir. Kimlik ve sicil eşleştirmeleri farklı çalışanlara işaret ederse satır reddedilir.

## API sözleşmesi

Kök: `/api/v1/employees` (web istemcisinde `/v1/employees`).

| Yol | İşlem |
|---|---|
| `/personnel-number-ranges` | Listeleme ve oluşturma |
| `/personnel-number-ranges/{id}` | Ad/bitiş güncelleme |
| `/personnel-number-ranges/{id}/deactivate`, `/reactivate` | Durum değiştirme |
| `/personnel-number-rules` | Şirket/personel alanı/işlem kurallarını listeleme ve oluşturma |
| `/personnel-number-rules/{id}` | Aralık ve durum güncelleme |
| `/personnel-number-companies` | Şirket ve alt birim seçimleri |
| `/personnel-number-assignment?orgUnitId=...&operation=hire` | Salt önizleme |

Aralık oluşturma: `{code,name,startNumber,endNumber,assignmentMode}`. Kural oluşturma: `{companyId,personnelAreaId?,operation,rangeId}`. Ana sicil çalışan detayında salt okunur gösterilir. Yönetim izni `employee.personnel_number.manage`.

## Kurulum ve kullanım

1. Ayarlar → Şirket → Sicil yönetimi ekranında çakışmayan aralıkları oluşturun.
2. `Harici` seçiliyse numara kullanıcı/dosyadan gelir; seçili değilse sistem verir.
3. Şirketin işe alım kuralını tanımlayın. İsteğe bağlı personel alanı seçimi yalnız
   bu birim ve altındaki çalışanlar için şirket varsayılanını geçersiz kılar.
4. Eski çalışanları taşımak için `Aktarım` işlemine ayrıca aralık bağlayın.
   Eski ana sicil korunacaksa harici aralık; yeni ana sicil ve eski referans
   birlikte tutulacaksa dahili aralık kullanın.
5. Aktarımda `legacySourceSystem` ve `legacyPersonnelNumber` kolonlarını birlikte
   doldurun. Ana sicil dahili atanacaksa `sicilNumarasi` boş bırakılır.

Örnek: şirket varsayılanı `50000001–59999999` dahili, şirket altındaki fabrika
`10000001–19999999` harici olabilir. Fabrikanın çalışanı için harici sicil istenir;
diğer birimlerde şirket varsayılanının sıradaki numarası atanır. Bu aralıklar aynı
tenant içindeki numaraların tekilliğini korumak için çakışmaz.

## Model sınırı

Mevcut Employee modeli tenant içinde tek çalışan kaydı ve bu kaydın çalışma dönemlerini tutar. Aynı kişiye ayrı eşzamanlı istihdam kayıtları ve farklı ana siciller açılması bu değişikliğin kapsamına dahil değildir. Bunun için kişi/istihdam ayrımı, TCKN tekillik kuralı ve modüllerin çalışan referansları ayrıca tasarlanmalıdır.

## Doğrulama

Tamamlanma raporunda PostgreSQL üzerinde tahsis, çakışma, aralık sonu, harici doğrulama, değişmezlik, aktarımın tümü-ya-da-hiçbiri ve tenant izolasyonu testleri ile web test/tip/lint/build sonuçları kaydedilir. Üretim veritabanına migration uygulamak ve dağıtım bu yerel geliştirme işinden ayrı işlemdir.

2026-10-14 tamamlanma: geliştirici ve bağımsız inceleyici Employee **424/424**
(415 mevcut + 9 yeni sicil testi), DataTransfer **56/56** ve web **29/29** testini
başarıyla çalıştırdı. Başarısız/atlanan test yok. TypeScript, ESLint ve Vite/PWA
derlemesi geçti; ESLint'in mevcut iki uyarısı devam ediyor. Standart
`.Codex/takim/betikler/test-kapisi.mjs` ve `kapi-dogrula.mjs` depoda bulunmadığı
için gerçek modül testleri ve web kontrolleri bağımsız tekrarlandı.

Gerçek EF migration: `20261014155317_AddPersonnelNumberManagement`; mevcut
cost-center migration ve eski sicil kolonunun uzunluğu korunur. Kod yerel çalışma
ağacında doğrulandı. Commit/push kullanıcı isteğiyle yapılır; canlı veritabanına
uygulama için erişilebilir hedef bağlantısı gerekir.

Test konteynerinin gerçek host portu bu oturumda
`55433`; test başlatma betiğinin yazdırdığı `55432` başka bir projeye ait sunucuya
gidiyordu. Konteynerler değiştirilmeden testler gerçek portta çalıştırıldı.

## SAP kaynakları

- [NUMKR ve organizasyondan numara aralığı seçimi](https://learning.sap.com/courses/master-data-configuration-in-sap-hcm-for-s-4hana/defaulting-personnel-number-ranges_bc90d71d-5e10-4272-a0e0-064c247e68cf)
- [Personel numarası ve atama davranışı](https://help.sap.com/docs/SAP_S4HANA_ON-PREMISE/c6c3ffd90792427a9fee1a19df5b0925/e30e41f92ae5423f9c59e66933c33851.html)
- [HCM çalışan aktarımı](https://help.sap.com/docs/SAP_S4HANA_ON-PREMISE/d3a3eb7caa1842858bf0372e17ad3909/de66940cf3e2485c90730289ec815e16.html)
- [Şirket kodu ve personel alanı ilişkisi](https://help.sap.com/docs/SAP_S4HANA_ON-PREMISE/c6c3ffd90792427a9fee1a19df5b0925/ce36e153a217424de10000000a174cb4.html)
