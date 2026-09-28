# Sense HR mobil — yeniden tasarım (spec)

- Tarih: 2026-09-27 · Dal: `a/mobil-yeniden-tasarim` (taban `dev`) · Uygulama: `apps/senseik-mobile`
- Referans: ZenHR ESS mobil uygulaması (YouTube `V3_UwpFqimk` + kullanıcının paylaştığı mağaza görselleri)
- Onaylı görsel örnek: [`assets/2026-09-27-mobil-ana-sayfa-mockup.html`](assets/2026-09-27-mobil-ana-sayfa-mockup.html)
- Kullanıcı kararları (sohbette): kapsam tüm ekranlar · koyu tema yok (tek tema) · alt bar 3 öğe · mola eklenecek ·
  harita olacak · marka adı **Sense HR** · renkler giriş ekranının paleti. Kalan kararlar kullanıcı tarafından
  ajana devredildi ("kararları benim yerime sen ver"); bu belgedeki "Karar:" satırları o yetkiyle alındı.

## 1. Amaç ve başarı ölçütü

Mobil uygulamanın tüm ekranlarını ZenHR'ın düzen diline (cam kartlar, ikonlu işlem ızgarası, FAB'lı alt bar,
alt sayfa formlar) ve Sense HR giriş ekranının renk dünyasına taşımak; işlev kaybı olmadan.

Başarı: (1) emülatörde her ekran yeni görünümde açılır ve mevcut akışlar (izin/mesai/masraf/avans talebi, onay/red,
bordro indirme, giriş/çıkış, bildirimler) çalışır; (2) `tsc`, `eslint`, `jest` yeşil, mevcut 419 test kaybolmaz;
(3) mola başı/sonu uçtan uca çalışır, backend test kapısı yeşil.

## 2. Görsel dil (token'lar)

Tek tema, koyu. Değerler `LoginScreen`/`AuthBackdrop` ile birebir aynı kaynaktan gelir.

| Token | Değer | Kullanım |
|---|---|---|
| `night` | `#0E0033` | Zemin gradyanının koyu ucu |
| `navy` | `#280087` | Zemin gradyanının açık ucu |
| `brand` | `#008CF0` | Tek vurgu: FAB, birincil buton, aktif sekme, bağlantı zemini, ilerleme |
| `brandText` | `#5CBBFF` | Koyu zeminde okunur bağlantı/vurgu metni |
| `ice` | `#D3ECFD` | İkon rengi; `textMuted` = ice %72, `textFaint` = ice %45 |
| `coral` | `#FF8A80` | Hata metni / tehlikeli eylem; rozet zemini `#FF5C6C` |
| `glass` | `rgba(255,255,255,.07)` + kenarlık `rgba(255,255,255,.14)` | Kart, kutucuk, giriş alanı |
| `sheet` | `#24107A → #170553` | Alt sayfa / menü / diyalog yüzeyi (opak) |
| `tabBar` | `#1A0766` | Alt bar (opak) |

- Zemin: `experimental_backgroundImage` ile giriş ekranındaki üç katmanlı gradyan (sağ-üst mavi ışıma, sol-alt mavi ışıma,
  160° lacivert). Arkada 1–2 soluk "hayalet kare" (marka işareti izi), dokunulamaz.
- Durum etiketleri (koyu zemin): onaylandı `#4FD9A2`/`rgba(46,204,142,.18)`, bekliyor `#FFCB5C`/`rgba(255,196,77,.18)`,
  reddedildi `#FF9A8F`/`rgba(255,138,128,.18)`, nötr ice %12.
- İşlem ikon renkleri (glif / zemin): mockup'taki 9 çift (mavi, yeşil, mercan, mor, amber, turkuaz, indigo, pembe, gri).
- Tipografi: **Manrope** (`@expo-google-fonts/manrope`, `expo-font` ile açılışta yüklenir; yüklenene kadar splash).
  Android'de özel fontta `fontWeight` güvenilmez → token'lar ağırlık başına `fontFamily` verir (400/500/600/700/800).
  Başlık 800 −0.5 tracking; bölüm başlığı 12px 800 BÜYÜK HARF 0.14em; sayılar `fontVariant: ['tabular-nums']`.
  Bakiye kesri: büyük sayı 800 beyaz + "/ 44,00 gün" 400 `textMuted`.
- Biçim: kart yarıçapı 20, kutucuk 22, buton 14, etiket tam yuvarlak. Aralık ölçüsü mevcut `spacing`.
- İmza: FAB = marka işareti (arkada beyaz, önde mavi iki yuvarlatılmış kare); basınca "+" 45° döner, talep alt sayfası
  yükselir. `useReducedMotion` açıksa animasyon yok.
