# senseik-060 — "Aylık Puantaj" web ekranı

**Durum: review.** Branch `b/attendance-monthly-timesheet-web` (dev'in `6e8ca8f` ucundan). Dev'e merge edilmedi, push edilmedi. Backend'e dokunulmadı.

## Ne eklendi

- `web/src/services/attendance.service.ts`: `getMonthlyTimesheet`, `approveMonthlyTimesheet`, `approveAllMonthlyTimesheets` + `MonthlyTimesheetRow` / `MonthlyTimesheetApproval` tipleri (056 DTO'larının birebir yansıması; `approvedAt`/`approvedByUserId` opsiyonel).
- `web/src/pages/attendance/monthly-timesheet.tsx` (+ `monthly-timesheet-helpers.ts`): sayfa. Route `/app/attendance/monthly-timesheet`.
- `web/src/App.tsx` (lazy route), `web/src/config/navigation.ts` (launcher öğesi `monthlyTimesheet`, izin `attendance.timesheet.view`), navigation/attendance locale'leri tr/en/de.
- Modül eşlemesi: `MODULE_ROUTE_PREFIXES` zaten `/app/attendance` önekini `attendance`'a bağlıyor; yeni yol otomatik kapsandı, `modules.test.ts`'e satır eklendi.

## Tasarım kararları

- **API ızgarası günlük değil.** 056'nın `GET monthly` çıktısı çalışan başına ay toplamı (worked/missing/overtime + izin günleri + onay). "Çalışan × gün" görünümü için satır açılınca mevcut `GET timesheet/team/month` (`getTeamMonth`, ekip puantajı sayfasıyla aynı react-query önbelleği) kullanılıyor: gün şeridi + anomali sayaçları (açık/`unresolved` anomalili gün sayısı, ayrı turuncu "İzinli günde çalışma" rozeti). Bu sorgu başarısız olursa toplamlar ve onay butonları çalışmaya devam eder.
- **Yetki:** sayfa `attendance.timesheet.view` ister (NoAccess); `attendance.timesheet.approve` yoksa Onayla/Tümünü Onayla butonları GİZLENİR (pasif değil) ve bilgi metni gösterilir.
- **Tümünü Onayla:** `ConfirmModal` (bekleyen sayı + ay + "izin kontrolü yeniden yapılır" uyarısı); hepsi onaylıysa buton pasif. Sunucu onaylı satırları atladığı için sayı toast'ı sunucunun döndürdüğü sayıdır.
- **Onaylı satır:** "Onaylandı" rozeti (tooltip'te tarih) + "Yeniden onayla" (backend Reapprove).
- **Hatalar:** 403 → `attendance:monthlyTimesheet.forbidden`; diğerleri `getApiErrorMessage` (400 doğrulama `detail`) ile `toast` destructive.
- Kilitli dönem (`isPeriodLocked`) yalnız kilit rozetiyle gösterilir; backend onayı engellemediği için buton da engellenmedi.

## Doğrulama (dürüst durum)

Çalıştırıldı (web/ içinde, Docker yok): `tsc --noEmit` 0 hata; `eslint src` 0 hata (1 önceden var olan `employee-list.tsx` uyarısı); `vitest run` sayfa (10 test) + yardımcılar (11 test) + `route-permissions` + `modules` + `use-nav-visibility` yeşil.
Tam `vitest run`: 1 kırmızı — `src/i18n.locales.test.ts › attendance.json › de carries no untranslated placeholder markers` (de/attendance.json `teamTimesheet.week/month/breakApplied/nightMinutes/dailyLimitExceeded/nightLimitExceeded` yer tutucu). Bu dev'de zaten kırmızı (değişiklikler stash'lenip doğrulandı), bu karttan bağımsız; yeni `monthlyTimesheet` anahtarları temiz.
**Yapılmadı:** tarayıcıda elle/Playwright doğrulama, gerçek API ile uçtan uca deneme.

## Açık / sonraki

- Gün şeridi hücreleri tıklanınca ekip puantajındaki gün çekmecesine (öneri uygulama) bağlanmıyor; şimdilik yalnız tooltip. İhtiyaç olursa `TeamDayDrawer` ortaklaştırılmalı (team-timesheet.tsx 736 satır, önce ayrıştırılmalı).
- Büyük çalışan sayısında sayfalama yok (API de sayfalamıyor).
- de `teamTimesheet.*` yer tutucuları çevrilmeli (PC-A/B fark etmez, ayrı küçük iş).
