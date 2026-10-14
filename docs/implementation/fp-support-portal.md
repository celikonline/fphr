# Merkezi FP Support Portal bağlantısı

FPHR’nin mevcut destek kayıtları ve yerel talep kimlikleri korunur. Yeni kayıt `SupportTicketCreated` outbox olayıyla Sense.Worker tarafından merkezi portala gönderilir. Portal kimliği mevcut `ExternalRef` alanında `fp-support:<UUID>` olarak saklanır; şema migration’ı yoktur.

API ve Worker süreçlerinde aynı sunucu ortam değerlerini tanımlayın:

```dotenv
FP_SUPPORT_PORTAL_URL=https://support.example.com
FP_SUPPORT_TENANT_ID=SUPPORT-TENANT-UUID
FP_SUPPORT_PROJECT_ID=FPHR-SUPPORT-PROJECT-UUID
FP_SUPPORT_API_KEY=PRODUCT-CUSTOMER-BRIDGE-SECRET
```

Anahtar merkezi portalda proje yöneticisinin MFA ile oluşturduğu **Ürün müşteri destek köprüsü** anahtarıdır. Ayrı FPHR destek projesi ve ürün sunucu hesabı kullanın; anahtar yenilemede hesabı/projeyi koruyun. Anahtar tarayıcıya veya loglara gönderilmez. URL HTTPS origin olmalıdır; yalnız yerel loopback için HTTP kabul edilir. URL path/query/credential alanları ve yönlendirmeler kabul edilmez. URL boşsa mevcut e-posta desteği sürer; eksik/bozuk etkin bağlantı yapılandırması başlangıçta reddedilir.

Yeni talepler kalıcı yerel kayıt + outbox işleminde kaydedilir. Portal tesliminde idempotency anahtarı sabit yerel talep Guid’idir. Ağ hatası yutulmaz: mevcut outbox yeniden deneme/dead-letter mekanizması kullanılır. Merkezi kayıt başarılı, yerel referans kaydı başarısız olursa aynı anahtarla tekrar aynı portal talebi alınır. Worker’ın dead-letter kuyruğu mevcut işletim akışında takip edilmelidir. Bağlantı açılmadan önce tamamlanmış e-posta outbox olayları kendiliğinden yeniden gönderilmez; tarihsel toplu aktarım bu değişiklikte yapılmaz.

Müşteri kapsamı yerel kaydın doğrulanmış `TenantId` ve `RequestedByUserId` alanlarından `tenant:<UUID>:user:<UUID>` biçiminde oluşturulur. Başka kullanıcıya ait talepte yetkisiz detail veya takip yanıtı HTTP çağrısı portala ulaşmadan reddedilir. Kiracı destek yöneticisinin mevcut okuma yetkisi korunur; takip mesajı yazma yalnız talep sahibine açıktır. Merkeze bağlanan talebin eski yerel respond/close işlemleri reddedilir; yanıt/durum yönetimi merkezi PO akışına geçer.

Mevcut `/v1/support/tickets/mine` cevabına isteğe bağlı `portal` alanı eklenir. Merkezi durum yerel `open/answered/closed` enumundan ayrı taşınır. Liste bir müşteri için sayfalı toplu istek kullanır; her talebin son 5 açık mesajını alır. En fazla 20 sayfa taranır; daha eski kaydın tam bilgisi detail isteğiyle alınabilir. Kesilmiş mesaj geçmişinde **Tüm konuşmayı göster** yerel yetkili detail endpoint’inden tam açık geçmişi getirir. İç notlar müşteriye verilmez; altyapı adaptörü de `visibility=public` filtreler.

Destek ekranı 20 saniyede bir kullanıcı/tenant kapsamlı sorguyu yeniler. Portal erişilemiyorsa yerel kayıt görünür kalır ve bağlantı durumu gösterilir. AI müşteri agentı ve PO onaylı çözüm ayrı etiketlenir. Müşteri aktif talebe takip yanıtı gönderebilir; hata halinde metin ve idempotency UUID korunur. `resolved`, `closed`, `cancelled` durumlarında form kapalıdır.

İş kartı/sözleşme: `docs/takim/isler/FP-SUPPORT-001.json`, `docs/takim/handoff/FP-SUPPORT-001.md`. Backend bridge testleri ve web davranış testleri eklendi. Argümansız takım test kapısı ve bağımsız gözden geçirme sonucu handoff kaydında raporlanır; teknik derleme sonucu tam depo test kapısı yerine gösterilmez.
