# Çalışan kartı PDF çıktısı — tasarım

- **Tarih:** 2026-09-28
- **Durum:** Taslak. Kullanıcı "kararları kendin ver" dedi. Aşağıdaki kararların bir kısmı kullanıcıyla konuşuldu, bir kısmını ajan verdi; her birinin yanında belirtildi.
- **İstek:** `/app/employees/:id/general` çalışan kartında çalışanın tüm bilgileri PDF olarak alınabilsin. Kullanıcı üstteki sekmeleri seçip çıkarabilsin.

## 1. Amaç ve başarı ölçütü

Kartı görebilen kullanıcı:
1. Kart başlığındaki "PDF" butonuna basar.
2. Görebildiği sekmelerden istediklerini ve bir dönemi seçer.
3. Sunucuda üretilmiş tek bir PDF indirir.

PDF, kullanıcının ekranda göremeyeceği hiçbir veriyi içermez. Yetki, veri kapsamı, modül açık/kapalı durumu ve hassas alan maskelemesi aynı kalır.

**Başarı ölçütleri:**
- Seçilen her sekme PDF'te kendi başlığıyla bir bölümdür. Seçilmeyenler yer almaz.
- Yetkisiz ya da modülü kapalı bir sekme istense bile PDF'e girmez. Sunucu web'e güvenmez.
- Dönem, olay türü listeleri süzer. Durum türü veriler güncel haliyle gelir (§4).
- Türkçe karakterler doğru basılır.
- Dosya adı `calisan-karti-<ad-soyad>-<yyyy-MM-dd>.pdf` biçimindedir.

## 2. Verilen kararlar

| Karar | Seçim | Kim |
|---|---|---|
| Üretim yeri | Sunucu (QuestPDF, mevcut `ISimplePdfWriter`) | Kullanıcı |
| Kapsam | Seçilebilir dönem (C) | Kullanıcı |
| Mimari | Bölüm sağlayıcıları (yaklaşım 1) | Kullanıcı |
| Uç nokta ve "kapalı bölüm sessizce düşer" | Bölüm 1 | Kullanıcı |
| Sağlayıcı/sekme eşlemesi, dönem kuralı, 500 satır sınırı | Bölüm 2 | Ajan (kullanıcı yetki devretti) |
| Sözleşmenin yeri | `Employee.Contracts`. Bölüm 1'de `Shared.Contracts` denmişti; `IReportProvider` deseni gereği değişti. | Ajan |
| Web arayüzü, dönem ön ayarları | §6 | Ajan |
| Dil | Türkçe, mevcut PDF'ler gibi | Ajan |
| Branch | Aynı çalışma dizini, `dev`, yalnızca kendi dosyaları | Ajan (hafızadaki kullanıcı tercihi) |

## 3. Mimari

```
web ──GET /employees/{id}/card-pdf?sections=..&from=..&to=..──► EmployeeDocumentsController
                                                                    │ Dispatcher.Query(GetEmployeeCardPdfQuery)
                                                                    ▼
                                          GetEmployeeCardPdfHandler (Employee.Application)
                                            ├─ IEnumerable<IEmployeeCardPdfSection>  ◄── Employee, Payroll, Leave,
                                            │    (Key + Order ile gruplanır)              Assets, Documents, Expense
                                            ├─ IModuleAccessService (modül kapalı → düşer)
                                            └─ ISimplePdfWriter → ExportFileResult
```

### 3.1 Sözleşme: `Sense.Modules.Employee.Contracts/CardPdf/`

```csharp
public static class EmployeeCardPdfSectionKeys   // web sekme anahtarlarıyla aynı
{ General, Personal, CustomFields, Career, Payments, Payroll, Documents, Assets, Leave, Overtime, Account; All (sıra) }

public sealed record EmployeeCardPdfRequest(Guid TenantId, Guid EmployeeId, bool IsOwnCard, DateOnly? From, DateOnly? To);

public abstract record EmployeeCardPdfSectionResult
{   Content(IReadOnlyList<SimplePdfBlock> Blocks)   // boş liste = "kayıt yok" satırı
    Unavailable                                        // yetki yok / kapsam dışı → bölüm düşer }

public interface IEmployeeCardPdfSection
{
    string Key { get; }          // EmployeeCardPdfSectionKeys değeri
    int Order { get; }           // aynı anahtarda birden çok sağlayıcı (payments: Employee 0, Expense 10)
    string? ModuleKey { get; }   // ProductModuleKeys değeri; null = çekirdek
    Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken ct);
}
```

