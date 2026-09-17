# İzin — kesirli gün saklama biçimi (yarım gün / saatlik)

Bu not, Leave modülünün yarım gün ve saatlik izni bakiyeye nasıl yansıttığını, Faz 7'nin (Payroll/Expense —
eksik gün/puantaj bordroya nasıl yansır) okuması için özetler. Kaynak kod: `src/Modules/Leave/Sense.Modules.Leave.Domain/`.

## Alanlar (`LeaveRequest`, tablo `leave.leave_requests`)
- `StartTime`/`EndTime` (`TimeOnly?`, sütun `time without time zone`): yalnız `LeaveUnit.Hourly` türünde dolu,
  ikisi de aynı anda dolu ya da ikisi de `null` olmak zorunda.
- `HalfDayPeriod` (`enum?` → `character varying(30)`): yalnız `LeaveUnit.HalfDay` türünde dolu (`Morning`/`Afternoon`).
  `StartTime`/`EndTime` ile **karşılıklı dışlayıcı** (`LeaveRequest.ApplyDetails` ikisi birden doluysa reddeder).
- `CalculatedDays` (`decimal`, sütun `numeric(9,4)`): türden bağımsız, **her zaman gün cinsinden** tek gerçek
  miktar alanı — kesirli izinlerde de bordronun/ledger'ın okuduğu tek kaynak budur, saat/yarım gün alanları
  yalnız UI'ye "nasıl seçildi" bilgisini geri vermek için taşınır.

## Kesir → gün dönüşümü (`LeavePartialDay`, `Sense.Modules.Leave.Domain.Calendar`)
- **Yarım gün:** sabit `0.5m` (`HalfDayFraction`) — hangi yarı seçilirse seçilsin (Morning/Afternoon) aynı.
- **Saatlik:** `(bitiş - başlangıç dakika) / günlük çalışma dakikası`, `decimal.Round(…, 4, AwayFromZero)`.
  Günlük çalışma dakikası şu an **sabit `450` dakika** (7.5 saat = 4857 md.63 "45 saat/hafta ÷ 6 gün") —
  `LeavePartialDay.DefaultDailyWorkMinutes`. Bu, `Overtime.OvertimeMath.DefaultDailyWorkMinutes` ile **aynı
  varsayılanı** paylaşır (tutarlılık için bilinçli); ikisi de "çalışanın gerçek atanmış günlük saatine göre
  bölme" TODO'sunu bekliyor — `IWorkScheduleReader` bugün yalnız işgünü/tatil bilgisi veriyor, kişi başına
  günlük dakika sözleşmesi yok. Faz 7 bordro hesaplarken bu sabiti mi yoksa çalışanın gerçek vardiya süresini mi
  kullandığını netleştirmeli; bugünkü kod ikisi için de aynı sabiti kullanıyor.
- Örnek: 09:00–11:00 arası 2 saatlik izin → 120/450 = 0.2667 gün (4 ondalık, yukarı yuvarlama yok, standart
  `AwayFromZero` — 0.26666… → 0.2667).

## Bakiyeye yansıma (`LeaveLedgerEntry`, tablo `leave.ledger_entries`, `Amount numeric(9,4)`)
- Onay tamamlandığında (`LeaveApprovalCompletedHandler`) `LeaveLedgerEntry.Usage(..., request.CalculatedDays, ...)`
  çağrılır — **kesirli değer aynen** (0.5, 0.2667 gibi) negatif işaretle (`-Math.Abs(days)`) ledger'a yazılır.
  Ayrı bir "saat" alanı ledger'da **yok**; her şey gün cinsinden tek hassasiyette (9,4) tutulur.
- İptal/ters kayıt (`LeaveLedgerEntry.Reversal`) aynı hassasiyetle pozitif işaretle offset eder.
- Bakiye projeksiyonu (`LeaveBalanceEngine.Project`) ledger satırlarını olduğu gibi toplar — kesirli günler
  birikimli bakiyeyi de kesirli bırakabilir (ör. çalışan yılda 3 kez 0.2667 gün saatlik izin alırsa bakiyesi
  ...,7999 gibi görünür); UI/rapor tarafı yuvarlama **göstermiyor**, ham `numeric(9,4)` değeri döner.

