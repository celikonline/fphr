# senseik-037 — Dini bayramlar (Ramazan/Kurban) çok yıllı takvime yüklendi

Branch: `a/holiday-calendar-multiyear` · Kart: `docs/agent-board/cards/senseik-037-holiday-calendar-multiyear.json`

## Özet

Organization modülünün resmi tatil takvimi (`HolidayCalendar` / `HolidayCalendarEntry`,
roadmap Faz 0 madde #5) sabit tarihli tatilleri zaten `IsRecurringAnnually=true` ile
yıldan bağımsız bir kural olarak modelliyordu (tek satır, her yıl Month/Day eşleşmesiyle
otomatik geçerli — bkz. `HolidayCalendarEntry.MatchesDate`). Dini bayramlar (Ramazan/Kurban)
ise hicri takvime bağlı olduğundan her yıl için ayrı, sabit bir Gregorian tarih satırı
gerektiriyor (`IsRecurringAnnually=false`) ve seed dosyası sadece 2026-2027 için tanımlıydı.

Bu kart üç şeyi ekledi:

1. `src/Sense.Api/SeedData/holidays/tr-official-holidays.json`'a **2028-2031** için
   Ramazan Bayramı ve Kurban Bayramı (arife + tam günler) satırları eklendi — mevcut
   2026/2027 satırları **değiştirilmedi**, sadece sona eklendi. Artık dosyada 2026-2031
   arası kesintisiz 6 yıllık dini bayram verisi var.
2. `Organization.Contracts.IHolidayCalendarLookup`'a yeni bir salt-okunur metod eklendi:
   `GetLastDefinedReligiousHolidayYearAsync(tenantId)` — tenant'ın etkin takviminde
   non-recurring (dini bayram) girdisi bulunan **en son yıl**ı döner; takvim yoksa veya
   hiç dini bayram girdisi yoksa `null` döner (bu, "uyarma" anlamına gelir, "her zaman uyar"
   değil). `HolidayCalendarLookup` implementasyonunda `HolidayCalendarEntries` tablosundan
   `!IsRecurringAnnually` filtresiyle `Date.Year` MAX'i alınarak hesaplanıyor.
3. Leave modülünde (`LeaveRequestPreview.BuildAsync`, hem `PreviewMyLeaveRequestQuery`
   hem `PreviewEmployeeLeaveRequestQuery` — yani hem çalışanın kendi önizlemesi hem
   İK'nın "adına oluştur" önizlemesi) bu bilgiyi kullanan **non-blocking bir uyarı**
   eklendi: talebin bitiş tarihinin yılı, takvimde tanımlı son dini bayram yılından
   büyükse, `Warnings` listesine `leave.errors.holiday_calendar_year_undefined` kodlu
   bir `Error` ekleniyor (`{year}` parametresiyle). Talep yine de normal şekilde
   önizlenip gönderilebiliyor — bu, mevcut `InsufficientBalance` uyarısıyla aynı
   "bilgilendirici, engellemeyen" kanalı kullanıyor (roadmap S-7 ekip çakışması uyarısıyla
   aynı desen).

## Neden sadece Preview'de (submit response'ta değil)?

Kart maddesi 3 "kullanıcıya/İK'ya bir uyarı gösteriliyor" diyor. Web akışında hem
çalışanın kendi izin formu hem İK'nın "adına oluştur" formu, gönderim öncesi
`GET .../requests/preview` çağrısını her tarih değişikliğinde tetikliyor (bkz.
`LeaveTextLocalizer.Localize(Result<LeaveRequestPreviewDto>)` → `LeaveRequestPreviewResponse.Warnings`,
zaten kodu+mesajı ayrı ayrı localize edip dönüyor). Yani uyarı, talep gönderilmeden
**önce**, tam da tarih seçilirken ekranda beliriyor — hem çalışan hem İK bunu görüyor.
`SubmitLeaveRequestCommand` / `CreateLeaveRequestOnBehalfCommand`'ın kendisine ayrıca
bir `Warnings` alanı eklemedim: `LeaveRequestDto` birçok endpoint'te (liste, detay,
onay sonrası vs.) tekrar kullanılıyor ve "gönderim anındaki geçici bir uyarıyı" kalıcı
DTO'ya taşımak (persist edilmeyen bir alan, sadece bir response'ta anlamlı) ek karmaşıklık
getiriyordu; Preview zaten aynı bilgiyi submit'ten önce sağladığı için bu, kapsamı
gereksiz büyütmeden aynı riski kapatıyor. İleride "submit anında da göster" istenirse
`LeaveRequestPreview.BuildHolidayCalendarCoverageWarningAsync`'i `SubmitLeaveRequestHandler`
içinde de çağırıp `LeaveRequestDto`'ya opsiyonel bir `Warnings` alanı eklemek yeterli.

