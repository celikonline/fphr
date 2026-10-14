# Platform Yönetim ve Bakım Planı

## Amaç ve menü konumu

Platform operatörlerinin yedekleme, zamanlanmış işler, bakım pencereleri ve tanılama işlerini tek yerden yönetmesi hedeflenir. Giriş `Platform → Yönetim ve Bakım` altında bulunur. Bu alan yalnız platform personeline açıktır; şirket yöneticilerinin şirket ayarlarından veritabanı veya tüm platform verilerine erişmesi mümkün olmamalıdır.

Yönetim ekranı `/app/platform/maintenance` rotasında yer alır. Genel Bakış ve Zamanlanmış Görevler salt okunur API ile canlı veri gösterir; yedekleme, bakım ve destek kartları backend işlemleri tamamlanana kadar “Planlandı” durumundadır. Menü ve API erişimi `platform.operations.view` gibi granular platform izinleriyle korunur.

## Uygulama durumu

- Platform bakım ekranı, platform rolü ve granular izinlerle korunur.
- Servis sağlık özeti ve Hangfire kuyruk sayaçları canlı API'den okunur.
- Yinelenen görev kataloğu yalnız uygulamanın allowlist seed adlarını ve tenant kapsamını gösterir; payload ve istisna içeriği dışarı verilmez.
- Hangfire'ın etkileşimli dashboard'u artık yalnız doğrudan platform kiracısındaki `SuperAdmin` rolüne açıktır; impersonation/API key oturumu kabul edilmez.
- Görev tetikleme, yedekleme, kurtarma ve bakım penceresi aksiyonları henüz etkin değildir.

## Menü ve aksiyonlar

| Bölüm | Görüntüleme | Yönetim aksiyonları | Güvenlik sınırı |
| --- | --- | --- | --- |
| Genel Bakış | API, veritabanı, kuyruk, dosya deposu durumu; son başarılı yedek; başarısız görev sayısı | Sağlık kontrolünü yenile, olay ayrıntısına git | Sağlık uçları sır/bağlantı dizesi döndürmez; tenant verisi içermez |
| Yedekleme ve Kurtarma | Son yedekler, bileşen kapsamı, süre, boyut, checksum, saklama sonu, doğrulama sonucu | İzinli yedek planını şimdi çalıştır; doğrulama başlat; kurtarma tatbikatı kaydı aç | PostgreSQL ve MinIO/dosya deposu birlikte kapsanır; uygulama sunucusunda shell/SQL çalıştırılmaz; yedekler şifreli ve uygulama dışı depoda tutulur |
| Zamanlanmış Görevler | İzinli görev kataloğu, kapsam (platform/tenant), son/sonraki çalışma, süre, sonuç, korelasyon kimliği | Güvenli görevi çalıştır; başarısız idempotent çalışmayı yeniden dene; desteklenen planı duraklat/devam ettir | Sabit görev kataloğu ve doğrulanmış payload; kullanıcıdan serbest Hangfire tipi, cron veya payload kabul edilmez |
| Bakım Penceresi | Planlanmış/geçmiş pencereler, etkilenen bileşenler, bildirim zamanı | Pencere planla/değiştir/iptal et; kontrollü bakım modunu başlat/bitir | Her değişiklik denetim kaydı ve kullanıcı bildirimi üretir; acil kapatma yetkisi ayrı tutulur |
| Destek ve Tanılama | Servis olayları, maskelenmiş hata/korelasyon kayıtları ve destek erişimi geçmişi | Korelasyon kimliğine göre arama; erişim talebini incele; destek oturumunu sonlandır | Çalışan/ücret/izin içeriği varsayılan olarak gösterilmez; tenant desteği mevcut onaylı impersonation akışını kullanır |
| Denetim | Aktör, hedef, zaman, gerekçe, sonuç ve korelasyon kimliği | Filtrele ve dışa aktar (erişimi ayrıca yetkilendirilir) | Kayıtlar uygulama operatörünün sonradan değiştiremeyeceği append-only saklamaya alınır |

## Yetki modeli

Backend'de en az şu ayrı izinler tanımlanır:

- `platform.operations.view`
- `platform.backups.view`, `platform.backups.run`, `platform.backups.restore`
- `platform.jobs.view`, `platform.jobs.trigger`, `platform.jobs.manage`
- `platform.maintenance.view`, `platform.maintenance.manage`
- `platform.diagnostics.view`
- `platform.audit.view`, `platform.audit.export`

