# Faz 1b Durum Takibi (PC-B: Workflow / Notifications / Leave)

Kaynak: [11.7-yol-haritasi.md](./11.7-yol-haritasi.md) Faz 1 satırları #14–#31, #42–#44; ayrıntı [11.3.05-izin-ve-fazla-mesai.md](./11.3.05-izin-ve-fazla-mesai.md) (izin/FM), [11.3.04-onay-surecleri.md](./11.3.04-onay-surecleri.md) (onay), [11.3.13-bildirim-ana-sayfa-duyuru-destek-ui.md](./11.3.13-bildirim-ana-sayfa-duyuru-destek-ui.md) (bildirim). Employee/özlük kalemleri (#32–#45) PC-A hattındadır, bkz. [faz1a-durum-takibi.md](./faz1a-durum-takibi.md).

**Son güncelleme:** 2026-09-16 (#14 izin türü sihirbazı arayüzü)
**Nasıl güncellenir:** Kalem durumu değişince satırı koda göre güncelle, tarihi değiştir. Durumlar: ❌ Başlanmadı · 🟡 Kısmi · ✅ Bitti.

## Envanter (2026-09-16, koda göre)

Backend (Leave/Workflow/Notifications) büyük ölçüde önceki oturum(lar)da yazılmış; bu dosya o envanteri ilk kez tabloya döküyor.

- **Leave**: `LeaveRuleSet`/`LeaveType` (33 alanlık `LeaveTypeSettings`, kıdem kademeleri), `LeaveRequest`, `LeaveLedgerEntry` (append-only), `EmployeeLeaveProfile`, `Overtime{Request,Consent,Conversion}`, `LeaveBalanceEngine`. Migration'lar: `InitialLeave`, `LeaveOvertime`, `LeaveManagement`, `PayrollLegislationParameters`. 44 test. Web: `my-leave`/`leave-calendar`/`my-overtimes` sayfaları, `leave-rule-sets.tsx` (tür **görüntüleme**, ekleme/düzenleme arayüzü bu oturuma kadar yoktu).
- **Workflow**: `ApprovalPolicy`/`ApprovalPolicyStep` (max 6, `IsRequired`, `ShowCurrentApproverToRequester`), `ApprovalRequest`/`ApprovalTask`/`ApprovalHistoryEntry`/`EmailDecisionToken`. Tek migration (`InitialWorkflow`). Web: `approval-flows.tsx`, `inbox.tsx`.
- **Notifications**: `UserNotification`, `NotificationDelivery`, `NotificationPreference`; şablonlar dosya tabanlı (`.sbn`, Scriban), veritabanı entity'si yok. Tek migration (`InitialNotifications`). Web: `notification-bell.tsx`, `notifications/preferences.tsx`.

## Faz 1b kalemleri

| # | Kalem | Durum | Kanıt | Eksik / Not |
|---|---|---|---|---|
| 14 | İzin türü sihirbazı seçenek seti (Limitsiz/Limitli, hak ediş, devir, kıdem kademeleri…) | 🟡 Kısmi | Backend tam (`LeaveType.cs` 33 alan + `Validate()`), uçlar `POST/PUT /leave/rule-sets/{id}/types[/{typeId}]`. **2026-09-16'da eklendi**: web sihirbazı `web/src/components/leave/leave-type-dialog.tsx` (genel/limit/kurallar bölümleri, kıdem kademesi editörü) + `leave-rule-sets.tsx`'te "Yeni Tür ve Kural Ekle" + satırda Düzenle/Etkinleştir-Devre dışı bırak | Sunucu tarafı doğrulama hataları genel `getApiErrorMessage` ile gösteriliyor (alan bazlı client-side mesaj yok); tarayıcıda elle denenmedi |
| 15 | Kural setleri = paket ataması; kopyala-düzenle, varsayılan set, toplu atama | ✅ Bitti | `LeaveRuleSetsController` clone/set-default/assign; `leave-rule-sets.tsx` tam arayüz |
| 16 | Kıdem kademeleri düzenlenebilir + yaş istisnası + aylık hak ediş | ✅ Bitti | `LeaveSeniorityTier`, `AccrualFrequency.Monthly`, `AgeExceptionEnabled/MinDays` — artık web sihirbazında da düzenlenebilir |
| 17 | İzne esas hak ediş tarihi + kümülatif geriye dönük tahakkuk (`RecalculateFrom`) | 🟡 Kısmi | `EmployeeLeaveProfile.LeaveAccrualStartDate`, `POST /leave/employees/{id}/recalculate` var | `RecalculateFrom` tarih parametresi yok — 0 sonuç |
| 18 | Bakiye projeksiyon alanları (13 alan) + Ekstra izinler + ücretsiz izin öteleme | 🟡 Kısmi | `LeaveBalanceEngine`/`LeaveBalance` 16 alan, `UnpaidLeaveShiftsAccrual` bayrağı | "Ekstra izinler" ayrı liste arayüzü yok |
| 19 | Açılış bakiyesi = denkleştirme; izin şablonu kolonları (Kullanılan gün, İmzalı) | 🟡 Kısmi | `LeaveLedgerEntryType.OpeningBalance` var | `SignedFormReceived`, aktarım şablonu kolonları yok |
| 20 | Hazır izin türleri seed | ✅ Bitti | `LeaveRuleSetSeed.cs` 14 kod (mazeret, hastalık, iş arama, doğum günü, bedelli askerlik, doğum sonrası, koruyucu aile dahil) |
| 21 | Mevzuat parametreleri (cumartesi, ihbar-izin çakışma, yol izni, rehire kıdem, yarı zamanlı…) | 🟡 Kısmi | `LegislationParameterSeed.cs` çoğu anahtarı içeriyor | `WorkPattern`, ihbar-izin çakışma engeli, yol izni toplamı motor kuralı yok |
| 22 | 7578 s.K. güncellemesi (doğum sonrası 16 hafta, babalık 10 gün…) | ✅ Bitti | `LegislationParameterSeed.cs` "seed version 2026.05", 2026-05-01 tarihli |
| 23 | Çalışma takvimi düzeyinde izin sayım kuralı, takvim tanımı | ❌ Başlanmadı | `WorkCalendar`/`IncludeRestDayWhenFullWeek` kod tabanında yok (0 sonuç) |
| 24 | Yönetici izin ekleme: onaysız anahtarı, gün sayısını elle değiştirme, bilgilendirme e-postası | 🟡 Kısmi | `CreateLeaveRequestOnBehalfCommand(..., BypassApproval)` var | Elle gün sayısı değiştirme, bilgilendirme e-postası yok |
| 25 | İzin formu PDF/Word + imza bayrağı + mutabakat formu | ❌ Başlanmadı | `SignedFormReceived`, `IDocxRenderer` — 0 sonuç; Documents'a bağımlı |
| 26 | FM talebi alanları + "kendi FM kayıtlarını görebilsin" ayarı | ✅ Bitti | `OvertimeRequestFeatures.cs`, `my-overtimes.tsx` |
| 27 | FM → Ödemeye çevir / Ekstra izne çevir (onaydan sonra, yönetici) | 🟡 Kısmi | `OvertimeConversionKind` enum'u var, yalnız `TimeOff` dalı kullanılıyor (`OvertimeConversion.cs:99`) | Ödemeye çevirme (çarpan/net-brüt) dalı yok |
| 28 | İzin türü seçiminde açıklama/uygunluk rehberliği | ❌ Başlanmadı | Yalnız `LeaveType.Description` var, rehberlik arayüzü yok |
| 29 | Onay süreci = talep türü başına sıralı onaycı listesi, birim ataması, "kimde olduğunu gör" | ✅ Bitti | `ApprovalPolicy.ShowCurrentApproverToRequester`, `OrgUnitApprovalPolicyAssignment` (devralma) |
| 30 | Onay şablonlarında zorunlu/opsiyonel onaycı, talep anında düzenleme, Admin doğrudan onay, e-postadan onay | 🟡 Kısmi | `ApprovalPolicyStep.IsRequired`, `inbox/{id}/override-decide`, `decisions/by-token` | Talep anında onaycı listesi düzenleme yok |
| 31 | Vize belgesi talebi + belge süreçleri | ❌ Başlanmadı | `VisaDocument` — 0 sonuç |
| 42 | Tek "Talep" butonu (İzin/Avans/Harcama/FM/Vize) | ❌ Başlanmadı | `web/src/config/navigation.ts`/`App.tsx`'te birleşik talep menüsü yok |
| 43 | Kullanıcı bazlı e-posta bildirim koşulları; talep e-postaları; IP/tarih log | ✅ Bitti | `NotificationPreference.cs`, `Templates/request.{created,approved,rejected}.*`, `LeaveRequest.SubmittedIpAddress` |
| 44 | İzin bakiye raporları (özet + ayrıntılı, asenkron, e-posta) | ❌ Başlanmadı | `leave.balance.summary`/`.detail` — 0 sonuç; Analytics'te yok |

## Değişiklik geçmişi

- 2026-09-16: Dosya oluşturuldu; başka bir Claude oturumunun (aynı çalışma dizininde, PC-A hattı Faz 1a'yı bitirdikten sonra) çıkardığı Leave/Workflow/Notifications kod envanteri tabloya işlendi.
- 2026-09-16: #14'ün web sihirbazı eklendi. `web/src/services/leave.service.ts`'e `addLeaveType`/`updateLeaveType`/`setLeaveTypeDisabled` + `LeaveSeniorityTier` tipi; `web/src/components/leave/leave-type-dialog.tsx` (Genel/Limit ve hak ediş/Kullanım kuralları bölümleri, limit tipine göre koşullu alanlar, yalnız `Annual` türde kıdem kademesi editörü); `leave-rule-sets.tsx`'e ekleme butonu + satır başı düzenle/etkinleştir-devre dışı bırak. Yeni türler her zaman `isStatutory=false` (API zorluyor), bu yüzden arayüzde o anahtar yok. Doğrulama: web `tsc`/`eslint`/`vite build` (Docker `node:24-alpine`) temiz; backend değişmedi (mevcut uçlar kullanıldı). Arayüz tarayıcıda elle denenmedi. tr/en/de çevirileri eklendi (`leave:types.*`, `leave:negativeBalance.*`, `leave:accrualTiming.*`, `leave:accrualBasis.*`, `leave:ruleSets.enable/disable`).
