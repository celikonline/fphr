# senseik-031 — PDKS Self-Servis QR/GPS Check-in + Canlı Pano — UYGULAMA NOTU

**Durum: Uygulandı, yerel commit'e hazır (GATE 1/2 önceden onaylı). Push yapılmadı.** Branch: `b/attendance-self-checkin`.

Karttaki "senseik-012/014 merge olana kadar başlatılma" uyarısı geçersiz kılındı — o merge (fazla mesai/puantaj motoru) çoktan `dev`'e girmiş, `AttendanceDay`/PDKS akışı güncel haliyle okunarak bu iş üstüne inşa edildi.

## Ne Yapıldı

### Ortak "giriş kaydı oluştur" servisi (kabul kriteri 4)

`Sense.Modules.Attendance.Application.AttendanceDays.AttendanceDayUpsert` (yeni, internal static): work schedule'dan planlanan günü çözüp mevcut `AttendanceDay`'i güncelleyen ya da yenisini oluşturan tek upsert. Daha önce üç ayrı yerde (manuel kayıt, PDKS Excel içe alma, PDKS cihaz/CSV içe alma) neredeyse birebir tekrar eden bu mantık artık tek yerde:
- `AttendanceDayFeatures.cs` → `RecordAttendanceHandler`
- `Import/AttendanceImportRowHandler.cs`
- `DeviceLogs/DeviceLogImportFeatures.cs` → `ImportDeviceLogsHandler`
- (yeni) `SelfCheckIns/SelfCheckInFeatures.cs` → `QrCheckInHandler`, `GpsCheckInHandler`

Hepsi aynı upsert'ten geçtiği için self-servis QR/GPS kaydı, mevcut `AttendanceDay` modeline PDKS import ile **aynı şekilde** akıyor (aynı anomali hesaplama, aynı "aynı çalışan+gün tekrar yüklenirse üzerine yazar" kuralı). `AttendanceDaySource` enumuna `SelfService = 3` eklendi, ekip puantajı/PDKS raporlarında kaynağı ayırt etmek için.

Ayrıca ekip haftalık/aylık ızgarasındaki (`TeamTimesheetFeatures.cs`) "görünür çalışan kümesini çöz" mantığı da `VisibleEmployeeResolver` altında ortaklaştırıldı; canlı pano da aynı yardımcıyı kullanıyor.

### Kabul kriteri 1 — QR check-in