UI'daki görünürlük tek başına güvenlik kontrolü değildir. API her istekte platform personelini, aksiyon iznini, hedef kapsamı ve gerekiyorsa onayı doğrular. Her aksiyon için aktör, hedef, gerekçe, zaman, sonuç ve correlation/request ID kaydedilir; sırlar ve kişisel veriler loglara yazılmaz.

## Uygulama aşamaları

### Faz 0 — Menü, erişim sözleşmesi ve katalog

- Tamamlandı: platform bakım rotası, menü öğesi ve yol haritası kartları.
- Tamamlandı: platform izinlerinin kataloğa eklenmesi; API, personel kaydı ve granular izin doğrulaması.
- Tamamlandı: tipli seed adlarına dayalı read-only Hangfire kataloğu; Dashboard yalnız doğrudan platform SuperAdmin oturumuna kapalıdır.
- Kısmi: Hangfire ve veritabanı/Redis/MinIO health durumu gösterilir. Hangfire dışındaki `BackgroundService` ve outbox tüketicilerinin canlı durum kataloğu sonraki alt iştir.

### Faz 1 — Salt okunur operasyon görünümü ve güvenli görev tetikleme

- Kısmi: sağlık özeti, kuyruk sayaçları, recurring katalog ve son/sonraki çalışma durumu.
- Henüz yok: çalışma geçmişi, ayrıntı korelasyonu ve manuel çalıştırma/yeniden deneme.
- Mevcut recurring görevlerin tamamı veri değişikliği yapabilir veya bildirim gönderir; handler bazında idempotency/etki analizi bitmeden elle tetikleme eklenmeyecek.
- Her tetikleme için tenant kapsamı, kilit/çakışma kontrolü, actor ve correlation ID gereklidir.

Mevcut `IBackgroundJobService` işlemleri tenant kapsamlıdır. Platform genelindeki bir iş bu servisi tenant kimliği olmadan çağırmamalı; platform işleri için ayrı, dar yetkili bir uygulama servisi ve handler sözleşmesi gerekir. Hangfire dışındaki `BackgroundService` ve outbox tüketicileri ortak görev API'sinde varmış gibi gösterilmemeli; ayrı sağlık/işletim durumu sunulmalıdır.

### Faz 2 — Yedekleme görünümü ve çalıştırma

- Veritabanı ve nesne depolama için eşleşen yedek manifesti, şifreleme, checksum ve retention politikası.
- Zamanlanmış yedek, elle güvenli tetikleme, başarı/başarısızlık uyarısı ve düzenli geri yükleme doğrulaması.
- Yedek hedefi üretim veritabanı ve uygulama kümesinden bağımsız olmalıdır. Yalnız PVC/snapshot veya yalnız veritabanı kopyası tam kurtarma garantisi sayılmaz.

### Faz 3 — Bakım pencereleri ve bildirimler

- Zamanlama, etkilenen servis seçimi, tenant duyurusu, başlangıç/bitiş ve iptal akışı.
- Bakım modu için servis bazlı davranış ve health check istisnaları.
- Arka planda çalışan job ve outbox süreçlerinin bakım penceresindeki davranışının her iş türü için açıkça tanımlanması.

### Faz 4 — Kurtarma tatbikatı ve kısıtlı geri yükleme

- Önce izole ortama geri yükleme, bütünlük doğrulama ve RTO/RPO ölçümü.
- Üretime geri yükleme varsayılan yönetim ekranında doğrudan açılmaz. Ayrı `platform.backups.restore` yetkisi, gerekçe, ikinci operatör onayı, bakım penceresi, son yedek doğrulaması ve geri dönüş planıyla uygulanır.
- İşlem kullanıcı verisini değiştirebileceği için yürütme altyapı ajanında olur; UI yalnız imzalı/kimliği doğrulanmış bir operasyon talebi oluşturur ve durumu izler.

## İlk teslimatın kabul ölçütleri

- Menü ve doğrudan URL aynı platform erişim kontrolüne tabidir.
- Kartlardaki işlemler backend desteği olmadan çalışır görünmez; “Planlandı” etiketi açıkça görünür.
- Gerçek operasyon API'leri geldiğinde frontend'de kullanıcı kontrollü serbest komut, SQL, Hangfire tipi veya job payload alanı bulunmaz.
- Yedekleme kapsamı PostgreSQL ile dosya deposunu içerir; en az bir izole geri yükleme doğrulaması raporlanır.
- Hassas işlemlerin tamamı gerekçe ve correlation ID ile denetlenebilir.
