# Ayrıntılı izin bakiye raporu

Tarih: 2026-10-07. İş: [leave-balance-detail](../isler/leave-balance-detail.json).
Dal: `takim/leave-balance-detail`. Başlangıç: `9ee074f8c0e7b2ab346098983e8db4649ddf210b`.
Durum: özellik doğrulamaya hazır; tam test kapısı ve bağımsız son inceleme bekleniyor. Bu belge tek başına tamamlanma onayı değildir.

Kullanıcı Faz 4'teki ayrıntılı izin bakiye raporunu önceliklendirdi. Kapsam mevcut
`leave.balance.summary` akışına ayrı `leave.balance.detail` raporu eklemek; İdari kategorisi,
yalnız yıllık izin, mevcut sunucu veri kapsamı ve dinamik şirket yapısı kolonları korunur.
Referans: [raporlama analizi](../../analysis/kolayik-video/11.3.12-raporlama-analitik.md).

## Kararlar

- Güncel alanlar kiracı saat dilimindeki bugüne göre hesaplanır. Ay/yıl filtreleri güncel
  alanları değiştirmez; beş aralık alanı ekler. Yalnız yıl Aralık; yalnız ay cari yıl.
- Dokümandaki “25 kolon” sabit toplam sayı değildir. Üç kimlik alanı + kiracıdaki kariyer
  seviyeleri + 18 güncel izin alanı; tarih filtresiyle beş ek alan. Hassas kimlik numarası
  kolonu yalnız `employee.sensitive.view` yetkisiyle eklenir.
- Hak ediş/devir motoru yeniden kullanılır; rapor izin defterine yazmaz. Toplam alanlar
  işe başlangıçtan bugüne, dönem alanları güncel hak ediş dönemine göre ayrılır.
- Aralık açılışı aynı günün hak ediş/devir/ekstra ve düzeltme kayıtlarını içerir;
  kullanımı içermez. Aralık kapanışı bitiş gününün sonudur. Çakışan taleplerin izin
  günleri mevcut iş takvimi ve kısmi gün hesaplarıyla çözülür.
- `IReportProvider.ExecutionMode` varsayılan senkron davranışı korur; ayrıntılı rapor
  asenkron çalışır. Kalıcı işlemden sonra outbox ile kuyruğa alınır; çalışan işte
  isteği yapanın kiracı/yetki/veri kapsamı tekrar uygulanır.
- Mevcut e-posta sonuç bağlantısı ve execution geçmişi kullanılır. Excel ve PDF aynı
  hesaplanan tabloyu kullanır. PDF genişliği, yatay sayfa ve Ad/Soyad tekrar eden
  kolon gruplarıyla çözülür. Ek kitaplık veya rapor oluşturucu kapsamda yoktur.
- Web ortak filtre ve sonuç bileşenlerini kullanır; kuyruk/çalışıyor durumları izlenir,
  başarı/hata halinde sorgulama durur; PDF hazırlanırken erken indirme denenmez.

## İş paylaşımı

`takim-orkestrator` salt okuma planını çıkardı. Ajan oturumu sınırı nedeniyle tek
`takim-gelistirici-dotnet` ajanı backend ve mevcut web akışını sırayla geliştirir.
Ana oturum iş kartı, Faz 4 durumu ve bu teslim belgesini yönetir.
`takim-gozden-gecirici` bağımsız olarak hesap/yetki/async/UI akışını inceleyecek
ve filtresiz test kapısını kendisi çalıştıracaktır.

## Doğrulama

Backend odaklı Reporting paketi PostgreSQL 17 ile 63/63 geçti. Web paketi 141 dosyada 1.316 testle geçti; TypeScript, ESLint ve üretim derlemesi başarılı. Tam kapı ve bağımsız inceleme son kaynak ağacı sabitlendikten sonra yeniden çalıştırılacak. Mail gönderimi outbox üzerinden yeniden denenir; SMTP kabulünden sonra outbox tamamlanmadan süreç çökerse yinelenen e-posta olasılığı kalır (at-least-once).