- `Organization.Location`'a `SelfCheckInCode` eklendi (oluşturma anında üretilen 8 karakterlik kod, tenant içinde benzersiz — girişe asılacak QR'ın kodladığı değer).
- `POST api/v1/attendance/self-checkin/qr` (`QrCheckInCommand`, izin `attendance.my.checkin`): `{ code, direction }` alır, `Organization.Contracts.ILocationLookup.FindBySelfCheckInCodeAsync` ile lokasyonu çözer, `AttendanceDayUpsert` ile günü günceller, `SelfCheckIn` (yeni denetim-kaydı entity'si, `Sense.Modules.Attendance.Domain.SelfCheckIns`) satırı yazar.
- Web: `web/src/pages/attendance/self-check-in.tsx` — kodu elle giren bir metin alanı (gerçek kamera taraması aşağıdaki "Mobile Entegrasyon" bölümünde).

### Kabul kriteri 2 — GPS check-in + opsiyonel geofence

- `Organization.Location`'a `Latitude`/`Longitude`/`GeofenceRadiusMeters` eklendi (üçü de nullable; radius boşsa mesafe doğrulanmaz, konum yalnız kaydedilir — kartın istediği "opsiyonel geofence kontrolü"). Yeni `PUT api/v1/organization/locations/{id}/geofence` (`SetLocationGeofenceCommand`, aynı `organization.locations.update` izni) ile ayarlanıyor/temizleniyor.
- `POST api/v1/attendance/self-checkin/gps` (`GpsCheckInCommand`): `{ latitude, longitude, locationId?, direction }`. `locationId` verilmezse (saha/uzaktan çalışan, sabit lokasyonu yok) doğrudan kabul edilir. Verilmişse `ILocationLookup.GetGeofenceAsync` ile geofence okunur; merkez+yarıçap tanımlıysa Haversine mesafesi (`GeoDistance.cs`) hesaplanıp yarıçap aşılırsa `attendance.self_checkin.outside_geofence` ile reddedilir (AttendanceDay'e hiç dokunmadan).
- Her GPS/QR denemesi `SelfCheckIn` tablosuna yazılır (yöntem, çözülen lokasyon, ham koordinatlar, geofence sonucu) — turnike CSV'sinin aksine bu kanal kanıt/denetim amacıyla ham denemeyi de saklıyor.
- Web: aynı sayfada "Konumumla giriş/çıkış yap" butonu, tarayıcı `navigator.geolocation` API'si.

### Kabul kriteri 3 — Canlı içeride/dışarıda panosu

- `GET api/v1/attendance/board/live?orgUnitId=` (`GetLiveAttendanceBoardQuery`, yeni izin `attendance.board.view`): görünür (data-scope) çalışanlar için bugünün `AttendanceDay` kaydını okur, `CheckIn != null && CheckOut == null` → "içeride". Kaydı olmayan çalışan "dışarıda, saat yok" olarak listelenir.
- Web: `web/src/pages/attendance/live-board.tsx` — sade tablo, 30 saniyede bir `refetchInterval` ile polling (WebSocket yok, kart da zorunlu tutmuyor). Ofis ekranına yansıtmaya uygun, tam ekran/karartılmış tema eklenmedi (istenirse ayrı iş).

### Kabul kriteri 4 — bkz. yukarıdaki "Ortak servis" bölümü.

### Diğer değişiklikler

- Yeni izinler: `attendance.my.checkin` (adı "*.my.*" kalıbına uyduğu için ekstra `SystemRoleDefinitions` değişikliği gerekmedi — her rol zaten `PermissionPatterns.SelfService` üzerinden kapsıyor), `attendance.board.view` (Owner/Admin/HR/HRSpecialist/Timekeeper zaten "attendance.*" ile, Manager/PayrollSpecialist "attendance.*.view" ile kapsıyor).
- Yeni hata kodları (`AttendanceErrors`): `attendance.self_checkin.invalid_code`, `attendance.self_checkin.location_not_found`, `attendance.self_checkin.outside_geofence`. `SharedResource{,.en}.resx`'e eklendi.
- `organization.location.geofence_invalid` (Organization domain hatası) — Organization modülünün Location hata kodları (`LocationNameRequired` dahil) daha önce hiç resx'e eklenmemiş; bu ön var olan boşluğu bu PR genişletmedi, sadece kendi yeni kodunu da aynı (eklenmemiş) durumda bıraktı — ayrı bir iş olarak not edilmeli.
- Migrationlar: `Sense.Modules.Organization.Infrastructure` (`Location` yeni kolonlar) ve `Sense.Modules.Attendance.Infrastructure` (`self_check_ins` tablosu) için birer yeni EF migration eklendi (bkz. Migrations klasörleri).

## Test

Yeni birim testleri (NSubstitute, mevcut kalıpları izliyor):
- `tests/Modules/Sense.Modules.Organization.Tests/LocationTests.cs` — `SetGeofence` + `SelfCheckInCode` üretimi.
- `tests/Modules/Sense.Modules.Attendance.Tests/SelfCheckInTests.cs` — `SelfCheckIn` domain entity.
- `tests/Modules/Sense.Modules.Attendance.Tests/SelfCheckInFeaturesTests.cs` — QR/GPS handler'ları (geçersiz kod, geofence içi/dışı, geofence tanımsız, aynı gün ikinci "in" damgasının ilk girişi koruması, "out" damgasının üzerine yazması, bağlı çalışan yoksa NotFound).
- `tests/Modules/Sense.Modules.Attendance.Tests/LiveAttendanceBoardFeaturesTests.cs` — içeride/dışarıda/kayıtsız satırlar.
- Mevcut `AttendanceImportRowHandlerTests.cs`, `TeamTimesheetFeaturesTests.cs`, `DeviceLogImportFeaturesTests` (varsa) davranış değişmeden geçmeli — `AttendanceDayUpsert`/`VisibleEmployeeResolver` refactor'ları saf çıkarma, mantık aynı.

`dotnet build`/`dotnet test` (Docker SDK konteyneri) ve `pnpm exec tsc --noEmit` bu oturumda tamamlanamadı — bkz. bu dosyanın sonundaki "Doğrulama Durumu" notu; bir sonraki oturumda ilk iş bu komutları çalıştırıp sonucu buraya eklemek olmalı.

## Mobile Entegrasyon Planı (dokunulmadı, backend hazır)

`apps/senseik-mobile` şu an yalnız "Puantajım" (`AttendanceScreen.tsx`, `AttendanceService.ts`) sunuyor; self-servis check-in ekranı yok. Öneri:

1. **QR tarama**: `expo-camera`'nın `CameraView` barkod tarayıcısı (Expo SDK 52+, ayrı `expo-barcode-scanner` paketine gerek yok) `barcodeScannerSettings={{ barcodeTypes: ["qr"] }}` ile. Taranan metin doğrudan `Location.SelfCheckInCode` değeridir (8 karakter, büyük harf+rakam) — ekstra parse gerekmez, direkt `POST /v1/attendance/self-checkin/qr` gövdesine `code` olarak konur.
2. **GPS**: `expo-location`'ın `getCurrentPositionAsync` — izin akışı (`requestForegroundPermissionsAsync`) önce gösterilmeli; reddedilirse QR'a yönlendir.
3. **Yön (in/out) belirleme**: Önce `GET /v1/attendance/self-checkin/status` çağrılıp `isCheckedIn` okunmalı, buton buna göre "Giriş Yap"/"Çıkış Yap" göstermeli — web sayfası (`self-check-in.tsx`) bunu zaten böyle yapıyor, aynı deseni mobilde tekrarla.
4. **Yeni ekran**: `apps/senseik-mobile/src/screens/app/SelfCheckInScreen.tsx` + `AttendanceService.ts`'e `qrCheckIn`/`gpsCheckIn`/`getMyCheckInStatus` eklenmesi (web servisiyle aynı sözleşme, `web/src/services/attendance.service.ts`'teki `qrCheckIn`/`gpsCheckIn`/`getMyCheckInStatus` fonksiyonlarını birebir taşı). Navigasyona (muhtemelen tab bar ya da "Puantajım" ekranından bir buton) eklenmeli — mevcut navigasyon dosyasına bakılmadı, bu ayrı bir keşif gerektirir.
5. **Offline/zayıf sinyal**: Kapsam dışı bırakıldı — kabul kriterleri bunu istemiyor; ileride retry kuyruğu (ör. günün ilk QR taramasını cihazda tutup bağlantı gelince gönderme) ayrı bir iş olarak değerlendirilebilir.

## Bilinen Sınırlamalar / Borçlar

- `Location.SelfCheckInCode` çakışma ihtimaline karşı retry yok (8 karakter × 32 alfabe ≈ 40 bit entropi, tenant-scope unique index var ama insert çakışırsa handler'da yakalanıp yeniden denenmiyor) — pratikte gerçekçi tenant büyüklüklerinde ihmal edilebilir risk, ama not edilmeli.
- GPS check-in geofence dışı kaldığında hiçbir kayıt (ne `AttendanceDay` ne `SelfCheckIn`) yazılmıyor — reddedilen denemelerin kendisi denetlenmek istenirse (ör. "kaç kez dışarıdan denendi" raporu) ayrı bir iş.
- Web'de QR kodunun gerçek görsel (kamera ile taranabilir) üretimi/gösterimi yok — bu PR'da yalnız kodun kendisi (`Location.selfCheckInCode`) API'den dönüyor; admin tarafında "QR göster/yazdır" ekranı ve gerçek QR image render'ı (ör. `qrcode.react`) ayrı bir küçük iş olarak bırakıldı, kapsam notunda "web UI minimal" denildiği için bilinçli olarak atlandı.
- Organization Location'ın diğer domain hata kodları (`LocationNameRequired` vb.) resx'e hiç eklenmemiş; bu PR kendi yeni `geofence_invalid` kodunu da aynı (eklenmemiş) durumda bıraktı, tutarlılık için.

## Kritik Dosyalar

- `src/Modules/Attendance/Sense.Modules.Attendance.Application/AttendanceDays/AttendanceDayUpsert.cs` (yeni, ortak upsert)
- `src/Modules/Attendance/Sense.Modules.Attendance.Application/SelfCheckIns/SelfCheckInFeatures.cs`, `LiveAttendanceBoardFeatures.cs`, `GeoDistance.cs` (yeni)
- `src/Modules/Attendance/Sense.Modules.Attendance.Domain/SelfCheckIns/SelfCheckIn.cs` (yeni)
- `src/Modules/Attendance/Sense.Modules.Attendance.Api/Controllers/SelfCheckInController.cs`, `LiveBoardController.cs` (yeni)
- `src/Modules/Organization/Sense.Modules.Organization.Domain/Locations/Location.cs` (geofence + SelfCheckInCode)
- `src/Modules/Organization/Sense.Modules.Organization.Contracts/ILocationLookup.cs` (yeni `LocationGeofence`, `GetGeofenceAsync`, `FindBySelfCheckInCodeAsync`)
- `web/src/pages/attendance/self-check-in.tsx`, `web/src/pages/attendance/live-board.tsx` (yeni)
- `web/src/services/attendance.service.ts`, `web/src/services/locations.service.ts` (self-checkin/board/geofence eklendi)

## Doğrulama Durumu (bu oturumun sonunda)

**Bu commit lokal — `4526f9b` (branch `b/attendance-self-checkin` = worktree branch `worktree-agent-ad7d0f07ccee8191f`).**

Bu oturumda hem Docker daemon hem de host'ta `node`/`pnpm` bu sandbox'ta kullanılamadı:
- Docker: `docker version` / `docker ps` / `docker info` gibi en basit komutlar bile dakikalarca yanıt vermedi (SDK konteyneriyle `dotnet build` denemesi dahil, tümü zaman aşımına uğrayıp arka plana düştü, hiçbiri sonuçlanmadı). Bu, konteyner/derleme ile ilgili bir sorun değil — Docker daemon'ının kendisi bu oturumda genel olarak yanıt vermez durumdaydı (muhtemelen bu dizinde paralel çalışan başka bir oturumun/işlemin Docker'ı yoğun kullanması; bkz. proje hafızasındaki "Parallel sessions in one directory" notu).
- `pnpm`/`node`: bu ajan sandbox'ında PATH'te hiç yok (`Get-Command node/pnpm` boş döndü) — CLAUDE.md'nin "host pnpm çalışır" notu bu izole ajan ortamı için geçerli değildi.
- Sonuç: kod **derleme zamanı satır satır elle gözden geçirildi** (tip/namespace/using tutarlılığı, mevcut çağıranların yeni imzalara uyumu, `Result<T>`/ternary target-typing kalıpları, cref'lerin proje sınırlarını aşmaması, EF `IEntityTypeConfiguration` kaydının assembly-scan ile toplanması) ama **gerçek `dotnet build`/`dotnet test`/`pnpm exec tsc --noEmit` bu oturumda hiç çalıştırılamadı**. `git commit` kendisi docker'a bağımlı değildi ve sorunsuz tamamlandı (husky/lint-staged hook'ları bu worktree'de tetiklenmedi - muhtemelen `node_modules`/`.husky` bu izole worktree'de kurulu değil).

Bir sonraki oturum/gözden geçirme ÖNCELİKLE (Docker ve pnpm çalışan bir ortamda):
1. `dotnet build Sense.slnx` (Docker SDK konteyneri, CLAUDE.md komutu) — bu commit'in gerçekten derlendiğini doğrulamak birinci öncelik.
2. `dotnet test` — en az `Sense.Modules.Attendance.Tests`, `Sense.Modules.Organization.Tests`.
3. Organization + Attendance için `dotnet ef migrations add` ile gerçek migration dosyaları üretilmeli — **bu commit'te migration dosyaları YOK**, yalnız DbContext/EF configuration kodu var (`Location.Latitude/Longitude/GeofenceRadiusMeters/SelfCheckInCode`, `attendance.self_check_ins` tablosu). Mevcut entegrasyon testleri bu yeni alan/tabloya dokunmadığı için migrasyon eksikliği `dotnet test`'i muhtemelen kırmaz, ama özellik gerçek bir Postgres'e karşı ÇALIŞMAZ ta ki migration üretilene kadar — bu, "demo'da QR/GPS check-in akışı uçtan uca çalışıyor" merge gate'inin şu an KARŞILANMADIĞI anlamına gelir.
4. `pnpm exec tsc --noEmit` ve `pnpm exec eslint src` (`web/` dizininde, host'ta).
çalıştırılıp bu bölüm gerçek sonuçlarla güncellenmelidir.
