# senseik-041 — Date Picker Sorunları — UYGULAMA NOTU

## Durum

**GATE 1 ve GATE 2 onaylı (önceden verildi), uygulandı, branch `a/date-picker-fixes`. Push yapılmadı.**

Ana dosya: `web/src/components/ui/date-picker.tsx` (kartın istediği gibi). İki yan dosya da dokunuldu, gerekçesi
aşağıda: `web/src/components/leave/leave-request-dialog.tsx` (Bug 3'ün asıl kök nedeni bu dosyadaki `Modal`),
`web/src/pages/settings/holiday-calendars.tsx` (Bug 4'ü gerçek bir çağrı noktasında göstermek için).

## Bug 1 — Format: dd.MM.yyyy

`valueFormat` daha önce dile göre `"MMM D, YYYY"` / `"D MMM YYYY"` idi (ay adıyla, örn. "18 Eyl 2026"). Artık dilden
bağımsız sabit `"DD.MM.YYYY"` (dayjs format token'ları) — `18.09.2026`. Türkiye pazarı için standart numerik format;
`i18n.resolvedLanguage`'a artık ihtiyaç kalmadığı için `i18n` destructure'ı da kaldırıldı (`t` kaldı: placeholder/clear
metinleri için).

## Bug 2 — İki tarih seçici art arda açılınca üst üste binme

**Kök neden:** Mantine'in `DatePickerInput`'u kendi açık/kapalı durumunu tamamen kendi içinde (`useDisclosure`) yönetiyor
— kardeş `DatePicker` örnekleri arasında hiçbir "sadece biri açık olsun" garantisi yok. Hızlı art arda tıklama/Tab ile
geçiş sırasında önceki popover'ın kapanma geçişi bitmeden ikincisi açılırsa, ikisi de aynı anda DOM'da kalıp üst üste
biniyor ve geçiş animasyonu yüzünden saydam görünüyor.

**Çözüm:** `date-picker.tsx` içinde modül seviyesinde küçük bir kayıt defteri (`closeById` Map + `openInstanceId`).
Her `DatePicker` örneği `useId()` ile kendine sabit bir id alıyor ve `useEffect` ile kendi "beni zorla kapat"
callback'ini (`setGeneration` state'ini artırır) bu deftere kaydediyor. Girdiyi saran `<div style={{display:
"contents"}} onPointerDownCapture={...}>` — capture fazında (Mantine'in kendi tıklama handler'ından ÖNCE) çalışıp
`claimOpen(instanceId)` çağırıyor: başka bir örnek zaten "açık" işaretliyse onun kayıtlı callback'i tetiklenir, o
örneğin `<DatePickerInput key={generation}>` `key`'i değişir, React onu **anında unmount/remount** eder — Mantine'in
kendi kapanma animasyonunu beklemeden, kesin ve testable bir şekilde. `display:contents` seçildi ki bu sarmalayıcı
hiçbir grid/flex layout'u etkilemesin (mevcut hiçbir çağrı noktası `className` prop'unu kullanmıyor, doğrulandı).

Bu mekanizma uygulama genelinde geçerli (tek bir modül-seviyesi singleton) — sadece izin formu değil, `audit-log.tsx`
filtresi gibi diğer başlangıç/bitiş çiftlerini de kapsıyor.

## Bug 3 — KRİTİK: dışarı tıklayınca form verisi kaybı

**Kök neden ayrımı:** `date-picker.tsx`'in kendisi controlled bir bileşen (`value`/`onChange` parent'ta) — kendi
içinde kaybedilecek "taslak" veri tutmuyor; test'te doğrulandı ki takvimi dışarı tıklayarak kapatmak `onChange`'i
tetiklemiyor ve gösterilen değer değişmiyor (bkz. testler). Asıl veri kaybı, iki tarih seçicinin (başlangıç/bitiş)
birlikte kullanıldığı **`LeaveRequestDialog`'un sarmalayıcı `Modal`'ında**: takvim popover'ı `withinPortal` ile
`document.body`'ye portallanıyor; kullanıcı sadece takvimi kapatmak isterken attığı bir tıklama modal içeriğinin
DOM ağacı dışında bir yere denk gelirse, Mantine `Modal`'ın varsayılan `closeOnClickOutside` davranışı devreye girip
**tüm formu** (tür, tarihler, açıklama) kaydetmeden kapatıyor.

**Çözüm:** `leave-request-dialog.tsx`'teki `<Modal>`'a `closeOnClickOutside={false}` eklendi — form artık yalnızca
açık "İptal" butonu veya Escape ile kapanıyor, kazara dış tıklama ile değil. Bu, kartın kabul ettiği asgari çözümden
("en azından onay") daha güçlü: onay istemek yerine kazara kapanmayı tamamen engelliyor.

**Kapsam notu:** Aynı "iki DatePicker + Modal" deseni başka dosyalarda da var (`assets-section.tsx`,
`lifecycle-actions.tsx`, `overtime-section.tsx`, `payments-section.tsx`, `union-section.tsx`, `subcontractors.tsx`,
`review-cycles.tsx`) ama bunlar paralel-geliştirme planında (§4-5) farklı makine/lane'lere ait Employee/Payroll
dosyaları — bu kart kapsamında dokunulmadı, aynı `closeOnClickOutside={false}` yamasının oralarda da uygulanması
öneriliyor (backlog).