Ortak biçimleme yardımcısı da buraya gelir: `EmployeeCardPdfFormat` (tr-TR tarih/para, `InPeriod`, `Table` ile 500 satır sınırı ve devam notu).

`Employee.Contracts` yalnızca Kernel ve `Sense.Shared.Contracts`'a bağlıdır; `SimplePdfBlock` oradan gelir. Diğer modüllerin `Employee.Contracts`'a referansı architecture testine (`Modules_TalkOnlyThroughContracts`) uygundur.

### 3.2 Uç noktalar: `EmployeeDocumentsController`

**`GET api/v1/employees/{id}/card-pdf?sections=general,leave&from=yyyy-MM-dd&to=yyyy-MM-dd`**
- Sorgu `GetEmployeeCardPdfQuery(Id, Sections, From, To)`.
- `[RequiresPermission(employee.employees.read)]` ve `IScopedRequest`. Kapsam dışı hedef 404 döner.

**`GET api/v1/employees/me/card-pdf?...`**
- Sorgu `GetMyEmployeeCardPdfQuery`, `[RequiresPermission(employee.my.view)]`.
- Çalışan `SelfEmployeeResolver` ile çözülür ve `IsOwnCard = true` olur.

**Doğrulama:**
- `sections` boş olamaz, bilinmeyen anahtar 400 döner.
- `from > to` 400 döner.
- Tarihlerin ikisi de boşsa tüm zamanlar alınır.

### 3.3 Handler akışı

1. Çalışanı okur. Yoksa 404 döner.
2. İstenen anahtarları `EmployeeCardPdfSectionKeys.All` sırasına dizer.
3. Her anahtarın sağlayıcılarını `Order` sırasıyla çalıştırır. `ModuleKey` doluysa önce `IModuleAccessService.IsEnabledAsync` çağrılır; modül kapalıysa sağlayıcı atlanır.
4. Anahtarın tüm sağlayıcıları `Unavailable` ya da atlanmışsa bölüm düşer. Aksi halde `SimplePdfSectionHeading`, ardından bloklar gelir. Blok yoksa "Bu bölümde kayıt yok." paragrafı basılır.
5. Hiç bölüm kalmazsa 403 döner (`employee.cardPdf.noSections`).
6. Belgenin ilk bloğu çalışan özetidir: ad soyad, sicil, dönem metni ("Dönem: 28.09.2025 – 28.09.2026" ya da "Tüm zamanlar").
7. Başlık "Çalışan Kartı"dır. Footer "Oluşturma: <tarih saat>" olur.
8. `ISimplePdfWriter.Write` çağrılır, sonuç `ExportFileResult` olarak döner.

### 3.4 PDF yazıcı eki (Shared)

`SimplePdfSectionHeading(string Text) : SimplePdfBlock` eklenir: dolgu arka planlı, kalın, 12 pt bir bant. Bölümler arasında sayfa kırılımı zorlanmaz.

## 4. Sağlayıcılar

Her sağlayıcı kendi modülünün **mevcut** sorgularını `IDispatcher` ile çağırır. Böylece şunlar aynen çalışır:
- `[RequiresPermission]`
- `IScopedRequest`
- maskeleme
- handler içi kapsam kontrolleri

Sorgu `Forbidden` ya da `NotFound` dönerse sağlayıcı `Unavailable` üretir. Handler'ında kapsam kontrolü olmayan bir sorgu için (örneğin zimmetler) güvence şuradan gelir: endpoint 1. adımda `employees.read` ile hedefin kapsamda olduğunu zaten doğruladı. `/me` modunda hedef her zaman kendisidir.