- Durum çubuğu her yerde açık (`light`).

## 3. Navigasyon kabuğu

- Alt bar (özel `tabBar`): **Ana sayfa** (ızgara ikonu) · **FAB** · **Giriş / Çıkış** (kronometre). Bar opak, üst
  köşeleri 24 yuvarlak, FAB'ın etrafında oyuk (SVG path, `react-native-svg`). Etiketli, aktif sekme mavi ikon.
- FAB bir rota değildir: **Yeni talep** alt sayfasını açar → İzin · Fazla mesai · Masraf · Avans. Seçim `NewRequest`
  yığın ekranına `{ kind }` ile gider.
- Karar: Puantaj, İzinlerim, Bordro, Onaylarım, Profil sekme olmaktan çıkar, `AppStack` ekranı olur (Ana sayfadaki
  İşlemlerim ızgarasından ve avatardan açılır). Mevcut yığın ekranları (Kişiler, Vardiya, Hedefler, Destek,
  Bildirimler, Bildirim tercihleri, Taleplerim) kalır.
- Karar: yığın ekranlarında navigator başlığı kapalı; her ekran ortak `ScreenHeader` kullanır: cam yuvarlak geri
  düğmesi + büyük başlık (800, 28px) + isteğe bağlı sağ eylem (ör. "Geçmiş"). Android geri tuşu çalışmaya devam eder.
- Bildirim/push derin bağlantıları (`notificationNavigation.ts`, `pushNavigation.ts`) yeni rota adlarına göre güncellenir;
  eski `MainTabs`→`Leave` gibi hedefler yığın ekranına yönlenir.
- Çıkış yap: Profil ekranına ve Ana sayfa ⋮ menüsüne taşınır (Ana sayfadaki kırmızı buton kalkar).

## 4. Ana sayfa

Mockup'taki sıra: başlık satırı (marka işareti + "Sense**HR**", izin takvimi, zil + okunmamış rozeti, ⋮ menü:
Profilim / Bildirim tercihleri / Destek / Çıkış yap) → avatar + "Hoş geldin, {ad}" + "Profili gör" → **Kalan bakiyem**
(yatay kaydırma, snap; her izin türü bir kart; kart İzinlerim'e gider) → **İşlemlerim** (3 sütun, sayfalı, sayfa
noktaları; Puantaj, İzinlerim, Bordro, Onaylarım[bekleyen rozeti], Taleplerim, Kişiler | Vardiyam, Hedeflerim, Destek)
→ **Bugün izinde olanlar** → **Yaklaşan doğum günleri** → **Yaklaşan etkinlikler** → **Duyurular**.

| Bölüm | Kaynak | Kural |
|---|---|---|
| Bakiye | `leaveService.getMyBalances()` (mevcut) | Tüm türler; yıllık izin ilk sırada |
| Onay rozeti | `approvalsService.getInbox()` uzunluğu | 0 ise rozet yok; hata sessiz |
| Okunmamış | `notificationsService.getUnreadCount()` (mevcut) | "9+" üst sınırı korunur |
| İzindekiler | `GET /api/v1/leave/calendar?from=bugün&to=bugün` | `status==approved`, kendisi hariç, en çok 5; `isMasked` ise tür adı "İzinli". Boş/403 → bölüm gizli |
| Doğum günü | `GET /api/v1/employees/upcoming-birthdays?days=30` | İlk 3; "N gün kaldı"/"Bugün". Boş/403 → gizli |
| Etkinlik | `GET /api/v1/announcements/events/calendar?from=bugün&to=+30g` | Yalnız `companyEvent`/`meeting`; ilk 3. Boş → gizli |
| Duyuru | `GET /api/v1/announcements/feed` | `kind` "announcement" (büyük/küçük harf duyarsız), ilk 2; okunmamışsa "YENİ" etiketi. Boş → gizli |

- Bölümler birbirinden bağımsız yüklenir (`Promise.allSettled`); biri düşerse diğerleri görünür. Çekerek yenileme hepsini
  tazeler. Bakiye/puantaj hatası mevcut hata bandı ile gösterilir.
