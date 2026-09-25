# FX2 — web takip düzeltmeleri

1. Bildirim adları: tr/en/de notifications.json'a katalogdaki eksik tüm şablonlar (7 istenen + leave.added_on_behalf, onboarding.task_*) ve `category.onboarding`. Anahtar biçimi mevcut kalıpta (`template.<grup>.<ad>` düz metin; `.name` eki kullanılmadı, preferences.tsx bu biçimi okuyor). Test: web/src/i18n.notification-templates.test.ts (backend anahtarları sabit).
2. incalculableReason: aylık gün çipi etiketi ve ekip puantajı hücre/kart ipucu (TeamWeekDay.incalculableReason opsiyonel); ambiguousShift çevirisi eklendi; yardımcı `incalculableReasonText`. Testler: monthly-timesheet.test.tsx, team-timesheet.test.tsx (yeni).
3. device-log-import: hata yolu getAttendanceErrorMessage ile; test device-log-import.test.tsx (yeni).
4. Çoklu çalışan seçici maxSelection testleri (sayaç, kırmızı uyarı, onay kilidi, prop yokken aynı); overLimit metni 'seçim' genel (tr/en/de).
5. Onboarding kategori Sil -> ConfirmModal (+ categories.confirmDelete); kullanılmayan dailyList.unassigned kaldırıldı; grup silme testine onay sonrası deleteMessageGroup assertion'ı.

Notlar: günlük liste DailyAttendanceRowDto ipucu BX1 şeridinde. Backend ekip haftası/ayı DTO'sunun incalculableReason göndermesi ayrı iş (alan opsiyonel).
Kanıt: tsc temiz; eslint 0 hata; vitest 123 dosya / 1136 test yeşil.
