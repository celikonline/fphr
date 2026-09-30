# Handoff — Çalışan kartı PDF çıktısı

- **Tarih:** 2026-09-28
- **Dal:** `dev`
- **Commit aralığı:** `ececbcee..4ccb58d7` (12 commit)
- **Spec:** `docs/superpowers/specs/2026-09-28-employee-card-pdf-design.md`
- **Plan:** `docs/superpowers/plans/2026-09-28-employee-card-pdf.md`

## Ne yapıldı

Çalışan kartı başlığına "PDF olarak indir" butonu eklendi. Diyalogda iki seçim var:
- **Sekmeler:** kullanıcının görebildiği sekmeler; "Diğer" hariç.
- **Dönem:** Son 12 ay, Bu yıl, Tüm zamanlar ya da Özel.

PDF sunucuda üretilir (QuestPDF).

**Uç noktalar**
- `GET api/v1/employees/{id}/card-pdf?sections=a,b&from=yyyy-MM-dd&to=yyyy-MM-dd`: `employee.employees.read` yetkisi ister ve veri kapsamı uygulanır (`IScopedRequest`).
- `GET api/v1/employees/me/card-pdf?...`: `employee.my.view` yetkisi ister ve yalnız kendi kartını döner.

**Mimari**
- Sözleşme `Employee.Contracts/CardPdf` altında: `IEmployeeCardPdfSection` ve `EmployeeCardPdfFormat`.
- Her modül kendi bölümünü kendi Infrastructure katmanında yazar. Mevcut sorgularını `IDispatcher` ile çağırdığı için yetki, kapsam ve maskeleme aynen uygulanır.
- `EmployeeCardPdfComposer` bölümleri toplar, modül açık/kapalı durumunu kontrol eder ve PDF'i Türkçe kültürle üretir.

| Bölüm | Sağlayıcı |
|---|---|
| general, personal, customFields, career, payments (maaş/banka), account | Employee |
| leave, overtime | Leave |
| payroll | Payroll |
| assets | Assets |
| documents | Documents |
| payments (masraf/avans) | Expense |

## Doğrulama

- Her task'a birim testi yazıldı ve task bazında review'dan geçti. Ardından tüm branch incelendi, düzeltme turu yapıldı ve düzeltmeler yeniden incelendi.
- Test kapısı 3088ebf1'de YEŞİL geçti (4177 test). Son hâl `takim-gozden-gecirici` ile yeniden koşuldu.
- **Canlı kontrol** (compose, demo tenant, temiz `git archive HEAD` imajı):
  - Tüm bölümlerle 4 sayfalık PDF oluştu. Türkçe karakterler doğru, dosya adı `calisan-karti-ahmet-celik-2026-09-28.pdf`.
  - Bilinmeyen bölüm, ters dönem ve boş seçim 400; olmayan çalışan 404 döndü.
  - Canlı kontrolde görülen ham izin türü anahtarı (`leave.type.annual`) düzeltildi.

## Bilinen sınırlamalar

- **Kayıt tavanı:** izin ve fazla mesai listeleri kaynak sorgunun `PagingDefaults.MaxPageSize` sınırıyla gelir. Sınıra ulaşılırsa PDF'e "Yalnızca en yeni N kayıt gösterildi" notu düşer.
- **Kendi kartında eksik kalanlar:** self-service sorgusu olmadığı için sendika üyelikleri, istihdam dönemleri, bordro profili ve icra/nafaka emirleri yer almaz.
- **Kapsam dışı:** Attendance (çalışma takvimi), eğitim, fotoğraf/logo ve İngilizce PDF.
- **Tarih/saat:** saat dilimi `Europe/Istanbul` olarak sabit; tenant saat dilimi kullanılmıyor. "Son davet" tarihi UTC günüdür.
- **Sicil no:** detay DTO'sunda alan olmadığı için özet bloğunda sicil numarası yok.
- **Performans:** çalışan detayı bir PDF'te 4 kez okunur (General, Personal, CustomFields, Account).
- **Zimmet:** sorgu geçmiş tutmadığı için yalnız güncel zimmetler görünür, ekrandakiyle aynı.

## Ajanın kullanıcı adına verdiği kararlar

Kullanıcı "kararları kendin ver" dedi. Ayrıntılar spec §2'de ve SDD ledger'ında. Özet:
- Sözleşme `Shared.Contracts` yerine `Employee.Contracts`'a kondu (`IReportProvider` deseni).
- Sonuç tipi soyut hiyerarşi yerine tek record; `Blocks == null` "gizli" demek.
- Spec'teki API entegrasyon testi yerine gerçek yazıcıyla composer testi ve canlı kontrol yapıldı.
- Bordro enum etiketleri `employees.json` (`payrollTab.enum.*`) metinlerinden alındı.
- PDF metinleri çağıranın dilinden bağımsız olarak tr-TR üretilir.

## Takip önerileri

- `downloadWorkCertificate` da aynı blob hata gövdesi sorununu taşıyor: sunucu mesajı kullanıcıya ulaşmıyor.
- `unwrapBlobError` 4 serviste tekrarlanıyor; ortak yardımcıya çekilebilir.
- Handler/controller testleri eksik (`/me` çözümlemesi, kapsam dışı 404). Personal/Account bölümleri ve maskeli TC kimlik için regresyon testi yok.