`IsOwnCard` ise sağlayıcı, modülün kendine ait `my.*` sorgularını kullanır (web'deki own-card davranışı).

| Anahtar | Sağlayıcı | İçerik | Dönemle süzülür |
|---|---|---|---|
| `general` | Employee | İş bilgileri, alt işveren | hayır |
| `personal` | Employee | Kişisel ve iletişim bilgileri (hassas alanlar maskeli), sendika (`union.view`) | hayır |
| `customFields` | Employee | Özel alanlar | hayır |
| `career` | Employee | Kariyer olayları, istihdam dönemleri | hayır |
| `payments` | Employee (0) | Maaş geçmişi (`salary.view`), banka hesapları (IBAN maskeli) | hayır |
| `payments` | Expense (10) | Harcama ödemeleri | evet |
| `payroll` | Payroll | Bordro profili, pusula özetleri, haciz emirleri | pusulalar |
| `documents` | Documents | Evrak gereksinimleri ve durumu (dosya içeriği yok) | hayır |
| `assets` | Assets | Zimmetler | teslim tarihi |
| `leave` | Leave | Bakiyeler; izin talepleri | talepler |
| `overtime` | Leave | Fazla mesai kayıtları | evet |
| `account` | Employee | Hesap durumu, erişim tipi, son davet | hayır |

"Diğer" sekmesi yalnızca kısayol olduğundan PDF'te yoktur. Attendance çalışma takvimi ve eğitim kapsam dışıdır.

**Dönem kuralı:** Yalnızca olay türü listeler süzülür. Kayıt `[From, To]` ile kesişiyorsa (başlangıç ≤ To ve bitiş ≥ From) listeye girer. Sorgu tarih filtresini desteklemiyorsa sağlayıcı bellekte süzer.

**Satır sınırı:** Her tablo en fazla 500 satır basar, en yeni kayıtlar önce gelir. Fazlası için "N kayıt daha var; dönemi daraltın." paragrafı eklenir.

## 5. Güvenlik ve hata yönetimi

- **Katmanlı yetki:** Endpoint `employees.read` + kapsam ya da `my.view` ister. Her bölüm ayrıca kendi yetkisini ister.
- **Modül kapalıysa bölüm düşer.** `ModuleAccessFilter` bu endpoint'i korumadığı için kontrol handler'da yapılır.
- **Hassas alanlar** ekrandaki kuralla maskelenir. PDF içeriği loglanmaz.
- **Beklenmeyen hata:** Sağlayıcı `Forbidden`/`NotFound` dışında bir hata dönerse ya da istisna fırlatırsa istek bütünüyle hata verir. Eksik bölümlü sessiz bir PDF üretilmez.

## 6. Web

- **Buton:** `employee-card.tsx` başlığına "PDF" butonu eklenir (`FileDown` ikonu). Kartı gören herkes görür.
- **Diyalog:** yeni `components/employees/employee-card-pdf-dialog.tsx`.
  - Sekmeler: `useVisibleEmployeeCardTabs()` listesinden `other` hariç, checkbox olarak. Varsayılan hepsi seçili. "Tümünü seç / Temizle" var.
  - Dönem: "Son 12 ay" (varsayılan), "Bu yıl", "Tüm zamanlar", "Özel". "Özel" seçilince iki tarih alanı açılır.
  - "PDF oluştur": seçim boşsa ya da özel aralık geçersizse pasif. Üretim sırasında loading gösterilir. Hata toast olarak çıkar.
- **Servis:** `employees.service.ts` içine `downloadEmployeeCardPdf(employeeId | "me", options)` eklenir. Dosya, blob olarak `downloadWorkCertificate` deseniyle indirilir.
- **i18n:** anahtarlar `employees` namespace'ine tr ve en olarak eklenir.

## 7. Test

- **Shared:** `SimplePdfSectionHeading` render testi.
- **Employee.Application:** handler birim testleri, sahte sağlayıcılarla:
  - sıra
  - `Unavailable` düşmesi
  - modül kapalı atlaması
  - hiç bölüm yokken 403
  - doğrulama 400'leri
  - `Order` birleştirmesi
- **Format yardımcısı:** `InPeriod` ve 500 satır sınırı testleri.
- **Her modül sağlayıcısı:** sahte `IDispatcher` ile şu testler:
  - `Forbidden` durumunda `Unavailable`
  - dönem süzmesi
  - own-card sorgu seçimi
- **Web:** diyalog için Vitest testi.
- **Kapı:** `node .claude/takim/betikler/test-kapisi.mjs` ve `takim-gozden-gecirici`.

## 8. Kapsam dışı

- Fotoğraf, logo, imza alanları
- İngilizce PDF
- Toplu (çok çalışanlı) PDF
- Attendance ve eğitim bölümleri
- Evrak dosyalarının kendisi
