# Teslim öncesi uçtan uca test raporu (2026-09-19)

Kapsam: İK uzmanı gözüyle izin, fazla mesai, PDKS/puantaj/vardiya, bordro/tazminat, harcama/avans, zimmet, işe alım, özlük,
belge, organizasyon, kimlik/yetki, onay akışları, bildirim, performans, raporlama. Demo hesaplar: owner, ik, bordro,
yonetici, calisan, denetci (`Demo123!`). Yöntem: canlı compose yığını (API + Worker + web + Postgres) üzerinde API ve
web taraması; bulguların kök nedeni kodda doğrulandı, her düzeltme regresyon testiyle `qa/teslim-testi` dalında.

## Yayın engelleyiciler (düzeltildi)

| Sorun | Etki |
|---|---|
| Worker açılışta çöküyordu (DI: saat dilimi lookup, Leave şirket defteri handler'ı, Payroll engelli lookup'ı) | Outbox boşalmıyor: bildirim, onay sonuçları, işe alım yan etkileri hiç çalışmıyor |
| `GET /identity/me` NullReferenceException (mevcut DB'de `{}` jsonb) | Güncellenen DB'de kimse uygulamayı açamaz |
| Demo seed boş DB'de çöküyordu (Türkçe karakterli e-posta) | API açılmıyor |
| Harcama/avans onayı Worker'da işlenmiyordu | Onaylanan harcama `Submitted`'ta kalıyor, ödenemiyor |
| Çıkış yapan çalışanın hesabı kapanmıyordu (Worker'da Identity handler'ı yok) | İşten çıkan çalışan giriş yapabiliyor |
| Employee (mentor) ve Training migration'ları yoktu | Boş DB'de Training şeması/kolon yok |
| Scriban 6.6.0 (kritik/yüksek sandbox-escape açıkları) | 7.4.0'a yükseltildi |

## Diğer düzeltilenler (özet)

- **Kimlik/güvenlik:** hesap kilidi ve refresh-token reuse iptali artık kalıcı, login/by-token hız sınırı, rol izni yükseltme
  (kendi sahip olmadığın izni verme, sistem rolü boşaltma) kapatıldı, `override-decide` ile self-approval engellendi,
  kullanıcı numaralandırma, parola politikası, model-binding hata mesajı sızıntısı, davetli kullanıcı oluşturma (404), boş
  profil PUT'unun veriyi silmesi, KPI 500'leri.
- **Veri kapsamı:** yönetici ekip dışı çalışanın puantaj/vardiya/belge gereksinimi/hedef verisini okuyamaz.
- **İzin:** tek Cuma iznine fazladan Cumartesi düşülmesi; eksik `outcome` ile talebin onaylanması; fazla mesai geçersiz
  ücret türü ve gelecek tarih.
- **PDKS:** resmi tatil/onaylı izin günleri "eksik gün" sayılmıyor; vardiyada 11 saat dinlenme, gece vardiyası (7,5 saat),
  minimum mola; gelecek tarihli/24 saati aşan puantaj; başlamamış ay onayı.
- **Bordro:** kümülatif GV matrahı devri, kıst maaş (işe giriş/çıkış ayı), dönem maaşı, çalışan bazlı hata izolasyonu
  (`skipped` + neden), 2026 yasal parametreleri (asgari ücret 33.030, GV dilimleri, SGK tavan, kıdem tavanı yarıyıl satırları),
  tazminat/dönem 500'leri → 4xx, işe girişte bordro profili, çalışanın harcama kategorilerini görmesi, harcama tutar/para
  birimi/tarih doğrulamaları, zimmet bütünlüğü.
- **Yaşam döngüsü:** organizasyon ağacında döngü, 15 yaş altı/mantıksız tarihler, belge yükleme uzantı/dosya adı kısıtı,
  mükerrer iş e-postası ve aday e-postası, SGK çıkış kodu doğrulaması ve yanlış kodların açılışta düzeltilmesi, telefon formatı.

## Karar/teyit bekleyenler (kodla çözülmedi)

1. **Mali müşavir teyidi:** yemek istisnaları (GV günlük 240 TL, SGK oranı), engelli indirimi tutarları, 30 gün esaslı kıst
   hesabında asgari ücret istisnasının orantılanması. 2025 ve öncesi parametre satırları yok (o dönemler 422 döner).
2. **İş sahibi kararı:** çalışan/yönetici duyuruları okuyamıyor (`announcements.announcements.view` yalnız yönetim rollerinde);
   `denetci` self-servis yazma (izin talebi, check-in) yapabiliyor; ileri tarihli işten çıkış anında `Terminated` oluyor
   (bilinçli tasarım, PO G-3-09); İK rolü `/payroll/*` erişimine sahip değil.
3. **Bilinen sınırlamalar:** aday KVKK açık rızası kalıcı saklanmıyor (yalnız log); adaydan çalışana dönüştürme (Hired)
   yan etki üretmiyor; net maaşlı çalışan için net→brüt çözücü yok (bordroda nedenle `skipped` listesine düşer);
   puantajdaki fazla mesai Leave'in onaylı kayıtlarından bağımsız; GPS check-in `locationId` verilmezse geofence atlanır;
   onay e-postasında izin tarihi/gün sayısı/ret gerekçesi yok; e-posta son geçerlilik saatleri UTC; vardiya silme ucu yok;
   audit log alan bazlı değişiklik özeti içermiyor.
4. **Dev dalından gelen, bu çalışmanın dışındaki kırmızı testler:** `ProductModules` kataloğunda Training yok
   (`EveryHostedModule_IsInTheProductModuleCatalog`), DataTransfer şablon kolon sayısı testleri, mimari/kontrat/tenant-izolasyon
   konvansiyon testleri (`PermissionCatalogs_DoNotExposeUntranslatedEnglishDisplayNames`, `IntegrationTraitConventionTests`).

## Kurulum notları

- `docker compose up -d --build`, ardından Migrator: `dotnet run --project src/Sense.Migrator -- migrate` (Migrator'da `seed`
  komutu yoktur; demo seed API açılışında `Seeding__Enabled=true` ile çalışır).
- API açılışında `TerminationReasonCorrectionSynchronizer` yanlış SGK çıkış kodlarını düzeltir.
- Demo veri notu: seed API açılışında Worker'dan önce çalışır; çalışma takvimi atamaları sonradan gelir (bkz. seed düzeltmesi).