- Avatar: `Avatar` bileşeni `/api/v1/employees/{id}/photo` (kendisi için `/employees/me/photo`) görselini
  `Authorization` başlığıyla dener, 404/hata → baş harfler (renk, adın hash'inden sabit palet).
- Yeni servis dosyaları: `HomeFeedService.ts` (izindekiler, doğum günü, etkinlik, duyuru) — saf eşleme/filtreleme
  fonksiyonları ayrı dışa aktarılır ve birim testlidir.

## 5. Ekranlar (yeniden kurulum)

Ortak bileşenlerle (bkz. §7) aşağıdaki düzenlere taşınır; iş mantığı ve servis çağrıları korunur.

- **Puantaj:** gün kartları — solda büyük gün numarası + "Eki 2026", ayraç, "Çalışma süresi X sa", ›; altında durum
  etiketi (Tamamlandı / Eksik / Anomali) ve giriş-çıkış saatleri. Üstte ay seçici + "Filtre" hapı. Karar: "Ekibim"
  sekmesi yalnız mobilde zaten kullanılan bir ekip puantajı ucu varsa eklenir; yoksa eklenmez (yeni backend yok).
- **İzinlerim:** üstte bakiye kartları (Ana sayfadakiyle aynı bileşen), altta talep listesi (`LeaveRequestListItem`
  yeni kart görünümü, durum etiketi). "Yeni izin talebi" birincil buton → `NewRequest{kind:'leave'}`.
- **Yeni talep (`NewRequest`):** ZenHR "Request Time Off" düzeni — üstte marka mavisi başlık bandı (başlık + ×),
  altında yuvarlak köşeli koyu gövde; mevcut form bileşenleri (`LeaveRequestForm`, `OvertimeRequestSection`,
  `ExpenseClaimSection`, `AdvanceRequestSection`) bu kabuğa taşınır. Alanlar cam giriş kutusu, etiket üstte (*zorunlu).
  Tarih: **Tarih seç** alt sayfası — "Tek gün / Birden çok gün" segment, "Başlangıç/Bitiş" kutuları, ay takvimi,
  aralık vurgusu, "Uygula (N gün)". Karar: tarih aralığı takvimi JS ile çizilir (yeni bağımlılık yok); saat alanları
  mevcut `TimeField` (native picker) ile kalır.
- **Taleplerim:** tür filtresi (hap) + talep kartları (tür ikonu, tarih, tutar/süre, durum etiketi).
- **Bordro:** üstte vurgulu kart (marka mavisi zemin, beyaz metin): "Geçen ay net maaşın · Ağustos 2026 · ₺xx"; altında
  ay satırları (ay adı + yıl solda, net tutar sağda, okundu/onay durumu). Belge indirme/paylaşma ve "okudum" onayı korunur.
- **Onaylarım:** "Tür" açılır seçimi (Tümü / İzin / Fazla mesai / Masraf / Avans + sayılar), sağ üstte "Geçmiş" hapı
  (varsa mevcut geçmiş görünümü). Kart: avatar + ad + ünvan/departman, süre/tutar + tür, "Bekliyor" etiketi, ayraç,
  Başlangıç/Bitiş/Tutar satırları, **Reddet** (mercan) / **Onayla** (mavi). Red gerekçesi akışı (`approvalsRejectFlow`)
  korunur, alt sayfaya taşınır.
- **Profil:** avatar + ad + ünvan başlığı, yatay sekmeler (Kişisel / İletişim / İş / Belgeler — mevcut veride ne varsa),
  bilgi kartları (etiket küçük muted, değer 600). Altta Bildirim tercihleri, Destek, **Çıkış yap**.
- **Bildirimler, Bildirim tercihleri, Kişiler, Vardiyam, Hedeflerim, Destek:** `ScreenHeader` + cam kart listeleri,
  mevcut işlev aynen.
- **Giriş / Çıkış:** bkz. §6.
- **Giriş ekranı ve yardımcı auth ekranları:** yalnız marka adı "Sense**HR**" ve Manrope'a hizalama; düzen aynı.
- Uygulama adı (`app.json` `name`): "Sense HR". Paket kimliği (`com.algosense.senseik`) değişmez.

## 6. Giriş / Çıkış ve mola

### Backend (Attendance modülü)
- `PunchDirection` += `BreakStart`, `BreakEnd` (JSON `"breakStart"`/`"breakEnd"`). Mevcut `POST self-checkin/gps` ve
  `/qr` bu yönleri kabul eder.
- Kurallar: `BreakStart` yalnız bugün giriş yapılmış, çıkış yapılmamış ve açık mola yokken; `BreakEnd` yalnız açık mola
  varken. Aksi halde doğrulama hatası (yeni, yerelleştirilmiş hata kodları: `SelfCheckInBreakNotCheckedIn`,
  `SelfCheckInBreakAlreadyOpen`, `SelfCheckInBreakNotOpen`). Açık molayla `Out` gelirse mola o anda kapanır.
- Mola süresi `BreakEnd` anında `AttendanceDay.ActualBreakMinutes`'e eklenir ve günün net/uygulanan mola dakikaları
  mevcut hesaplayıcıyla (`DailyWorkCalculator`) yeniden hesaplanır.
- Açık mola durumu `SelfCheckIn` satırlarından türetilir (bugünün son `BreakStart`'ı ve sonrasında `BreakEnd` yok).
  Yeni kolon gerekmiyorsa migration yok; gerekirse modülün kendi EF migration'ı üretilir.
- `MyCheckInStatusDto` += `OnBreak` (bool), `BreakStartedAt` (TimeOnly?), `BreakMinutesToday` (int).
- Testler: domain/handler birim testleri (geçerli/geçersiz sıralar, `Out` açık molayı kapatır, dakika toplamı) +
  mevcut Attendance entegrasyon testlerine yeni yönler.

### Mobil ekran
- Üstte harita (≈%35 yükseklik): `react-native-maps`, kullanıcının konumu + konum dairesi. Android Google Maps
  anahtarı `GOOGLE_MAPS_ANDROID_API_KEY` ortam değişkeninden `app.config.js` ile native yapılandırmaya ve
  `extra.hasMapsKey`'e girer. **Anahtar yoksa harita render edilmez** (SDK anahtarsız çöker); yerine konum kartı
  (konum adı, koordinat doğruluğu) gösterilir. Anahtar git'e girmez.
- Altında: "Girişten beri 2 sa 12 dk" satırı; saat kartı (canlı saat, tarih, konum doğruluğu rozeti "Doğru"/"Zayıf"
  — `expo-location` `accuracy` metresine göre, ≤50 m doğru); 2×2 kutucuk: **Giriş · Çıkış · Mola başı · Mola sonu**
  (saat veya "--:--"; sıradaki eylem mavi kenarlı vurgulu, uygun olmayanlar soluk); tek büyük birincil buton sıradaki
  eylemi yapar (GPS ile `POST gps`). QR ile giriş ikincil eylem olarak kalır (mevcut kamera akışı).
- `AttendanceService` += `gpsCheckIn({latitude, longitude, locationId?, direction})`; durum tipine mola alanları.

## 7. Ortak bileşenler (`src/components/ui/`)

`theme.ts` (token'lar + `fonts`), `ScreenBackground`, `Screen` (arka plan + güvenli alan + kaydırma + yenileme),
`ScreenHeader`, `GlassCard`, `SectionHeader`, `StatusPill`, `ActionTile`, `Avatar`, `IconCircleButton`,
`PrimaryButton` (+ `danger`, `secondary`), `TextField`/`SelectField` (cam), `SegmentedControl`, `BottomSheet`
(Modal tabanlı, arka plan karartma, kaydırarak/geri tuşuyla kapanır), `EmptyState`, `ErrorBanner`,
`BrandMark` (mevcut — küçük boy için `animated=false`).

- Mevcut `styles/colors.ts` anahtarları yeni koyu değerlere eşlenir (derleme kırılmasın), yeni kod `theme.ts` kullanır.
- Her bileşen `accessibilityRole`/`accessibilityLabel` taşır; dokunma alanı ≥44.

## 8. Test ve doğrulama

- TDD: saf fonksiyonlar (feed eşlemeleri, bakiye sıralama, rozet, mola durum türetimi, tarih aralığı gün sayısı,
  sıradaki giriş/çıkış eylemi) önce test. Mevcut ekran testleri yeni rota/bileşenlere uyarlanır; test silinmez.
- Kapılar: mobil `pnpm exec tsc --noEmit`, `pnpm exec eslint .`, `pnpm exec jest` (apps/senseik-mobile);
  backend için takım test kapısı `node .claude/takim/betikler/test-kapisi.mjs`.
- Emülatörde (Pixel_7_Pro, dev build — yeni native bağımlılıklar nedeniyle `expo run:android`) her ekranın ekran
  görüntüsü alınır; kontrol listesi: yatay taşma yok, metin kontrastı, alt barın içeriği kapatmaması (alt boşluk).

## 9. Kapsam dışı

Açık tema / tema seçici · yeni "Ekibim" puantaj backend'i · otomatik giriş (auto-clock) · işlem ızgarasının
kullanıcı tarafından yeniden sıralanması ("Düzenle") · iOS doğrulaması (yalnız Android emülatörde doğrulanır).
