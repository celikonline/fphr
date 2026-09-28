# Genel rapor ekranı — tasarım

Tarih: 2026-09-28 · Dal: `a/rapor-ekrani` · Yol: mimari (backend sözleşmesi + web sayfası)

## Amaç

Raporlar menüsünde ("Rapor oluştur") bir rapor seçilince sonuç, Excel'e ek olarak **tam ekran bir sayfada tablo (grid)
olarak** açılsın; Excel bu sayfadaki **Excel indir** düğmesinden indirilsin. Ekran her rapor için ayrı yazılmaz:
katalogdaki mevcut ve gelecekteki tüm raporlar aynı bileşenle görünür. Başarı ölçütü: yeni bir `IReportProvider`
eklendiğinde ekran kodu yazmak gerekmez.

## Kapsam dışı

Sütun bazlı filtre, grafik, kayıtlı görünüm, PDF önizleme, asenkron üretim, filtrelenmiş alt kümeyi Excel'e aktarma
(Excel her zaman raporun tamamıdır). Bunlar istenirse ayrı iş olur.

## Bugünkü durum

- `web/src/pages/reports.tsx`: katalog + sağ panelde `ReportRunPanel` (filtre formu + **Oluştur** → sunucu üretir →
  Excel'i doğrudan indirir) + "Son oluşturduklarım".
- `IReportProvider.GenerateAsync` yalnızca hazır `.xlsx` (`ReportFileResult`) döndürür; sağlayıcılar veriyi
  `ExportTable` olarak kurup `IReportExporter` ile Excel'e çevirir. Dosya `ReportExecution`'da saklanır.
- `RequestReportCommand` her üretimde "rapor hazır" e-postası gönderir.

## Tasarım

### 1. Akış

1. Katalog panelindeki **Oluştur** düğmesi **Raporu aç** olur ve `/app/reports/:key?<filtreler>` adresine gider.
   Panelden doğrudan Excel indirme ve panelin e-posta notu kalkar (indirme artık sayfadadır; geçmiş tablosundaki
   indirme aynen kalır).
2. Sayfa açılınca zorunlu filtreler tamsa raporu **otomatik getirir** (yeni execution). Sonuç gelince adres
   `?execution=<id>` ile güncellenir (`replace`), böylece yenileme ve bağlantı paylaşımı gridi geri getirir.
3. Filtreler sayfanın üstünde düzenlenebilir; **Uygula** raporu yeniden getirir. **Excel indir**, gösterilen
   execution'ın dosyasını indirir (yeniden üretmez) — grid ile Excel aynı veriden gelir.
4. Filtre değerleri URL'de yalnızca `orgUnitIds`, `year`, `month`, `from`, `to`, `includeTerminated`
   anahtarlarıyla taşınır (kimlik/kişisel veri yok).

E-posta bağlantısının (`/app/reports?execution=<id>` → otomatik indirme) mevcut davranışı değişmez.

### 2. Backend

- **Port:** `IReportTableReader` (`Sense.Shared.Contracts/Export`) — `IReportExporter`'ın tersi. Uygulama
  `Sense.Shared.Infrastructure/Export` altında, ClosedXML ile `ExcelReportExporter`'ın yanında; Reporting modülü
  yalnızca Contracts'a bağlı kalır, yeni paket referansı gerekmez.
  - Okuma kuralı: 1. satır başlık; sayfa başına sütun tipi = o sütunun ilk dolu hücresinin tipi (karışıksa `Text`);
    tarih hücresi biçimi saat içeriyorsa `DateTime`, içermiyorsa `Date`.
- **Uç:** `GET /v1/reports/executions/{id}/data?sheet=0&page=1&pageSize=50&q=&sort=<sütun-indeksi>&desc=false`
  - İzin ve sahiplik/"hazır mı" kontrolleri `GetReportExecutionFileQuery` ile aynıdır (`reports.my.executions`).
  - Yanıt: `sheets[] {index, name}`, `columns[] {header, type}`, `rows[][]` (o sayfa), `total`, `page`, `pageSize`.
    Hücreler: metin → string, sayı → number, `Date` → `yyyy-MM-dd`, `DateTime` → ISO 8601, boolean → bool, boş → null.
  - `q`: tüm hücre metinlerinde büyük/küçük harf duyarsız (tr kültürü) arama. `sort`: tek sütun, tipe göre karşılaştırma,
    boşlar sonda. Dosya bellekte zaten ayrıştırıldığından arama/sıralama/sayfalama sunucuda yapılır.
  - Geçersiz `sheet`/`sort` → doğrulama hatası. Dosya `.xlsx` değilse (içerik tipi) → "önizleme yok" doğrulama hatası;
    sayfa bu durumda yalnızca **Excel indir** sunar. Hangi sağlayıcıların xlsx dışı çıktı verdiği planda kodla doğrulanır.
- **`notify` bayrağı:** `RequestReportRequest`/`RequestReportCommand`'a `Notify` eklenir (varsayılan `true` → mevcut
  istemciler etkilenmez). Yeni sayfa `false` gönderir; kullanıcı zaten ekrana bakıyordur.