## Kaynak tarihler ve doğrulama

- **2026-2027**: Dosyada zaten mevcuttu, bu kartta değiştirilmedi (önceki iş tarafından
  tahmini/Diyanet referanslı olarak eklenmiş).
- **2028-2031**: WebSearch ile araştırıldı — birincil kaynak Diyanet İşleri Başkanlığı'nın
  hesaplamalı dini günler takvimi (`vakithesaplama.diyanet.gov.tr/icerik.php?icerik=185..188`,
  2028/2029/2030/2031 sayfaları), ikincil çapraz kontrol `calendardate.com`'un
  Eid al-Fitr/Eid al-Adha sayfaları. Kullanılan tarihler:

  | Yıl | Ramazan Bayramı Arife | Ramazan 1-3. gün | Kurban Bayramı Arife | Kurban 1-4. gün |
  |---|---|---|---|---|
  | 2028 | 2028-02-25 | 02-26 / 02-27 / 02-28 | 2028-05-04 | 05-05 / 05-06 / 05-07 / 05-08 |
  | 2029 | 2029-02-13 | 02-14 / 02-15 / 02-16 | 2029-04-23 | 04-24 / 04-25 / 04-26 / 04-27 |
  | 2030 | 2030-02-03 | 02-04 / 02-05 / 02-06 | 2030-04-12 | 04-13 / 04-14 / 04-15 / 04-16 |
  | 2031 | 2031-01-23 | 01-24 / 01-25 / 01-26 | 2031-04-01 | 04-02 / 04-03 / 04-04 / 04-05 |

  **Not / varsayım**: Diyanet'in astronomik hesaplama kriteriyle bazı genel (Türkiye'ye
  özel olmayan) hicri takvim dönüştürücüleri (ör. calendardate.com) arasında yıl başına
  ±1 gün fark bulundu (Ramazan Bayramı'nda her 4 yılda da, Kurban Bayramı'nda 2030 ve
  2031'de). Bu beklenen bir varyasyon (hilal görünürlüğü/hesaplama kriteri farkı) —
  Türkiye'nin resmi tatil takvimi Diyanet'in hesabına dayandığından seed'e Diyanet
  tarihleri yazıldı. **Bu tarihler Diyanet'in resmi taktvimi Cumhurbaşkanlığı kararnamesiyle
  yayımlandığında (genelde ilgili yıldan ~1-2 yıl önce) tekrar teyit edilmeli** — şu an
  4-5 yıl ileriye dönük hesaplamalı/tahmini tarihlerdir, seed dosyasındaki `note` alanı
  bunu açıkça belirtiyor.
  - 2028: https://vakithesaplama.diyanet.gov.tr/icerik.php?icerik=185
  - 2029: https://vakithesaplama.diyanet.gov.tr/icerik.php?icerik=186
  - 2030: https://vakithesaplama.diyanet.gov.tr/icerik.php?icerik=187
  - 2031: https://vakithesaplama.diyanet.gov.tr/icerik.php?icerik=188
  - Çapraz kontrol: calendardate.com'un ilgili yıl `eid_al_fitr_YYYY.htm` / `eid_al_adha_YYYY.htm` sayfaları.

## Değiştirilen/eklenen dosyalar

- `src/Sense.Api/SeedData/holidays/tr-official-holidays.json` — 2028-2031 dini bayram girdileri eklendi (append-only, mevcut satırlar dokunulmadı).
- `src/Modules/Organization/Sense.Modules.Organization.Contracts/IHolidayCalendarLookup.cs` — `GetLastDefinedReligiousHolidayYearAsync` eklendi.
- `src/Modules/Organization/Sense.Modules.Organization.Infrastructure/Lookups/HolidayCalendarLookup.cs` — implementasyon (tek SQL `MAX`, nullable projeksiyon; boş kümede `null`).
- `src/Modules/Leave/Sense.Modules.Leave.Application/Requests/LeaveRequestCore.cs` — `LeaveRequestErrors.HolidayCalendarYearUndefined` kodu.
- `src/Modules/Leave/Sense.Modules.Leave.Application/Requests/LeaveRequestPreview.cs` — `IHolidayCalendarLookup` enjekte edildi (her iki preview handler'a), kapsam uyarısı `Warnings`'e ekleniyor.
- `src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.resx` / `.en.resx` — TR/EN uyarı mesajı.
- `tests/Modules/Sense.Modules.Organization.Tests/TrHolidaySeedFileTests.cs` — yeni test: dini bayram verisinin en az 2027-2031 arası kesintisiz tanımlı olduğunu ve her yılda Ramazan (arife+3) / Kurban (arife+4) girdilerinin eksiksiz ve tekrarsız olduğunu doğruluyor (yıl sabit kodlanmış, "bugün+5" değil — 2031'e ulaşıldığında sessizce yeşil kalmasın diye).
- `tests/Modules/Sense.Modules.Organization.Tests/HolidayCalendarIntegrationTests.cs` — `GetLastDefinedReligiousHolidayYearAsync`'in recurring girdiyi saymadığını, takvimsiz tenant'ta `null` döndüğünü doğrulayan assertion'lar + sadece recurring girdisi olan takvimde `null` dönen ayrı test.
- `tests/Modules/Sense.Modules.Leave.Tests/LeaveRequestTests.cs` — `Preview_WarnsWhenRequestFallsInAYearTheReligiousHolidayCalendarHasNoDataFor` testi.
- `docs/agent-board/cards/senseik-037-holiday-calendar-multiyear.json` — kart (dev'deki kabul kriterleri korunarak `review`'a alındı).
- `docs/analysis/kolayik-video/faz0-durum-takibi.md` — #5 satırı + tarihçe notu.

## Kalan/gelecek iş (bilinçli kapsam dışı)

- Submit/OnBehalf komutlarının response DTO'suna aynı uyarının eklenmesi (yukarıda
  açıklandığı gibi Preview zaten aynı korumayı sağlıyor; istenirse küçük bir takip kartıyla eklenebilir).
- 2032+ için otomatik hatırlatma/bildirim (bu kart sadece "izin talebi anında uyar"
  kriterini karşılıyor; ayrı bir "takvim N ay içinde dolacak" proaktif bildirimi
  roadmap'te ayrı bir madde olarak açılabilir).
- **Mevcut kiracılar**: `DataSeeder.SeedTrHolidayCalendar` seed dosyasını yalnızca kiracının TR takvimi ilk
  oluşturulurken uygular. Var olan kiracıların (ve mevcut demo DB'nin) takvimine 2028-2031 satırları otomatik
  eklenmez — demo için DB'yi yeniden seed'lemek yeterli; canlı kiracılar için eksik dini bayram satırlarını
  idempotent ekleyen bir başlangıç senkronizasyonu (`SystemRolePermissionSynchronizer` benzeri) ayrı takip kartı olmalı.

## Doğrulama (2026-09-19)

- `dev`'e rebase edildi (kart JSON'u add/add çakışması: dev'deki kabul kriterleri korundu, state `review`).
- `dotnet test tests/Modules/Sense.Modules.Organization.Tests`: 112/112 yeşil (bu kartın son kodu, rebase öncesi).
- `Leave.Tests`: son koşu Docker Desktop yanıt vermediği için tamamlanamadı (43 test `DockerUnavailableException`).
  Önceki koşudaki 20 hata bu karttan değil, dev'de senseik-030 ile düzeltilen eksik `IEmployeeBirthDateLookup`
  test DI kaydından kaynaklanıyordu. Merge sonrası `dotnet test tests/Modules/Sense.Modules.Leave.Tests` tekrar koşulmalı
  (yeni test: `Preview_WarnsWhenRequestFallsInAYearTheReligiousHolidayCalendarHasNoDataFor`).
- EF modeli değişmedi → migration yok. Web değişikliği yok (uyarı mevcut preview `warnings` kanalıyla localize edilip dönüyor).
