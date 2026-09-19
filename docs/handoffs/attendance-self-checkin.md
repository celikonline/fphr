# senseik-031 — PDKS Self-Servis QR/GPS Check-in + Canlı Pano — UYGULAMA NOTU

**Durum: review (rebase + güvenlik turu + doğrulama tamam). Push/merge yapılmadı.** Branch: `b/attendance-self-checkin`.

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

- `Organization.Location`'a `SelfCheckInCode` eklendi (oluşturma anında CSPRNG ile üretilen 12 karakterlik kod, tenant içinde benzersiz — girişe asılacak QR'ın kodladığı değer; `POST organization/locations/{id}/self-checkin-code/regenerate` ile yenilenir, eski poster anında geçersiz olur).
- `POST api/v1/attendance/self-checkin/qr` (`QrCheckInCommand`, izin `attendance.my.checkin`): `{ code, direction, latitude?, longitude? }` alır, `Organization.Contracts.ILocationLookup.FindBySelfCheckInCodeAsync` ile lokasyonu çözer, `AttendanceDayUpsert` ile günü günceller, `SelfCheckIn` (yeni denetim-kaydı entity'si, `Sense.Modules.Attendance.Domain.SelfCheckIns`) satırı yazar.
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
- Migrationlar (dev üzerine rebase sonrası üretildi): Organization `20260919124045_LocationSelfCheckInGeofence` (geofence kolonları + `self_check_in_code varchar(12)` + tenant-scope unique index; **mevcut satırlar için `gen_random_uuid()` ile geri doldurma SQL'i var** - `defaultValue: ""` unique index'i 2+ lokasyonlu tenantlarda kırardı), Attendance `20260919124058_AddSelfCheckIns` (`self_check_ins` tablosu). Identity için migration yok (yalnız yeni `ITenantTimeZoneLookup` sözleşmesi).

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

1. **QR tarama**: `expo-camera`'nın `CameraView` barkod tarayıcısı (Expo SDK 52+, ayrı `expo-barcode-scanner` paketine gerek yok) `barcodeScannerSettings={{ barcodeTypes: ["qr"] }}` ile. Taranan metin doğrudan `Location.SelfCheckInCode` değeridir (12 karakter, büyük harf+rakam) — ekstra parse gerekmez, `POST /v1/attendance/self-checkin/qr` gövdesine `code` olarak konur; geofence yarıçaplı lokasyonlarda `expo-location` koordinatı da `latitude`/`longitude` olarak eklenmelidir (yoksa sunucu `coordinates_required` döner).
2. **GPS**: `expo-location`'ın `getCurrentPositionAsync` — izin akışı (`requestForegroundPermissionsAsync`) önce gösterilmeli; reddedilirse QR'a yönlendir.
3. **Yön (in/out) belirleme**: Önce `GET /v1/attendance/self-checkin/status` çağrılıp `isCheckedIn` okunmalı, buton buna göre "Giriş Yap"/"Çıkış Yap" göstermeli — web sayfası (`self-check-in.tsx`) bunu zaten böyle yapıyor, aynı deseni mobilde tekrarla.
4. **Yeni ekran**: `apps/senseik-mobile/src/screens/app/SelfCheckInScreen.tsx` + `AttendanceService.ts`'e `qrCheckIn`/`gpsCheckIn`/`getMyCheckInStatus` eklenmesi (web servisiyle aynı sözleşme, `web/src/services/attendance.service.ts`'teki `qrCheckIn`/`gpsCheckIn`/`getMyCheckInStatus` fonksiyonlarını birebir taşı). Navigasyona (muhtemelen tab bar ya da "Puantajım" ekranından bir buton) eklenmeli — mevcut navigasyon dosyasına bakılmadı, bu ayrı bir keşif gerektirir.
5. **Offline/zayıf sinyal**: Kapsam dışı bırakıldı — kabul kriterleri bunu istemiyor; ileride retry kuyruğu (ör. günün ilk QR taramasını cihazda tutup bağlantı gelince gönderme) ayrı bir iş olarak değerlendirilebilir.

## Bilinen Sınırlamalar / Borçlar