- Sağlayıcılara, izinlere ve veri kapsamı mantığına dokunulmaz.

### 3. Web

- **`ReportParameterForm`** (`web/src/components/reports/`): `ReportRunPanel` içindeki parametre formu buraya çıkarılır
  ve katalog paneli ile yeni sayfa aynı formu kullanır (kopya yok). `reports.tsx` küçülür.
- **`report-viewer.tsx`** (`web/src/pages/`, rota `reports/:key`, `App.tsx`'te `reports` satırının altına eklenir):
  - Üst: geri bağlantısı, rapor adı/açıklaması, filtre kartı (**Uygula**, **Excel indir**).
  - Alt: birden çok sayfa varsa sekmeler; arama kutusu; dinamik sütunlu `DataTable` (sayı sağa hizalı, tarihler locale'e
    göre biçimli, boolean evet/hayır); `PaginationFooter` ile sunucu sayfalama. Mobilde yatay kaydırma.
  - Durumlar: yükleniyor, üretim hatası (execution'ın hata mesajı), "kayıt yok", önizlenemeyen dosya (yalnız indirme),
    yetki yok (`NoAccess`).
  - React StrictMode'da çift üretimi önlemek için otomatik getirme bir `ref` ile bir kez tetiklenir. `?execution=`
    varsa ve o rapora aitse yeniden üretmeden yüklenir; bulunamazsa (saklama süresi doldu) filtreler tamsa yeniden üretilir.
- **Servis:** `reports.service.ts`'e `getReportExecutionData(...)` ve `requestReport(..., { notify })` eklenir.
- **i18n:** `web/public/locales/*/reports.json` dosyalarında mevcut tüm dillere yeni metinler.
- UI koduna başlamadan `frontend-standards` skill'i çağrılır (Mantine bileşenleri, mevcut sayfalarla tutarlı görünüm).

### 4. Test

- **Backend:** exporter → reader gidiş-dönüş (tipler, çok sayfa, boş hücre, tarih/tarih-saat); veri ucu handler testleri
  (başkasının execution'ı, hazır olmayan, xlsx dışı, geçersiz sayfa/sütun, arama, sıralama, sayfalama); `notify=false`
  e-posta göndermez, varsayılan gönderir.
- **Web:** viewer sayfası (otomatik getirme, `?execution` ile geri yükleme, hata, sayfalama, Excel indir); mevcut
  `reports.test.tsx` güncellenir (Oluştur → Raporu aç).
- Bitirmeden: test kapısı (`node .claude/takim/betikler/test-kapisi.mjs`) ve `takim-gozden-gecirici`.

## Riskler

- Büyük raporlar her veri isteğinde bellekte ayrıştırılır. Sayfa boyutu üst sınırla kısıtlanır; ölçüm sorun gösterirse
  ayrıştırma sonucu kısa süreli önbelleğe alınır (bu turda yok).
- Okuyucu, başlığın 1. satırda olduğunu varsayar; `ExcelReportExporter` dışında xlsx üreten bir sağlayıcı varsa planda ele alınır.

## Dokunulacak yerler (özet)

`Sense.Shared.Contracts/Export/IReportExporter.cs` (yeni port), `Sense.Shared.Infrastructure/Export/` (okuyucu + DI),
`Sense.Modules.Reporting.Application` (`GetReportExecutionDataQuery`, `RequestReportCommand.Notify`),
`Sense.Modules.Reporting.Api/Controllers/ReportsController.cs`, `web/src/App.tsx`, `web/src/pages/reports.tsx`,
`web/src/pages/report-viewer.tsx`, `web/src/components/reports/report-parameter-form.tsx`,
`web/src/services/reports.service.ts`, `web/public/locales/*/reports.json`, ilgili testler.
