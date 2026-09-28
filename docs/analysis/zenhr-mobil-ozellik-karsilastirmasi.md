# ZenHR mobil ↔ Sense HR mobil — özellik farkı özeti

- Tarih: 2026-09-27 · Açan: analist-zenhr · Kartlar: `docs/takim/isler/FG-001 … FG-029.json` (29 kart)
- İnceleme tabanı: `a/mobil-yeniden-tasarim` dalının çalışma ağacı (salt okuma), `src/Modules/*`, `web/src`.
- "Backend hazır" = gereken uçlar ve Employee rolünün çağırabileceği izin bugün var. "Mobil-only" = yalnız `takim-gelistirici-node` ile yapılabilir.

## Kart tablosu

| FG | Başlık | Öncelik | Büyüklük | Backend hazır mı | Mobil-only yapılabilir mi |
|---|---|---|---|---|---|
| FG-001 | Mobil çok dil altyapısı + Türkçe/İngilizce | yüksek | L | Evet (Accept-Language tr/en; self dil tercihi ucu opsiyonel) | Evet |
| FG-002 | Arapça dil ve RTL düzen | yüksek | L | Hayır (ar kaynakları FG-003'e bağlı) | Mobil kısmı evet, FG-003 sonrası |
| FG-003 | Backend Arapça kaynakları (resx, kültür listesi, bildirim şablonları) | yüksek | M | Hayır | Hayır (dotnet) |
| FG-004 | Eksik giriş/çıkış (missing punch) düzeltme talebi | yüksek | L | Hayır (çalışan talebi + workflow türü + migration) | Hayır |
| FG-005 | Akıllı giriş: geofence doğrulama + biyometrik teyit | yüksek | M | Kısmen (geofence kontrolü var; çalışanın lokasyon listesi ucu yok) | Hayır |
| FG-006 | Otomatik giriş/çıkış (auto clock) — spec §9 kapsam dışıydı | orta | L | Hayır | Hayır |
| FG-007 | Ekibim (yönetici): ekip puantajı, ekip listesi, ekip izinleri | orta | L | Kısmen (uçlar var, Employee grubundaki yöneticinin yetkisi yok) | Hayır (yetki kararı) |
| FG-008 | Onay geçmişi ve toplu onay | yüksek | M | Hayır (history + bulk uçları yok) | Hayır |
| FG-009 | Talep detayı: onay zinciri, durum, yorumlar | orta | M | Kısmen (durum ucu var, yorum yok) | Detay kısmı evet, yorum hayır |
| FG-010 | Genel İK talebi ve İK yazısı (HR letter / miscellaneous) | orta | L | Hayır | Hayır |
| FG-011 | Vize belge talebi (mobil) | düşük | M | Evet | Evet |
| FG-012 | Çalışan adına talep (on-behalf) | düşük | M | Evet (yalnız İK/yetkili rol) | Evet (yetkili kullanıcı için) |
| FG-013 | Belgelerim ve şirket belgeleri | yüksek | M | Kısmen (yükleme/gereksinim/paylaşılan dosya var; "belgelerimi listele" ucu yok) | Hayır (küçük self uç) |
| FG-014 | Profil sekmeleri: kişisel, iş/sözleşme, özel alanlar, kariyer, iletişim düzenleme | orta | M | Evet | Evet (KVKK kararı insan onayı) |
| FG-015 | Banka hesaplarım ve maaş geçmişim | orta | S | Evet | Evet |
| FG-016 | Zimmetlerim | orta | S | Evet | Evet |
| FG-017 | Görevlerim (işe alışım görevleri) | orta | S | Evet | Evet |
| FG-018 | Performans: değerlendirmelerim, hedef oluşturma, ekip hedefleri | orta | M | Kısmen (öz değerlendirme gönderme ve ekip yetkisi yok) | Faz 1 evet |
| FG-019 | Eğitimlerim ve yetkinlikler | düşük | S | Eğitim evet, yetkinlik hayır | Eğitim kısmı evet |
| FG-020 | Çalışma takvimi: vardiya + çalışma programı + izin/tatil günleri | orta | M | Evet | Evet |
| FG-021 | Kişi detayı ve organizasyon şeması | orta | M | Evet | Evet |
| FG-022 | Güvenlik ayarları: 2FA yönetimi + biyometrik giriş aç/kapa | orta | M | Evet | Evet |
| FG-023 | İş seyahati ve harcırah | orta | L | Hayır | Hayır |
| FG-024 | Aile bilgileri | düşük | M | Hayır | Hayır |
| FG-025 | Disiplin kayıtlarım | düşük | M | Hayır | Hayır |
| FG-026 | Sigorta bilgilerim (yan hak poliçeleri) | düşük | M | Hayır | Hayır |
| FG-027 | Açık tema ve tema seçici — spec §9 kapsam dışıydı | düşük | M | Gerekmez | Evet |
| FG-028 | Ana sayfa / ızgara düzenleme (Arrange) — spec §9 kapsam dışıydı | düşük | S | Gerekmez | Evet |
| FG-029 | Kurumsal hesapla giriş (SSO) | düşük | L | Hayır | Hayır |

Tam mobil-only yapılabilenler (12): FG-001, FG-011, FG-012, FG-014, FG-015, FG-016, FG-017, FG-020, FG-021, FG-022 ve backend gerektirmeyen FG-027/FG-028. Faz 1'i mobil-only olanlar: FG-009 (detay), FG-018, FG-019.

Riskli alan (para/PII/auth/migration → insan onayı): FG-004, 005, 006, 007, 010, 013, 014, 015, 022, 023, 024, 025, 029.

Yetki bulgusu (birden çok kartı etkiliyor): `SystemRoleDefinitions` yorumuna göre ekip yöneticileri artık "Çalışan" grubunda. Employee rolünde `attendance.timesheet.view`, `leave.requests.read`, `employee.employees.read`, `performance.team.goals` yok. Bu yüzden ZenHR MSS'in ekip görünümleri (FG-007, FG-018 ekip sekmesi, FG-012) bir yetki modeli kararına bağlı.

## Zaten karşıladığımız ZenHR özellikleri (kanıtla)

| ZenHR özelliği | Sense HR karşılığı |
|---|---|
| Ana sayfada kalan izin bakiyeleri | `screens/app/home/BalanceCarousel.tsx`, `GET leave/me/balances` |
| "My Actions" ızgarası | `home/ActionGrid.tsx` (Puantaj, İzinlerim, Bordro, Onaylarım, Taleplerim, Kişiler, Vardiyam, Hedeflerim, Destek) |
| Bugün izinde olanlar / uzaktan çalışanlar | `home/PeopleOffCard.tsx`, `HomeFeedService` (`leaveTypeCode === "remoteWork"`) |
| Yaklaşan doğum günleri, etkinlikler, duyurular | `home/BirthdayCard.tsx`, `EventCard.tsx`, `AnnouncementCard.tsx` |
| Talep türleri sheet'i (izin, fazla mesai, mali işlem) | `navigation/RequestSheet.tsx` + `lib/requestOptions.ts` (İzin, Fazla mesai, Masraf, Avans) |
| Kredi (Loans) | Avans taksitli talep: `AdvanceRequest.IsInstallmentRequested/InstallmentCount`, mobil `ExpenseService` |
| İzin formu: tek gün/çok gün tarih sayfası + kameradan ek | `NewRequestScreen` + `components/leave/useLeaveDocuments.ts` (`launchCameraAsync`) |
| Giriş/çıkış/mola, harita, konum doğruluğu, çalışma süresi sayacı | `CheckInScreen`, `checkin/MapHeader.tsx`, `ClockCard.tsx`, `PunchTiles.tsx`; `POST self-checkin/gps` (breakStart/breakEnd) |
| Onaylarım + tür filtresi + onay/red gerekçesi | `ApprovalsScreen`, `approvals/ApprovalTypeSelect.tsx`, `ApprovalRejectSheet.tsx` |
| Kendi puantaj geçmişi + dönem filtresi | `AttendanceScreen`, `attendance/PeriodStepper.tsx`, `GET attendance/timesheet/me` |
| Bordro pusulaları görüntüle/indir | `PayrollScreen`, `GET payroll/payslips/my`, `…/document`, okundu onayı |
| Çalışan rehberi: arama, telefon, e-posta | `PeopleScreen` (`GET employees/directory`, `tel:`/`mailto:`) |
| Vardiya görüntüleme | `ShiftScreen` (`GET attendance/shifts/me`), üstüne müsaitlik bildirimi |
| Hedef/OKR ilerlemesi | `GoalsScreen` (`GET performance/goals/me`, ilerleme güncelleme) |
| Push + uygulama içi bildirim, tercihler | `NotificationsScreen`, `NotificationPreferencesScreen`, `PushNotificationService` |
| İşlem geçmişi ("Transactions") | `RequestsScreen` (izin, fazla mesai, masraf, avans + geri çekme); detay/yorum FG-009 |
| Face ID / parmak izi ile giriş | `screens/auth/BiometricPromptScreen.tsx`, `lib/biometric.ts` |
| 2FA ile giriş, şifre sıfırlama | `auth/login/MfaCodePanel.tsx`, `ForgotPasswordScreen` (2FA yönetimi FG-022) |
| Koyu mod | Uygulama tek tema koyu (`app.json userInterfaceStyle: dark`); açık tema FG-027 |
| Rol bazlı erişim | `RequiresPermission` + `SystemRoleDefinitions` (Employee = self-servis) |
| Talep → onay → bildirim → İK/puantaj/bordroya yansıma | İzin, fazla mesai, masraf, avans için Workflow + outbox ile var; eksik giriş/çıkış FG-004, yazı FG-010 |
| (Fazlası) Destek merkezi | `SupportScreen` |

## Kart açılmayan / uygulanamaz

- **Altibbi doktor danışmanlığı** (blog): bölgesel iş ortağı entegrasyonu, İK çekirdeği değil.
- **Bayram temaları** (App Store 30.8.13): kozmetik.
- **Denetim izi (audit trail) görünürlüğü** (yalnız ikincil kaynak): ZenHR mobilinde resmi kaynakta yok. Web'de İK için `pages/audit-log.tsx` + `identity.audit.view` var, mobil ESS'e uygun değil.
- **Saat kartında GMT farkı** (video): küçük gösterim ayrıntısı, gerekirse FG-005 içinde ele alınır.
- **Şirket varlığı talep etme** (blog): FG-016 faz 2 notu olarak yazıldı.

## İkincil kaynak (ChatGPT) maddelerinin eşlemesi

Dashboard → karşılanıyor, görevler FG-017 · Profil/sözleşme → FG-014 · Puantaj geçmişi → karşılanıyor · GPS/geofence → FG-005 · Biyometrik → karşılanıyor · İzin/fazla mesai → karşılanıyor · Eksik okutma → FG-004 · Kredi → karşılanıyor (taksitli avans) · Masraf → karşılanıyor · İK yazısı → FG-010/FG-011 · Onaylar → karşılanıyor + FG-008 · Vardiya → FG-020 · Bordro → karşılanıyor · Belge yükleme → FG-013 · Rehber → FG-021 · İşlem geçmişi → karşılanıyor + FG-009 · Performans → FG-018 · Görevler → FG-017 · Bildirimler → karşılanıyor · Dil → FG-001/002/003 (hedef tr/en/ar) · Koyu mod → karşılanıyor, açık tema FG-027 · RBAC → karşılanıyor; audit trail → uygulanamaz · MSS ekip puantajı/listesi → FG-007, ekip OKR → FG-018.

## Kaynaklar

- Resmi: App Store listesi https://apps.apple.com/us/app/zenhr/id1376637090 (açıklama + sürüm notları 30.8.7–30.9.2; ekran görüntüsü yazıları sayfa metninde yok; "Languages" alanında yalnız English var)
- Resmi: https://www.zenhr.com/en/mobile (ESS/MSS özellik listesi)
- Resmi: ZenHR destek makalesi "ZenHR Mobile ESS" https://zenhrsolutions.freshdesk.com/en/support/solutions/articles/43000745169 (profil sekmeleri, talep türleri, auto clocking, 2FA, SSO, düzen özelleştirme)
- Resmi: https://blog.zenhr.com/en/zenhrs-mobile-app-simple-self-service-for-employees-hr-teams
- Referans: tanıtım videosu + mağaza ekran görüntüleri (spec `docs/superpowers/specs/2026-09-27-mobil-yeniden-tasarim-design.md` referansı, istemde özetlenen liste)
- İkincil, doğrulanamadı: kullanıcının ilettiği ChatGPT özeti (kartlarda "ikincil (ChatGPT), doğrulanamadı" olarak işaretli)
- Erişilemeyen: Google Play sayfası denenmedi. App Store ekran görüntüsü yazıları metin olarak alınamadı. ZenHR'ın Arapça arayüz desteği resmi kaynakta teyit edilemedi.