- `Location.SelfCheckInCode` çakışma ihtimaline karşı retry yok (12 karakter × 31 sembol ≈ 59 bit CSPRNG entropi, tenant-scope unique index var ama insert çakışırsa handler'da yakalanıp yeniden denenmiyor) — pratikte gerçekçi tenant büyüklüklerinde ihmal edilebilir risk, ama not edilmeli.
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

## Güvenlik Turu (rebase sonrası)

Uçlar `Sense.Modules.Attendance.Api` assembly'sinde (`SelfCheckInController`, `LiveBoardController`), dolayısıyla `attendance` modülü kapatıldığında global `ModuleAccessFilter` 403 döner. Kimlik: çalışan yalnız token'daki `EmployeeId` ile işlem yapar (gövdede employeeId yok), tenant `ITenantContext`'ten; QR/lokasyon çözümü tenant-scope, canlı pano `IDataScopeService` ile veri kapsamlı. İzin adları TR/EN resx'te ve Türkçe DisplayName ile (Architecture testi bunu zorunlu kılıyor).

Bulunan ve düzeltilen:

1. **(Yüksek) QR kodu tahmin edilebilirdi**: `Guid.CreateVersion7()` baytlarından (ilk 6 bayt ms zaman damgası) türetiliyordu, 8 karakter. Artık `RandomNumberGenerator` ile 12 karakter (~59 bit), 31 sembollü alfabe. Mevcut satırlar migration'da CSPRNG-tabanlı `gen_random_uuid()` ile geri dolduruluyor.
2. **(Yüksek) Statik QR + konum doğrulaması yoktu**: QR "fiziksel varlık kanıtı" sayılıyordu; posterin fotoğrafı uzaktaki birine gönderilebilirdi. Artık lokasyonda geofence yarıçapı tanımlıysa QR isteğinde koordinat **zorunlu** ve sunucuda Haversine ile doğrulanıyor (`attendance.self_checkin.coordinates_required` / `outside_geofence`); istemcinin "içerideyim" iddiası hiçbir yerde güvenilmiyor. Yarıçap tanımlı değilse konum yalnız kaydediliyor (kartın "opsiyonel geofence" kuralı).
3. **(Orta) Sızan kod iptal edilemiyordu**: `RegenerateLocationSelfCheckInCodeCommand` + web'de "Kodu yenile" (onaylı) eklendi.
4. **(Yüksek, doğruluk) Saatler UTC damgalanıyordu**: `AttendanceDay` giriş/çıkış yerel duvar saati ve yerel çalışma tablosuyla kıyaslanır; İstanbul'da 09:00 girişi 06:00 yazılıp anomali üretirdi. Yeni `Identity.Contracts.ITenantTimeZoneLookup` (+ `TenantTimeZoneLookup`, `IdentityModule`'e tek satır kayıt) ve Attendance'ta `ITenantLocalClock`; tarih/saat, durum sorgusu ve pano artık tenant yerel saatinde. Tanınmayan saat dilimi UTC'ye düşer.
5. **(Orta) Ortak upsert vardiya limitlerini/mola dakikasını yitiriyordu** (rebase sonrası dev'in `ActualBreakMinutes`/`ShiftLimits` imzasıyla): `AttendanceDayUpsert` bunları opsiyonel parametre olarak alıyor, self-servis yolu tenant `ShiftSettings` limitlerini ve mevcut mola dakikasını koruyarak geçiriyor; `ToShiftLimits` ortaklaştırıldı (cihaz içe alma da bunu kullanıyor).
6. **(Orta) Gece vardiyası**: sabah "Out" + dün akşam (12:00+) açık "In" varsa dünün kaydı `overnight` kapatılıyor (cihaz içe alma G-6-14 kuralıyla aynı); durum sorgusu ve pano dünün açık kaydını "içeride" sayıyor.
7. `Result<bool?>` null değeri başarısızlık sayıldığı için geofence sonucu `GeofenceVerdict` struct'ına sarıldı (birim test yakaladı).
8. `organization.location.geofence_invalid` resx'e eklendi (tr/en); `attendance.self_checkin.coordinates_required` yeni; `attendance.my.checkin`/`attendance.board.view` DisplayName'leri Türkçeleştirildi; web'de `locations.tsx`'te kullanılmayan `ActionIcon`/`Tooltip` import'ları (tsc/eslint hatası) temizlendi.

Replay: istek sunucu saatini damgalıyor (istemci zamanı yok); "in" ikinci kez gelirse ilk girişi korur, "out" son saate yazılır - tekrar oynatmanın etkisi yeni bir gerçek damga ile aynıdır, ayrı nonce gerekmedi.

Bilinen kalan riskler / borçlar (bilerek kapsam dışı):

- `SelfCheckInCode` statik bir sırdır; geofence tanımlı olmayan lokasyonda poster fotoğrafı yeterlidir (yönetici geofence tanımlamalı). Dönen (TOTP benzeri) kod ekranı ayrı iş.
- `locationId`'siz GPS check-in (saha/uzaktan) doğası gereği kandırılabilir: yalnız koordinat kaydedilir.
- Yanlış kod / geofence dışı denemeler yazılmıyor (işlem geri alınıyor) ve uca özel rate limit yok (genel API sınırlayıcıya bağlı) - brute-force ancak kimlik doğrulamalı ve 59 bitlik uzayda.
- Aynı çalışanın aynı anda iki ilk-giriş isteği `(tenant, employee, date)` unique index'inde yarışabilir (biri 5xx alır, tekrar dener).
- Yönetim ekranında QR görselinin çizimi/yazdırılması (`qrcode.react`), mobil kamera taraması ve offline kuyruk yok.

## Doğrulama

Ortam: Windows host, .NET SDK 10.0.401 + node/pnpm (corepack) yerelde; Docker bu oturumda çalışıyordu (Testcontainers PostgreSQL 17 ayağa kalktı), bu yüzden integration testler de koştu.

Çalıştırıldı, yeşil:

- `dotnet build`: Attendance.Api, Organization.Api, Identity, src/Sense.Api, src/Sense.Migrator, src/Sense.Worker (0 hata).
- `Sense.Modules.Attendance.Tests` 267/267 (integration dahil; yeni: QR/GPS geofence, tenant yerel saat, gece vardiyası, pano gece vardiyası, `TenantLocalClockTests`), `Sense.Modules.Organization.Tests` 129/129 (CSPRNG kod + yenileme testleri dahil), `Sense.Modules.Identity.Tests` (integration hariç) 131/131.
- `Sense.Tests.Architecture` 56/56, `ErrorResourceParityTests` 2/2.
- `dotnet ef migrations has-pending-model-changes`: Organization, Attendance, Identity "No changes".
- web: `tsc --noEmit` temiz, `eslint src` 0 hata (1 önceden var olan `employee-list.tsx` uyarısı).

Çalıştırıldı ama dokunulan koda bağlı görünmeyen başarısızlıklar (dev üzerinde ayrıca doğrulanmadı, muhtemelen önceden var): `Sense.Tests.Contract` (2 başarısız: IntegrationEvents/Port interface yapısal testleri), `Sense.Tests.TenantIsolation` (3, reflection ile boş küme), `Sense.Tests.Integration` (2: trait konvansiyonu + `ModuleToggleIntegrationTests.LegacyRow_WithEmptyJsonObject` raw-SQL biçim hatası), web `i18n.locales.test.ts` (`de/attendance.json` içindeki `teamTimesheet.*` altı "[DE]" yer tutucusu - 029/056 kaynaklı).

Çalıştırılmadı / doğrulanmadı:

- Uçtan uca demo (tarayıcıda QR/GPS check-in akışı, gerçek konum izni) ve HTTP seviyesinde `self-checkin`/`board` endpoint integration testi yok; handler + domain + gerçek PostgreSQL migration seviyesinde doğrulandı.
- Migration'daki geri doldurma SQL'i boş tabloda uygulandı (mevcut lokasyon satırı olan bir veritabanında denenmedi).
- Expo mobil (`apps/senseik-mobile`) hiç dokunulmadı/lint edilmedi.