## Faz 7'nin bilmesi gerekenler (özet)
1. Tek kaynak alan: `LeaveRequest.CalculatedDays` / `LeaveLedgerEntry.Amount` — ikisi de `numeric(9,4)`, 4 ondalık.
2. Saat bilgisi (`StartTime`/`EndTime`/`HalfDayPeriod`) yalnız **görüntüleme** amaçlı; bordro hesaplarken bu
   alanlardan tekrar dakika/saat türetmeye gerek yok, `CalculatedDays` zaten nihai kesirli gün sayısıdır.
3. Günlük çalışma dakikası bugün **sabit 450** (`LeavePartialDay.DefaultDailyWorkMinutes`) — kişiye özel vardiya
   süresi henüz bağlanmadı (bkz. yukarı). Faz 7 bordro/puantaj tarafında kendi günlük saat kaynağıyla (varsa)
   tutarsızlık çıkabileceğini göz önünde bulundurmalı; şimdilik aynı sabiti paylaşmak en tutarlı seçenek.
4. Yuvarlama: `MidpointRounding.AwayFromZero`, 4 ondalık, her iki alanda (`CalculatedDays`, ledger `Amount`) da
   `decimal.Round(x, 4)` ile tek noktadan uygulanıyor (`LeaveRequest.ApplyDetails`, `LeaveLedgerEntry` ctor).

## Devamsızlık (Absence) ile ilişkisi — bkz. ayrı model
Devamsızlık (`AbsenceRecord`, `leave.absence_records`) izin bakiyesine **hiç dokunmaz** — ayrı, append-only bir
kayıt (bkz. `AbsenceFeatures.cs`). `Hours` alanı orada da `decimal?` ama gün kesrine çevrilmez; ham saat olarak
durur (`late` türünde "kaç saat geç kaldı" bilgisi). Faz 7 devamsızlığı "eksik gün" olarak okuyacaksa
`Leave.Contracts.IAbsenceReader` üzerinden `Kind`/`Hours`/`Date` alır, kendi gün dönüşümünü kendisi yapar —
Leave modülü bu dönüşümü yapmıyor (kapsam dışı, `docs/implementation/leave-kesirli-gun.md` bu notu Faz 7 için
burada bırakıyor).

**S-21 (ADR-0020, 2026-09-17):** `Leave.Absence` (`AbsenceRecord`), "devamsızlık günü"nün **tek otoriter
kaynağıdır**; Attendance modülünün `AttendanceAnomaly`'si yalnız operasyonel bir sinyaldir, hiçbir raporda
devamsızlık olarak sayılmaz ve **otomatik olarak `AbsenceRecord`'a dönüşmez** — anomaliden devamsızlık kaydına
geçiş yalnız İK'nın elle `POST .../absences` çağrısıyla olur. Faz 7/10 gibi tüketiciler devamsızlık sayısını
her zaman `Leave.Contracts.IAbsenceReader` üzerinden okumalı, Attendance anomali kayıtlarını devamsızlık
sinyali olarak kullanmamalıdır.

**Yarım gün / saatlik izin (ilk sürüm dışı, backlog):** `LeaveUnit` (FullDay/HalfDay/Hourly) şu an domain'de
tür özelliği olarak (`LeaveType.Unit`) modellenmiş, talep ('LeaveRequest') ise yalnız tam gün varsayımıyla
üretilmektedir. Gerekçeler: (1) talep formunda saat/yarım gün seçici UI (hazırlanıyor), (2) çalışanın gerçek
günlük çalışma saatine bağlı kesir hesabı (`IWorkScheduleReader` geçişi, şu an sabit 450 dakika), (3) bordro
entegrasyonu (`Payroll.Contracts` kesirli gün işlemesi). İlk sürümde türün tüm talepleri tam gün beklenir;
yarım/saatlik talep kaydı teknik olarak mümkün ama önerilmez. Backlog: Faz 7 kesirli gün desteğiyle birlikte talep
düzeltmesi ve form seçimi eklenmesi (bkz. `docs/analysis/` faz 05 envanteri).
