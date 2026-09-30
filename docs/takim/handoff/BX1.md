# BX1 - Backend takip duzeltmeleri (handoff)

Dal: takim/BX1 (taban a/zaman-devam). Sema/migration degisikligi YOK.

## Uygulananlar
1. B14 - `CompanyEvent.EventDate` zamanli etkinlikte kiraci yerel gununden turetilir. Domain saat dilimi bilmez:
   `CompanyEventPlan.LocalStartDate` parametresi; create/update handler'lari `ITenantTimeZoneLookup` ile hesaplar
   (`CompanyEventLocalDay`). Takvim: repository +-1 gun pay ile getirir, `GetCompanyCalendarHandler` yerel gune gore keser.
   Tum gun etkinlikler ve mevcut satirlar degismez. Handler kurucularina `ITenantTimeZoneLookup` eklendi.
2. B16 - `HrMessageDto` + `SenderName`, `RecipientPreview` (ilk 3 alici "Ad S."); `HrMessageDtoBuilder` (toplu:
   kisi profili, alici onizleme, iletisim; N+1 yok). Onem seviyelerine dokunulmadi. `recipientCount` zaten vardi.
3. B13 - ATLANDI: T2 (`IEmployeeLookup` sicil + her durumda eslesme/isten ayrilma) a/zaman-devam'da henuz yok.
   **T2 birlesince B13 bunu kullanacak:** PayslipBulkImport'ta `terminated_employee` yolu yeni port metoduyla baglanacak.
4. B3/I0 - `RecalculationExecutor`: parca basina `MaxChunkRetries = 2` yeniden deneme (`IsConcurrencyConflict`,
   `ClearTracked()` ile taze yukleme, `RetryDelay` 250 ms); tukenirse hata yukari cikar (job Failed + log, eski davranis).
   `IAttendanceDayRepository.ClearTracked()` eklendi.
5. N1 - `PayslipAcknowledgementReminderNotificationHandler`: SourceEventId = SHA256(olay, donem)[..16]; ayni olayin
   yeniden teslimi ayni anahtari uretir (idempotans korunur), iki donem iki bildirim.
6. `DailyAttendanceRowDto.IncalculableReason` (string?, camelCase, sona eklendi). Sayfa okutmalari zaten tek sorguda;
   plan yalniz Incalculable satirlar icin cozulur (`IncalculableExplainer` ListAttendanceDays ile ortak).
   Not: plan cozumu hala satir basina (yalniz hesaplanamayan gunler, sayfa <= 200); toplu plan cozucu ayri is.
7. B7 - vekalet "bugun" = kiraci yerel gunu (`DelegationResolver.TodayAsync`); ListInbox + DecideApprovalTask.
8. R1 - gunluk durum raporlarinda aralik 31 gune kirpilirsa PDF donem satirina ve Excel son satirina
   "Ilk 31 gun gosterildi" notu (`report.note.daily_range_truncated`, tr/en).

## Notlar
- Testcontainers bazi kosularda Docker soketine baglanamadi (Announcements migration entegrasyon testi, tekrarda gecti/kosmadi
  duruma gore); B16 PostgreSQL testleri (HrMessageTests) yesil kostu.