## Bug 4 — Resmi tatiller takvimde işaretlensin

`DatePicker`'a yeni opsiyonel prop: `holidayCalendarId?: string | null`. Verildiğinde `listHolidayCalendarEntries`
(`structure.service.ts`, zaten mevcut) ile o takvimin girdilerini çekiyor (`useQuery`, `enabled: !!holidayCalendarId`,
`staleTime: 1 saat`, `retry: false` — dekoratif bir özellik, izin yetkisi olmayan kullanıcılarda sessizce hata
yutuluyor, toast/throw yok). `getDayProps` ile eşleşen günlere `date-picker.module.css`'teki `.holiday` class'ı
(farklı arka plan + küçük nokta, Mantine CSS değişkenleriyle light/dark otomatik) ve native `title` (tatil adı,
tooltip) ekleniyor. Tekrarlayan tatiller (`isRecurringAnnually`) "MM-dd" anahtarıyla, tek seferlikler tam tarihle
eşleşiyor.

**Gerçek çağrı noktası:** `holiday-calendars.tsx`'teki "Tatil Ekle" diyaloğunda (`AddEntryDialog`) artık
`holidayCalendarId={calendar.id}` geçiliyor — kullanıcı zaten yönettiği takvimin mevcut tatillerini yeni tarih
seçerken görüyor (tekrar girişi önlüyor). Diğer çağrı noktalarına (örn. izin formu) bağlamak, "çalışanın şirketine
hangi takvim atanmış" bilgisini bulacak ek altyapı gerektiriyor — kapsam dışı bırakıldı, prop opt-in olduğu için
mevcut 25 çağrı noktasının hiçbiri etkilenmiyor.

## Değişen Dosyalar

- `web/src/components/ui/date-picker.tsx` — format, exclusivity, holiday marking, outside-click-safe value.
- `web/src/components/ui/date-picker.module.css` (yeni) — `.holiday` stili.
- `web/src/components/ui/date-picker.test.tsx` (yeni) — 4 test.
- `web/src/components/leave/leave-request-dialog.tsx` — `Modal`'a `closeOnClickOutside={false}`.
- `web/src/pages/settings/holiday-calendars.tsx` — `AddEntryDialog`'a `holidayCalendarId` bağlandı.

## Testler

`web/src/components/ui/date-picker.test.tsx` (RTL + vitest, gerçek Mantine 9.6.1'e karşı Docker'da doğrulandı):
1. `displays the value in dd.MM.yyyy format, not mm/dd/yyyy` — Bug 1.
2. `keeps the selected value and never calls onChange when the calendar is dismissed via an outside click` — Bug 3'ün
   component-seviyesi garantisi (takvimi dışarı tıklayarak kapatmak değeri bozmuyor/onChange tetiklemiyor).
3. `closes the first calendar when a second date picker is opened right after (no overlapping popovers)` — Bug 2,
   iki farklı `holidayCalendarId` ile her takvimin kendi tatilini gösterip göstermediği üzerinden (ikisi aynı anda
   DOM'da kalırsa ikisi de görünür kalırdı).
4. `marks a public holiday from the holiday calendar with a distinct, labeled day cell` — Bug 4.

**Doğrulama (host'ta node/pnpm yok, `docker run node:24-alpine` + named volume'lar üzerinden):**
- `pnpm exec tsc --noEmit` → temiz.
- `pnpm exec eslint src` → 0 hata, 1 ilgisiz pre-existing uyarı (`employee-list.tsx`, bu kartın dokunmadığı dosya).
- `pnpm exec vitest run` → 384/387 yeşil. 3 ilgisiz pre-existing hata, hiçbiri bu kartın dokunduğu dosyalarda değil:
  `i18n.locales.test.ts` (`de/attendance.json` çevrilmemiş placeholder), `http-interceptors.test.ts` (hook timeout,
  muhtemelen Docker/Alpine üzerinde yavaş ortam kaynaklı), `leave-type-dialog.test.tsx` bir testi (5000ms test
  timeout — izole çalıştırıldığında yeşil geçti, paralel/yüklü koşuda flaky).
- `pnpm exec vite build` → başarılı (65s).

## Backlog Önerisi

- `closeOnClickOutside={false}` desenini aynı "iki tarih + Modal" şekline sahip diğer dosyalara (yukarıda listelendi)
  da uygulayan takip kartı — modül sahiplerinin (PC-A: Employee/Documents; PC-B: Leave/Workflow) lane'lerine göre
  bölünmeli.
- `holidayCalendarId` bağlantısını izin formuna (`LeaveRequestDialog`) da taşımak için: çalışanın şirketine atanmış
  tatil takvimini döndüren bir endpoint/alan gerekiyor (şu an sadece `Company.holidayCalendarId` var, employee
  seviyesinde yok) — ayrı bir backend + frontend kartı.

---
*Kaynak: `docs/agent-board/cards/senseik-041-date-picker-issues.json`.*
