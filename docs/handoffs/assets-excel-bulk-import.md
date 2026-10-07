# Zimmet — Excel ile toplu ekleme (2026-10-07)

İş kartı: [senseik-068](../agent-board/cards/senseik-068-assets-excel-bulk-import.json).
Kullanıcı kapsamı: çalışanlara atanmış zimmetler ve henüz atanmamış envanter aynı dosyadan eklenebilir.

## Kullanım

Zimmet → Envanter → **Excel’den toplu ekle**. Pencereden mevcut `assets.zimmet` şablonu indirilir,
`.xlsx` veya `.csv` yüklenir; ortak Veri Aktarım Merkezi kolon eşleştirme, aktarım politikası ve satır
sonuçlarını gösterir. Dosya sınırı mevcut altyapıdaki 10 MB'dır. İş ekranında **Zimmet envanterine dön**
bağlantısı bulunur; aktarım başarıyla cevaplandığında `assets` sorguları geçersiz kılınır.

- Kategori zorunludur; varlık adı, seri numarası ve açıklama isteğe bağlıdır.
- Çalışan kimlik numarası ve veriliş/iade tarihleri boşsa varlık **boşta envanter** olarak oluşturulur.
- Çalışan kimlik numarası verilirse **veriliş tarihi zorunludur**; çalışan mevcut kiracıdaki aktif kayıttan eşleşir.
- Çalışansız tarih, geçersiz tarih veya bilinmeyen çalışan satır hatasıdır.
- İade tarihi bulunan çalışan satırı mevcut geçmiş zimmet davranışını sürdürür.
- Aynı kategori ve seri numarası mevcut fiziksel varlıkla eşleşir; aynı dosyada da ikinci varlık yaratılmaz.
- Ad verilmezse mevcut kategori/seri numarasından ad üretme davranışı korunur; eski şablonlar kullanılabilir.
- **Sadece güncelleme** modunda seri numarası gereklidir; eşleşmeyen varlık/kategori oluşturulmaz.
- **Tümü başarılıysa aktar** politikası doğrulama, eşleştirme veya domain hatasında hiçbir değişikliği kaydetmez.
  **Hatalı satırları atla** politikasında başarılı satırlar kaydedilir; başarısız satır yarım varlık bırakmaz.
- Çalışansız satır mevcut varlığın atamasını veya durumunu kaldırmaz. Ad/açıklama güncellemesi mevcut künyeyi
  (demirbaş no, marka/model, satın alma, garanti, durum) ve zimmet defterini korur.

## Yetkiler ve kapsam

Envanter erişimi `assets.assets.view`; toplu ekleme düğmesi ayrıca `assets.assets.import`,
`datatransfer.import.view`, `datatransfer.import.run` gerektirir. Şablon indirme ve sonuç raporu indirme
ayrı mevcut izinlerle (`datatransfer.template.download`, `datatransfer.result.download`) uygulanır.
Sunucudaki mevcut yetki/kiracı kontrolleri korunur. Yeni API, izin, bağımlılık veya migration eklenmemiştir.
Toplu aktarım mevcut davranışla çalışanlara tekil atama/iade bildirimleri göndermez.

Bu çalışma mevcut Assets v2 ve Notifications değişikliklerinin bulunduğu ortak `dev` ağacında hazırlanmıştır;
önceki değişiklikler korunmuştur. Commit, push, merge veya çalışan Docker uygulamalarına dağıtım yapılmamıştır.
Backend import dosyaları proje riskli alan listesinde olduğundan birleştirme insan incelemesine tabidir.

## Doğrulama

Frontend: yeni aktarım testleri ile mevcut zimmet ekranı testleri **30/30** geçti; TypeScript kontrolü,
ESLint ve Vite üretim derlemesi geçti. ESLint'te mevcut `employee-list.tsx` uyarısı var.
Backend: paylaşımlı test PostgreSQL sunucusuyla filtresiz tam Assets proje testi **187/187** geçti
(45 aktarım davranışı dahil, sıfır başarısız/atlanan). İlk RED→GREEN koşusu 36/36; incelemede bulunan
seri numarası harf duyarlılığı hatası ayrı RED (44 geçti/1 başarısız) ve son GREEN ile doğrulandı.
Kanıt: `.claude/takim/kanit/assets-tests-postgres-retry.log`; frontend kanıtları `%TEMP%/senseik-068-frontend-*.log`.
Argümansız takım test kapısı ilk koşusu **kırmızı**: `dotnet build`, web build/lint/tip kontrolü ve
web testleri (**1306 geçti**) başarılı; genel .NET test komutu çıkış 1. Kanıt
`.claude/takim/kanit/25a42497abfb6361.json`; kapının raporladığı test sayısı 5582 (taban 3683).
Kaydedilen hata `Sense.Modules.Notifications.Tests.HrMessages.HrMessageTests.Content_over_4096_rejected`:
bu görev başlamadan mevcut olan Notifications mesaj içeriği sınırı değişikliği ile eski test beklentisi
uyuşmuyor. İlgisiz Notifications kodu/testi değiştirilmedi. Bağımsız inceleyicinin son koşumu ve kararı
sohbet iş raporunda bildirilir. Bütün depo için yeşil test kapısı iddiası yoktur; birleştirme bekler.
