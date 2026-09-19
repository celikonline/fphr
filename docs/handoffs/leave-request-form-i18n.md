# senseik-039 — İzin Talebi Formunda 12 İzin Türü İngilizce Görünüyor — UYGULAMA NOTU

## Durum

GATE 1 ve GATE 2 önceden onaylandı, uygulandı. Branch: `b/leave-type-edit-modal-fix` (senseik-038 ile birlikte,
aynı Leave UI alanına dokunduğu için tek branch/agent olarak yürütüldü, ayrı commit).

## Kök Neden — senseik-008 ile AYNI DEĞİL, farklı bir veri yolu

Sistemde izin türü adları için **iki bağımsız, birbirinden habersiz** çözümleme mekanizması var:

1. **Frontend (i18n, `useLeaveName()` — `leave-shared.tsx`)**: Seed veride ham kaynak anahtarı olarak saklanan
   adları (`"leave.type.annual"`) react-i18next'in `leave:names.*` haritasından çözer. Kullanılan dil,
   `i18n.language` — kullanıcının **uygulama içi** dil tercihi (react-i18next `LanguageDetector`,
   `detection.order: ["localStorage", "navigator"]`, `web/src/i18n.ts`). Ayarlar → İzin kuralları, izin defteri,
   bakiye özeti ve "İzin Ekle" (HR'ın çalışan adına oluşturduğu form, `leave-section.tsx`'teki `addLeaveTypes`)
   HEP bu yolu kullanıyor — hepsi doğru şekilde Türkçe gösteriyordu.
2. **Backend (`LeaveTextLocalizer.Localize()` — `src/Modules/Leave/.../MyLeaveController.cs`)**: Aynı ham
   anahtarları `IErrorLocalizer.MessageOrText()` ile `SharedResource.resx`'ten (C# resx dosyaları) çözüyordu.
   Kullanılan kültür, ASP.NET Core `RequestLocalizationMiddleware`'in **HTTP `Accept-Language` başlığından**
   belirlediği `CurrentUICulture` — yani tarayıcının/işletim sisteminin dil ayarı, kullanıcının uygulama
   içindeki dil seçiminden **tamamen bağımsız**.

"+ Talep" formunun tür açılır listesi (`web/src/pages/leave/my-leave.tsx` → `buildRequestableTypes`), diğer
tüm ekranlardan farklı olarak, `GET /leave/me/leave-types` (`RequestableLeaveTypeDto`) çağrısının döndürdüğü
`name` alanını **doğrudan** kullanıyordu — `leaveName()`'den hiç geçirmiyordu; çünkü bu alanın API tarafından
"zaten çevrilmiş" geldiği varsayılıyordu (bkz. eski XML doc: "the API localizes resource keys"). Bu varsayım
doğruydu ama **yanlış kültür kaynağına** dayanıyordu: tarayıcının Accept-Language'ı Türkçe uygulama dili
tercihiyle uyuşmadığında (yaygın durum — OS/tarayıcı dili İngilizce, uygulama içi seçim Türkçe), bu tek uç nokta
İngilizce metin döndürüyordu, oysa aynı ham anahtarlar her yerde frontend'in kendi `i18n.language`'ına göre
doğru şekilde Türkçeye çözülüyordu. **Kanıt**: `leave-section.tsx`'teki bire bir eşdeğer akış olan "İzin Ekle"
(`addLeaveTypes`) baştan beri `leaveName(type.settings.name)` çağırıyordu — sadece "+ Talep" (`my-leave.tsx`)
bu adımı atlıyordu.

Bu, senseik-008'in düzelttiği "ham anahtar sızıntısı" (o kart: `leave-type-dialog.tsx`'in `useLeaveName()`
çağırmayı unutması — bir state initialization hatası) ile **aynı kök neden değil**. Buradaki hata, backend'in
kullanıcı diliyle ilgisiz bir kültür kaynağına (Accept-Language) göre önceden çevirdiği bir metni frontend'in
sorgusuzca güvenmesi — iki farklı, çakışan lokalizasyon otoritesi arasındaki bir tutarsızlık.

## Düzeltme

Tek doğru otorite olarak **frontend i18n**'i seçtik (mevcut, çalışan `leave-section.tsx` deseniyle tutarlı):

1. **Backend** — `MyLeaveController.LeaveTypes` artık `.Localize(localizer)` ÇAĞIRMIYOR;
   `RequestableLeaveTypeDto.Name` ham anahtar olarak dönüyor (`LeaveRuleSet.types[].name` ve
   `EmployeeLeaveBalances.balances[].leaveTypeName` ile aynı sözleşme). Artık kullanılmayan
   `RequestableLeaveTypeDto` `Localize()` overload'u `LeaveTextLocalizer.cs`'ten kaldırıldı.
