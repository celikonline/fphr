# Zimmet / Varlık Yönetimi v2 — devir notu (2026-10-07)

Kapsam, boşluk analizi ve API sözleşmesi: [zimmet-modulu-v2-kapsam-2026-10-07.md](../analysis/zimmet-modulu-v2-kapsam-2026-10-07.md).
Kullanıcı çevrimdışıyken otonom kararlarla yapıldı; kararların gerekçesi o belgededir.

## Ne eklendi
- **Atama defteri** (`assets.asset_assignments`): kim, ne zaman, nasıl bitti (iade/kayıp), iade durumu + notu, teslim onayı.
  Defter yalnız `Asset` domain metotlarıyla yazılır; Excel içe aktarma da bu sayede defteri doldurur.
- **Künye alanları:** demirbaş no (kiracı içinde tekil), marka, model, alış tarihi/bedeli, garanti bitişi, durum (sağlam/orta/hasarlı).
- **Kayıp** durumu, **teslim onayı** ("teslim aldım"), **özet** ucu (`GET /assets/summary`), envanterde **arama**, **geçmiş** uçları.
- **Web:** özet kartları, arama, yeni alanlar, kayıp bildir, iadede durum+not, atama geçmişi, "Zimmetlerim"de teslim onayı ve geçmiş,
  çalışan kartında geçmiş; `pendingAssignment` durumu artık web tipinde var (v1 hatası).
- **Düzeltme (aynı gün):** Eğitim oturumunda katılımcı "Çıkar" (ve Tamamlandı/Etkinleştir/Kapat) `tenant.mismatch` hatası — kart senseik-066.
- **İK mesajı detayı:** tam ekran düğmesi ve uzun içerik sarması.

## Kanıt (bu çalışma ağacı)
- Tam test kapısı: derleme, lint, typecheck yeşil; .NET testlerinde yalnız kapsam dışı zengin-mesaj testi ve tek-başına-geçen bir import testi kırmızıydı.
- Zimmet: 187 test yeşil (yeni: ledger, V2 handler, entegrasyon, migration backfill, **iki kiracılı izolasyon** testleri).
- Eğitim: 38 test yeşil; düzeltme öncesi 5 test gerçek `AuditTenantInterceptor` ile kırmızıydı (kanıt kartta).
- Web: tüm Vitest yeşil.

## Doğrulanamayanlar (dürüst liste)
- **Canlı uçtan uca deneme yapılamadı:** Docker Desktop oturum sırasında çöktü (açılışta bayat `sailor-ingest.sock` hatası,
  yönetici yetkisi olmadan silinemiyor). Yerel API + tarayıcı turu ve Postgres isteyen son testler koşulamadı.
- **Yeni şirket ile tüm ekranların taranması (senseik-067) yapılmadı**; "tüm ekranlar çoklu kiracıya uyumlu" iddiası kanıtlanmış değildir.

## İnsan onayı gereken noktalar
- Migration `20261007121441_AssetLedgerAndDetails` (riskli alan). `Down()` defteri (ve teslim onaylarını) siler. Backfill her `Assigned`
  varlığa bir açık satır yazar; `assigned_employee_id` NULL olan bozuk satırlar atlanır.
- Gözden geçirici notları (düşük): eşzamanlı iki oluşturmada aynı demirbaş no 409 yerine 500 verebilir; `GET /my/history` çalışana
  iade/kayıp notunu da döndürür (ürün kararı); `ReturnAssetRequest` varsayılanı `good`; garanti rozeti istemci yerel gününü, özet UTC gününü kullanır;
  kayıp için entegrasyon olayı yok (Documents tutanak gereksinimi bayat kalabilir).

## Ertelenenler
Amortisman, bakım/onarım, barkod/QR, sarf stok yönetimi, kayıpta maaştan kesinti, mobil ekran (gerekçeler kapsam belgesinde).
