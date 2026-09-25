# İnsan onayı bekleyen konular — zaman-devam programı (`a/zaman-devam`)

Bu liste, `.claude/takim/TAKIM.md` "riskli alan" kuralı gereği insan kararı isteyen maddelerdir. Şeritler bağımsız gözden geçiriciden geçti; buradaki maddeler PR birleştirilmeden / migration'lar uygulanmadan önce karara bağlanmalıdır. Çalışma bu maddeleri BEKLEMEDEN sürüyor (yerel entegrasyon dalında birleştirme yapılıyor; migration hiçbir ortama uygulanmadı).

## A. Migration onayları
| # | Modül | Migration | Not |
|---|---|---|---|
| 1 | Attendance (W0-a) | `20260925113345_AddPunchShiftDefinitionRotationTerminal` | 9 yeni tablo + `shift_assignments.day_type/shift_definition_id`; yalnız ekleme. Yeni indeks CONCURRENTLY değil → büyük tabloda yoğun olmayan saatte uygula |
| 2 | Workflow (B7) | `20260925120407_AddApprovalDelegations` | Yeni tablo + `approval_tasks.decided_on_behalf_of_employee_id` |
| 3 | Payroll (B12) | `AddPayslipAcknowledgementAndObjection` | 10 kolon; xmin için SQL yok; **bordro/para alanı** |
| 4 | Documents (B13) | `AddSharedFileTypesAndPayslipSupport` | Yeni tablo + varsayılanlı/nullable kolonlar |
| 5 | Announcements (B14) | `20260925115523_EnrichCompanyEvents` | Eski etkinlikleri "tüm gün"e çeviren UPDATE içerir |
| 6 | Onboarding (B15) | `OnboardingCategoriesAndTemplateSets` | FK (RESTRICT), `importance` varsayılan Normal |
| 7 | Notifications (B16) | `20260925115636_HrMessages` | 3 yeni tablo |
| 8 | Employee (B17) | `AddEmployeeNotes` | PII (çalışan notları) |
| 10 | Attendance (I0) | `20260925154835_AttendanceDayRowVersion` | xmin eşzamanlılık belirteci; şemaya DDL yok (yalnız migration geçmişi + snapshot) |
| 9 | Attendance (B1) | Geri-doldurma: SQL migration kaldırıldı → idempotent C# `PunchBackfillJob` | Karar: yaklaşım + ne zaman/nerede çalışacağı |

## B. B1 — bordroyu etkileyen davranış kararları
1. Bozuk cihaz sırası (`In,In,Out`) günü **Incalculable** yapar (worked/FM 0); eskiden ilk In kullanılırdı.
2. Self-servis In sonrası öğle Out→In okutması gerçek mola sayılır; net dakika değişebilir.
3. Elle/Excel girişi günün diğer okutmalarını gerekçeli soft-delete eder (eski "üzerine yaz" kuralı).
4. Incalculable gün aylık özette **eksik gün** gibi davranır. Bordro kapanışı engellensin mi?
5. Elle/Excel girilmiş güne cihaz okutması gelirse gün Incalculable olur (eskiden cihaz günü ezerdi).
6. Tek başına `Out` okutması Incalculable değil "eksik giriş" kalır (spec'ten bilinçli sapma).
7. K6 (onaylı ay kilidi) artık **tüm kanallarda** uygulanır: onaylı aya geç okutma eklenemez.
8. Mevcut tuhaflıklar OLDUĞU GİBİ bırakıldı (karakterizasyon testleriyle sabit): aylık özet FM 390 dk ↔ bordro provizyonel FM 360 dk; plansız pazar eksik günü düşürür; limitsiz gece dakikası 0.

## C. B2 kararları
- Tolerans içi geç giriş yine `Underwork` bayrağı alabilir.
- Aylık özet FM'i tanım min/maks eşiklerini görmez, bordro FM'i görür.
- Dinamik vardiya eşitliği (`IsAmbiguous`) artık I0'da Incalculable'a bağlandı (neden kodu `AmbiguousShift`) — kabul mü?

## C2. I0 (B1+B2+B3 entegrasyonu) — ek insan kararları
- Gece vardiyası çakışması (eşit uzaklık) artık TÜM kanallarda günü Incalculable yapar (eskiden yalnız `IPunchRecorder`'da).
- Yeniden hesap kiracı mesai limitlerini uygular; karar (ajan): yeniden hesap günü kayıt anındaki kanal politikasıyla kurar (Manual/Import limitsiz, diğerleri kiracı limitli) — mevcut davranış korunur, gece dakikası kayması önlenir. Kabul mü?
- Yayınlanmamış (taslak) vardiya atamaları plan sayılır (B9 rotasyon taslakları puantajı etkiler).
- Worker'da `IAttendanceDayRecalculator.EnqueueAsync` kuyruğu boşaltan servis yalnız API'de çalışır.
- AttendanceDay için xmin eşzamanlılık belirteci (kayıp güncelleme koruması) eklenecek (I0 tur 1).

## D. Yetki / kimlik / güvenlik
- Manager rolüne eklenen izinler: `attendance.punch.manage`, `attendance.punchrequest.manage`, `employee.notes.manage`.
- Identity davet akışı: `sendCredentials` bayrağı (varsayılan true). Web'de çalışan oluşturmada anahtar varsayılan AÇIK ve `identity.users.invite` iznine bağlı.
- Altyapı: ingress-nginx arkasında `UseForwardedHeaders` + `KnownNetworks` yapılandırılmamış; terminal ucunda IP sınırı bu yüzden yalnız BAŞARISIZ kimlik denemelerini sınırlıyor (T1 tur 2). Doğru istemci IP'si için proxy yapılandırması gerekir.
- T1 (terminal ucu, henüz yazılmadı): anahtar yalnız hash, sabit zamanlı karşılaştırma, kimlik değeri HMAC özeti, hız sınırı (bağlayıcı gereksinimler ledger'da).

## E. Bordro / ürün
- B12: B12 öncesi ödenmiş dönemlere `published_at` geriye dönük yazılmadı (o bordrolar "Bordrolarım"da görünmez) — doldurulsun mu?
- B12: `PayslipObjection` onay politikası varsayılanı yönetici→HR kuyruğu; itirazın bordro/İK'ya yönlenmesi için politika tanımlanmalı.
- B12: hatırlatma yalnız son 90 gün; yeniden hesapta onay kanıtı `acknowledgement_history`'ye arşivlenir.
- Mobil bordro: ekran görüntüsü engeli (FLAG_SECURE) kodlanmadı; bordro PDF indirme UI'ı kapsam dışı.
- Vekalet bitişi UTC tarihiyle (TR'de gece 00–03 arası bir gün kayabilir).

## F. Not
`main`'e birleştirme ve `a/zaman-devam` → `dev` PR'ının birleştirilmesi insandadır.