2. **Frontend** — `web/src/pages/leave/my-leave.tsx`:
   - `buildRequestableTypes` artık üçüncü parametre olarak `leaveName` alıyor ve hem birincil yolda
     (`/leave-types` başarılı) hem bakiye-only fallback yolunda (`/leave-types` başarısız) adları
     `leaveName(...)`'den geçiriyor — `leave-section.tsx`'teki `addLeaveTypes` ile birebir aynı desen.
   - Aynı komponentteki `ledgerTypes` (İzin defteri filtre dropdown'ı) da `typesQuery.data` üzerinden aynı ham
     alanı kullanıyordu (`type.name`, `leaveName()` olmadan) — API artık ham anahtar döndürdüğü için bu da
     düzeltildi, aksi halde ledger filtresi "leave.type.annual" gibi ham anahtarlar göstermeye başlardı.
3. Yanıltıcı yorumlar güncellendi: `RequestableLeaveType` (TS arayüzü) ve `RequestableLeaveTypeDto`/
   `ListMyRequestableLeaveTypesQuery` (C#) artık "API çevirir" değil "ham gelir, frontend çözer" diyor.

**Kapsam dışı bırakılanlar (bilinçli, riski azaltmak için)**: `LeaveRequestDto.LeaveTypeName` ve
`LeaveRequestDetailDto.LeaveTypeName` hâlâ aynı `.Localize()`/Accept-Language mekanizmasını kullanıyor (izin
talep listesi/detayı, takvim). Teorik olarak aynı kültür-uyuşmazlığı riski onlarda da var, ama kart bunları
İngilizce olarak raporlamadı (muhtemelen test ortamının Accept-Language'ı o an tesadüfen tutarlıydı ya da farklı
bir önbellekleme/zamanlama etkisi var) ve bu dosyalar başka kartların (takvim/talep listesi ile ilgili
senseik-024/026/030 vb.) aktif çalışma alanına giriyor olabilir. **Öneri**: ayrı bir takip kartı — aynı
Accept-Language-vs-uygulama-dili tutarsızlığının `LeaveRequestDto`/`LeaveRequestDetailDto`/
`LeaveCalendarEntryDto` için de sistematik olarak denetlenip (muhtemelen aynı çözüm: backend localize'ı kaldır,
frontend `leaveName()`'e devret) düzeltilmesi.

## Test

- **Otomatik (yeni)**: `web/src/pages/leave/my-leave.build-requestable-types.test.ts` — `buildRequestableTypes`
  artık export edilen saf bir fonksiyon; üç senaryo: (1) `/leave-types` başarılı + ham anahtar → `leaveName()`
  çağrılıyor, (2) serbest metin (anahtar değil) değişmeden geçiyor, (3) bakiye-only fallback yolunda da
  `leaveName()` çağrılıyor. Gerçek i18n kurulumuna ihtiyaç yok — basit bir stub `leaveName` ile davranış
  doğrulanıyor (fonksiyonun kendisi zaten hangi map'i kullandığını umursamıyor, sadece çağırdığını kanıtlıyoruz).
- **Manuel** (asıl doğrulama — gerçek i18n + gerçek API culture davranışı jsdom'da taklit edilemez): İzinler →
  "+ Talep" açılır listesinde 12 türün tamamının Türkçe göründüğü doğrulanmalı; ayrıca İzin defteri filtre
  dropdown'ı da (aynı kod yolu) kontrol edilmeli. Tarayıcının/OS'un dili İngilizce olsa DAHİ (uygulama içi dil
  Türkçe kaldığı sürece) liste Türkçe kalmalı — bu, düzeltmenin asıl kanıtı (backend Accept-Language'a artık
  bağımlı değil).
- `pnpm exec tsc --noEmit`: temiz (2 ilgisiz pre-existing hata, senseik-038 notundaki ile aynı — bu kartın
  dokunmadığı `protected-route.test.tsx`/`home.test.tsx`).
- `pnpm exec eslint src`: 0 hata (1 ilgisiz pre-existing uyarı, `employee-list.tsx`).
- `pnpm exec vitest run src/components/leave src/pages/leave`: 21/21 yeşil.
- Backend: `ListMyRequestableLeaveTypesHandler`'ı doğrudan çağıran mevcut test
  (`tests/Modules/Sense.Modules.Leave.Tests/LeaveRequestTests.cs:292`) controller katmanını (dolayısıyla
  `.Localize()`'ı) atlıyor, etkilenmedi.

## Kritik Dosyalar

- `web/src/pages/leave/my-leave.tsx` (asıl düzeltme: `buildRequestableTypes`, `ledgerTypes`)
- `web/src/pages/leave/my-leave.build-requestable-types.test.ts` (yeni regresyon testi)
- `web/src/services/leave.service.ts` (`RequestableLeaveType` doc güncellemesi)
- `src/Modules/Leave/Sense.Modules.Leave.Api/Controllers/MyLeaveController.cs` (`.Localize()` kaldırıldı)
- `src/Modules/Leave/Sense.Modules.Leave.Api/Controllers/LeaveTextLocalizer.cs` (ölü overload kaldırıldı)
- `src/Modules/Leave/Sense.Modules.Leave.Application/Requests/RequestableLeaveTypes.cs` (doc güncellemesi)
- Referans (bu kartta değiştirilmedi): `web/src/components/leave/leave-section.tsx` (`addLeaveTypes` — doğru
  çalışan kardeş desen)

---
*Kaynak: 1. Tur Test Bulgu Dokümanı § B-03. Agent board: `docs/agent-board/cards/senseik-039-leave-request-form-english-types.json`.*
