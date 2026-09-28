# Sense HR mobil yeniden tasarım — Uygulama planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mobil uygulamanın tüm ekranlarını ZenHR düzenine ve Sense HR giriş ekranı paletine taşımak; mola başı/sonu ve haritalı Giriş/Çıkış eklemek.

**Architecture:** Önce ortak katman (token'lar, `components/ui`, navigasyon kabuğu, Ana sayfa) tek geliştiriciyle kurulur; backend molası buna paralel yürür. Sonra ekran grupları dosya sahipliği ayrık paralel parçalarda yeni bileşenlere taşınır; Giriş/Çıkış ekranı backend bitince yazılır. İş mantığı ve servis çağrıları korunur, yalnız sunum katmanı değişir.

**Tech Stack:** Expo SDK 57 dev build, React Native 0.86 (New Arch), react-navigation 7, reanimated 4, `react-native-svg`, `react-native-maps`, `expo-font` + `@expo-google-fonts/manrope`; .NET 10 Attendance modülü (EF Core, xUnit v3).

**Spec:** `docs/superpowers/specs/2026-09-27-mobil-yeniden-tasarim-design.md` (görsel referans: `docs/superpowers/specs/assets/2026-09-27-mobil-ana-sayfa-mockup.html` — renkler/aralıklar/sıra buradan okunur).

## Global Constraints

- Tek koyu tema; renkler spec §2 tablosundan birebir (`#0E0033`, `#280087`, `#008CF0`, `#5CBBFF`, `#D3ECFD`, `#FF8A80`, rozet `#FF5C6C`, cam `rgba(255,255,255,.07)`/`.14`, sheet `#24107A→#170553`, tabBar `#1A0766`).
- Font Manrope; ağırlık `fontFamily` ile verilir (`Manrope_400Regular` … `Manrope_800ExtraBold`), `fontWeight` kullanılmaz.
- Marka adı UI'da **Sense HR** (wordmark "Sense" beyaz + "HR" `#008CF0`); paket kimliği `com.algosense.senseik` değişmez.
- Yeni native bağımlılıklar yalnız: `react-native-svg`, `react-native-maps`, `expo-font`, `@expo-google-fonts/manrope` — `pnpm exec expo install` ile (SDK 57 uyumlu sürüm). Başka paket eklenmez.
- Google Maps anahtarı git'e girmez; `GOOGLE_MAPS_ANDROID_API_KEY` ortam değişkeni, yoksa harita render edilmez.
- API JSON camelCase, eksik alan = null; `announcements/feed` `kind/type/priority` PascalCase string gelir.
- Test silinmez/atlanmaz; mevcut 419 jest testi + yeni testler yeşil. Mobil kapı: `cd apps/senseik-mobile && pnpm exec tsc --noEmit && pnpm exec eslint . && pnpm exec jest`.
- Kullanıcıya dönük metin Türkçe, eylem adıyla ("Talebi gönder", "Molaya başla").
- Dokunma alanı ≥44, her etkileşimli öğede `accessibilityRole` + `accessibilityLabel`; `useReducedMotion` açıkken animasyon yok.
- Commit'i ana oturum yapar (geliştirici ajan commit atmaz); yalnız kendi dosyaların.

## Review Focus

1. **Bölüm ucu 403/boş döner** (çalışan `leave/calendar`'da yalnız kendini görür; tenant doğum günü ayarını kapatmış olabilir) → Ana sayfa çökmez, bölüm gizlenir, diğerleri görünür. Test: Task 4 `buildHomeSections` "rejected settled → section hidden".
2. **Uzun Türkçe adlar / büyük sistem yazı boyutu** → kart ve kutucuk metni taşmaz, `numberOfLines` ile kısalır. Test: Task 2 `initialsOf` ("İlkay Şahin-Öztürk" → "İŞ", tek kelime, boş).
3. **Alt bar içeriği kapatır** → her kaydırılabilir ekranın alt boşluğu `TAB_BAR_HEIGHT + safeArea.bottom + 16`. Test: Task 3 `contentBottomInset(bottomInset)`.
4. **Mola sırası bozuk** (girişsiz mola başı, çift mola başı, açık molayla çıkış) → sunucu doğrulama hatası, mobil sıradaki eylemi doğru gösterir. Test: Task 5 domain testleri + Task 9 `nextPunchAction`.
5. **Haritasız ortam** (anahtar yok) → Giriş/Çıkış ekranı açılır, konum kartı görünür, çökme yok. Test: Task 9 `shouldRenderMap(extra)`.

---

## Dosya haritası

```
apps/senseik-mobile/
  app.config.js                         (yeni, Task 1)  app.json'u sarar; maps anahtarı + extra.hasMapsKey
  app.json                              (Task 1)        name → "Sense HR"
  package.json                          (Task 1)        4 bağımlılık
  src/App.tsx                           (Task 1)        font yükleme
  src/theme/theme.ts                    (yeni, Task 1)  token'lar + fonts + text presetleri
  src/styles/colors.ts, typography.ts   (Task 1)        koyu değerlere eşleme (geriye uyum)
  src/__tests__/setup.ts                (Task 1)        expo-font, svg, maps mock
  src/components/ui/*.tsx               (yeni, Task 2)  UI kit
  src/types/navigation.ts               (Task 3)
  src/navigation/RootNavigator.tsx      (Task 3)
  src/navigation/NotchedTabBar.tsx      (yeni, Task 3)
  src/navigation/RequestSheet.tsx       (yeni, Task 3)
  src/navigation/layout.ts              (yeni, Task 3)  TAB_BAR_HEIGHT, contentBottomInset, buildNotchPath
  src/screens/app/NewRequestScreen.tsx  (yeni, Task 3)
  src/lib/notificationNavigation.ts, pushNavigation.ts (Task 3)
  src/services/HomeFeedService.ts       (yeni, Task 4)
  src/screens/app/DashboardScreen.tsx   (Task 4)        → Ana sayfa (rota adı Home)
  src/screens/app/{Payroll,Profile,Notifications,NotificationPreferences,People,Goals,Support}Screen.tsx (Task 6)
  src/screens/app/{Attendance,Leave,Requests,Shift}Screen.tsx + components/leave/*, components/requests/*, components/form/* (Task 7)
  src/screens/app/ApprovalsScreen.tsx, screens/auth/*, screens/SplashScreen.tsx, components/auth/* (Task 8)
  src/screens/app/CheckInScreen.tsx, src/services/AttendanceService.ts, src/lib/punch.ts (Task 9)
src/Modules/Attendance/**               (Task 5)
src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource{,.en}.resx (Task 5, sona ekleme)
```

Dalga sırası: **Dalga 1** = Task 1→2→3→4 (tek node geliştirici, sıralı) ∥ Task 5 (dotnet). **Dalga 2** = Task 6 ∥ Task 7 ∥ Task 8 ∥ Task 9. **Dalga 3** = Task 10.

---

### Task 1: Bağımlılıklar, yapılandırma, font ve token'lar

**Files:**
- Modify: `apps/senseik-mobile/package.json`, `app.json`, `src/App.tsx`, `src/styles/colors.ts`, `src/styles/typography.ts`, `src/__tests__/setup.ts`, `src/components/auth/AuthBackdrop.tsx` (gradyan sabitini theme'den alsın)
- Create: `apps/senseik-mobile/app.config.js`, `src/theme/theme.ts`
- Test: `src/__tests__/theme/theme.test.ts`

**Interfaces — Produces:**
```ts
// src/theme/theme.ts
export const palette: {
  night: "#0E0033"; navy: "#280087"; brand: "#008CF0"; brandText: "#5CBBFF"; ice: "#D3ECFD";
  coral: "#FF8A80"; badge: "#FF5C6C"; white: "#FFFFFF";
  glass: "rgba(255,255,255,0.07)"; glassBorder: "rgba(255,255,255,0.14)"; glassInner: "rgba(255,255,255,0.06)";
  line: "rgba(255,255,255,0.10)"; textMuted: "rgba(211,236,253,0.72)"; textFaint: "rgba(211,236,253,0.45)";
  sheetTop: "#24107A"; sheetBottom: "#170553"; tabBar: "#1A0766"; scrim: "rgba(5,0,20,0.55)";
};
export const backdropImage: string; // AuthBackdrop'taki üç katmanlı gradyan string'i (birebir)
export type Tone = "success" | "warning" | "danger" | "neutral" | "info";
export const tones: Record<Tone, { fg: string; bg: string }>;
// success #4FD9A2/rgba(46,204,142,0.18) · warning #FFCB5C/rgba(255,196,77,0.18) · danger #FF9A8F/rgba(255,138,128,0.18)
// neutral #C6D6EC/rgba(211,236,253,0.12) · info #5CBBFF/rgba(0,140,240,0.22)
export type AccentKey = "blue"|"green"|"coral"|"purple"|"amber"|"teal"|"indigo"|"pink"|"gray";
export const accents: Record<AccentKey, { fg: string; bg: string }>;
// blue #5CBBFF/rgba(0,140,240,0.22) · green #4FD9A2/rgba(46,204,142,0.18) · coral #FF9A8F/rgba(255,138,128,0.18)
// purple #B19BFF/rgba(150,120,255,0.22) · amber #FFCB5C/rgba(255,196,77,0.18) · teal #4DDFD1/rgba(56,214,200,0.18)
// indigo #9AA8FF/rgba(120,140,255,0.22) · pink #FF94C2/rgba(255,120,180,0.18) · gray #C6D6EC/rgba(211,236,253,0.12)
export type FontWeightKey = 400 | 500 | 600 | 700 | 800;
export const fonts: Record<FontWeightKey, string>; // "Manrope_400Regular" "Manrope_500Medium" "Manrope_600SemiBold" "Manrope_700Bold" "Manrope_800ExtraBold"
export function fontFor(weight: FontWeightKey): { fontFamily: string };
export const radius: { card: 20; tile: 22; button: 14; pill: 999; sheet: 28 };
export const text: {
  display: TextStyle; // 28/34, 800, -0.5, white
  title: TextStyle;   // 22/28, 800, -0.4, white
  heading: TextStyle; // 16/22, 800, white
  section: TextStyle; // 12/16, 800, uppercase, letterSpacing 1.7, textMuted
  body: TextStyle;    // 15/22, 500, white
  bodyMuted: TextStyle; // 14/20, 600, textMuted
  caption: TextStyle; // 12/16, 600, textMuted
  number: TextStyle;  // 24/30, 800, tabular-nums, white
  button: TextStyle;  // 16/22, 800, white
};
```

- [ ] **Step 1: Bağımlılıkları kur** — `cd apps/senseik-mobile && pnpm exec expo install react-native-svg react-native-maps expo-font @expo-google-fonts/manrope`. Sonra `package.json`'da `"typescript"` takma adının `npm:@typescript/typescript6@^6.0.2` kaldığını doğrula (expo install bozarsa geri yaz). `tsconfig.json` değişmişse geri al (`EXPO_NO_TYPESCRIPT_SETUP=1`).
- [ ] **Step 2: Failing test yaz** — `src/__tests__/theme/theme.test.ts`:
```ts
import { fontFor, fonts, palette, tones, accents } from "@/theme/theme";

describe("theme", () => {
  it("maps each weight to its Manrope family", () => {
    expect(fontFor(400)).toEqual({ fontFamily: "Manrope_400Regular" });
    expect(fontFor(800)).toEqual({ fontFamily: "Manrope_800ExtraBold" });
    expect(Object.keys(fonts)).toHaveLength(5);
  });
  it("keeps the login screen palette values", () => {
    expect(palette.night).toBe("#0E0033");
    expect(palette.navy).toBe("#280087");
    expect(palette.brand).toBe("#008CF0");
  });
  it("defines fg/bg for every tone and accent", () => {
    for (const t of Object.values(tones)) expect(t.fg && t.bg).toBeTruthy();
    expect(Object.keys(accents)).toHaveLength(9);
  });
});
```
- [ ] **Step 3: Testin düştüğünü gör** — `pnpm exec jest src/__tests__/theme` → FAIL (modül yok).
- [ ] **Step 4: `src/theme/theme.ts`'i yaz** (arayüz yukarıda; `backdropImage` = `AuthBackdrop.tsx`'teki `experimental_backgroundImage` string'inin aynısı; `AuthBackdrop` bu sabiti import eder).
- [ ] **Step 5: Geriye uyum** — `styles/colors.ts`: `background`→`palette.night`, `text`→`#FFFFFF`, `textSecondary`→textMuted, `textTertiary`→textFaint, `border`→glassBorder, `primary`→brand, `error`→coral; diğer anahtarlar kalır. `typography.ts` presetlerine `...fontFor(w)` eklenir, `fontWeight` kaldırılır.
- [ ] **Step 6: Font yükleme** — `App.tsx`: `useFonts({ Manrope_400Regular, Manrope_500Medium, Manrope_600SemiBold, Manrope_700Bold, Manrope_800ExtraBold })`; yüklenene kadar mevcut splash tutulur (varsa `SplashScreen.preventAutoHideAsync` akışına bağlanır), font hatası olursa sistem fontuyla devam edilir (uygulama bloke olmaz).
- [ ] **Step 7: `app.config.js`**:
```js
const base = require("./app.json");
module.exports = () => {
  const mapsKey = process.env.GOOGLE_MAPS_ANDROID_API_KEY;
  const expo = { ...base.expo };
  expo.android = { ...expo.android, ...(mapsKey ? { config: { googleMaps: { apiKey: mapsKey } } } : {}) };
  expo.extra = { ...(expo.extra ?? {}), hasMapsKey: Boolean(mapsKey) };
  return { expo };
};
```
  `app.json` `name` → `"Sense HR"`; `android.adaptiveIcon.backgroundColor` ve splash `backgroundColor` → `"#0E0033"`; `userInterfaceStyle` → `"dark"`.
- [ ] **Step 8: Jest mock'ları** — `setup.ts`: `expo-font` (`useFonts` → `[true, null]`), `@expo-google-fonts/manrope` (ad sabitleri), `react-native-svg` (View tabanlı stub: `Svg`, `Path`), `react-native-maps` (View stub + `Marker`, `Circle`).
- [ ] **Step 9: Kapıyı koş** — mobil kapı (Global Constraints). Beklenen: hepsi yeşil, test sayısı ≥ 422.

### Task 2: UI kit (`src/components/ui`)

**Files:** Create `src/components/ui/{Screen,ScreenBackground,ScreenHeader,GlassCard,SectionHeader,StatusPill,ActionTile,Avatar,IconCircleButton,PrimaryButton,TextField,SelectField,SegmentedControl,BottomSheet,EmptyState,ErrorBanner}.tsx`, `src/components/ui/index.ts`, `src/components/ui/avatarUtils.ts` · Test: `src/__tests__/components/ui/avatarUtils.test.ts`, `src/__tests__/components/ui/StatusPill.test.tsx`

**Interfaces — Consumes:** Task 1 `palette, tones, accents, text, radius, fontFor, backdropImage`; Task 3 `contentBottomInset` (Screen varsayılan alt boşluğu — Task 2 önce `layout.ts`'i yalnız bu fonksiyon ve `TAB_BAR_HEIGHT` ile oluşturur, Task 3 genişletir).
**Produces:**
```ts
type IoniconName = React.ComponentProps<typeof Ionicons>["name"];
export function initialsOf(fullName: string): string;            // "Ali Şahin"→"AŞ", "İlkay Şahin-Öztürk"→"İŞ", "Ece"→"E", ""→"?"
export function accentForName(fullName: string): AccentKey;      // deterministik hash → 9 accent'ten biri
export function Screen(p: { children: ReactNode; scroll?: boolean /*true*/; refreshing?: boolean; onRefresh?: () => void;
  header?: ReactNode; withTabBar?: boolean /*false; true ise alt boşluk contentBottomInset*/; testID?: string }): JSX.Element;
export function ScreenBackground(p: { children?: ReactNode }): JSX.Element;   // gradyan + 2 hayalet kare, absolute fill
export function ScreenHeader(p: { title: string; onBack?: () => void /*default navigation.goBack*/; right?: ReactNode; subtitle?: string }): JSX.Element;
export function GlassCard(p: { children: ReactNode; onPress?: () => void; style?: StyleProp<ViewStyle>; accessibilityLabel?: string; padding?: number }): JSX.Element;
export function SectionHeader(p: { title: string; actionLabel?: string; onAction?: () => void }): JSX.Element;
export function StatusPill(p: { label: string; tone: Tone }): JSX.Element;
export function ActionTile(p: { label: string; icon: IoniconName; accent: AccentKey; badge?: number; onPress: () => void }): JSX.Element;
export function Avatar(p: { name: string; employeeId?: string; self?: boolean; size?: number /*58*/; radius?: number /*16*/ }): JSX.Element;
export function IconCircleButton(p: { icon: IoniconName; onPress: () => void; accessibilityLabel: string; badge?: string }): JSX.Element;
export function PrimaryButton(p: { label: string; onPress: () => void; variant?: "primary"|"danger"|"secondary"; loading?: boolean; disabled?: boolean; icon?: IoniconName }): JSX.Element;
export function TextField(p: TextInputProps & { label: string; required?: boolean; error?: string }): JSX.Element;
export function SelectField(p: { label: string; value?: string; placeholder: string; onPress: () => void; required?: boolean; error?: string }): JSX.Element;
export function SegmentedControl<T extends string>(p: { options: { value: T; label: string }[]; value: T; onChange: (v: T) => void }): JSX.Element;
export function BottomSheet(p: { visible: boolean; onClose: () => void; title?: string; children: ReactNode }): JSX.Element; // Modal, scrim, grab, geri tuşu kapatır
export function EmptyState(p: { icon: IoniconName; title: string; message?: string; actionLabel?: string; onAction?: () => void }): JSX.Element;
export function ErrorBanner(p: { message: string; onRetry?: () => void }): JSX.Element;
```
- `Avatar`: `apiClient`'ın mevcut base URL ve erişim token'ıyla `source={{ uri: base + (self ? "/api/v1/employees/me/photo" : `/api/v1/employees/${employeeId}/photo`), headers: { Authorization: `Bearer ${token}` } }}`; `onError` → baş harf kutusu (`accents[accentForName(name)]`). Token/baseUrl'in `apiClient.ts`/`storage.ts`'te nasıl tutulduğuna bak; gerekirse `apiClient.ts`'e küçük bir dışa aktarılan getter ekle. employeeId ve self yoksa doğrudan baş harf.
- `ScreenBackground`: `experimental_backgroundImage: backdropImage` + `palette.night` yedek zemin; hayalet kareler `AuthBackdrop`'taki kenarlık/zemin değerleriyle, `pointerEvents="none"`.
- `BottomSheet`: `Modal transparent animationType="slide"`, scrim `palette.scrim` (dokununca kapat), gövde üst köşe `radius.sheet`, `experimental_backgroundImage` ile `sheetTop→sheetBottom`, grab çubuğu, `onRequestClose` = geri tuşu.

- [ ] **Step 1: Failing test** — `avatarUtils.test.ts`:
```ts
import { initialsOf, accentForName } from "@/components/ui/avatarUtils";
it.each([["Ali Şahin","AŞ"],["İlkay Şahin-Öztürk","İŞ"],["Ece","E"],["  ","?"],["","?"]])("initialsOf(%s)", (n, e) => expect(initialsOf(n)).toBe(e));
it("accentForName is stable and within the 9 accents", () => {
  expect(accentForName("Selin Aydın")).toBe(accentForName("Selin Aydın"));
  expect(["blue","green","coral","purple","amber","teal","indigo","pink","gray"]).toContain(accentForName("Murat Kaya"));
});
```
  `StatusPill.test.tsx`: mevcut render test kalıbıyla (`LeaveRequestListItem.test.tsx`'e bak) `<StatusPill label="Bekliyor" tone="warning" />` → "Bekliyor" metni görünür.
- [ ] **Step 2:** `pnpm exec jest src/__tests__/components/ui` → FAIL.
- [ ] **Step 3:** `avatarUtils.ts` + bileşenleri yaz (`initialsOf`: trim, boşlukla böl, ilk ve son kelimenin ilk harfi, `toLocaleUpperCase("tr-TR")`; tire ile bağlı soyad tek kelime sayılır).
- [ ] **Step 4:** testler PASS; mobil kapı yeşil.

### Task 3: Navigasyon kabuğu, alt bar, talep alt sayfası

**Files:** Modify `src/types/navigation.ts`, `src/navigation/RootNavigator.tsx`, `src/lib/notificationNavigation.ts`, `src/lib/pushNavigation.ts` (+ testleri), push/bildirim hedefini kullanan yerler (`NotificationsScreen.tsx` ve push dinleyicisi — yalnız `navigate` çağrısı), taşınan ekranların navigation prop tipleri · Create `src/navigation/{NotchedTabBar.tsx,RequestSheet.tsx}`, genişlet `src/navigation/layout.ts`, `src/screens/app/NewRequestScreen.tsx` · Test: `src/__tests__/navigation/layout.test.ts`, mevcut `navigation`/`lib` testleri.

**Interfaces — Produces:**
```ts
// types/navigation.ts
export type MainTabsParamList = { Home: undefined; CheckIn: undefined };
export type RequestKind = "leave" | "overtime" | "expense" | "advance";
export type AppStackParamList = {
  MainTabs: NavigatorScreenParams<MainTabsParamList>;
  Attendance: undefined; Leave: undefined; Payroll: undefined; Approvals: undefined; Profile: undefined;
  Requests: undefined; People: undefined; Shift: undefined; Goals: undefined; Support: undefined;
  Notifications: undefined; NotificationPreferences: undefined;
  NewRequest: { kind: RequestKind };
};
// navigation/layout.ts
export const TAB_BAR_HEIGHT = 72;          // safe-area hariç
export const FAB_SIZE = 62;
export function contentBottomInset(safeBottom: number): number; // TAB_BAR_HEIGHT + safeBottom + 16
export function buildNotchPath(width: number, height: number): string; // mockup path'i (390 genişlik) genişliğe ölçeklenmiş, merkez = width/2
// lib/notificationNavigation.ts & pushNavigation.ts
export type NavigationTarget = { stack: Exclude<keyof AppStackParamList, "MainTabs" | "NewRequest"> } | { tab: keyof MainTabsParamList };
export function resolveNotificationNavigationTarget(templateKey: string): NavigationTarget | null;
// approval.task_assigned→{stack:"Approvals"}, payroll.payslip_ready→{stack:"Payroll"}, leave.added_on_behalf→{stack:"Leave"},
// employee.birthday & announcement.published→{tab:"Home"}; pushNavigation'daki eşdeğer fonksiyon aynı dönüş tipine geçer
export function navigateToTarget(navigation: NavigationProp<AppStackParamList>, target: NavigationTarget): void;
```
- Alt bar: `Tab.Navigator tabBar={(props) => <NotchedTabBar {...props} />}` ve `screenOptions={{ headerShown:false }}`; sol "Ana sayfa" (`grid` / `grid-outline`), sağ "Giriş / Çıkış" (`stopwatch` / `stopwatch-outline`); bar zemini `react-native-svg` `Path d={buildNotchPath(w, h)}` `fill=palette.tabBar` üst kenarlık `rgba(255,255,255,0.12)`; ortada FAB (arka beyaz kare sağ-üst, ön mavi kare sol-alt, 54×54 radius 17, `add` ikonu; açıkken reanimated `withSpring` ile 45° dönüş, reduced-motion'da anında). FAB `RequestSheet`'i açar; öğe seçimi sheet'i kapatır ve `navigation.navigate("NewRequest", { kind })`.
- `RequestSheet` (Task 2 `BottomSheet`, başlık "Yeni talep", alt başlık "Ne için talep oluşturmak istiyorsun?"): İzin (`sunny-outline`, green, "Yıllık, mazeret, raporlu…"), Fazla mesai (`time-outline`, blue, "Planlanan veya yapılan mesai"), Masraf (`receipt-outline`, amber, "Fiş veya fatura ile"), Avans (`wallet-outline`, coral, "Maaştan mahsup edilir").
- `NewRequestScreen`: üstte `palette.brand` başlık bandı (güvenli alan dahil; başlık "İzin talebi"/"Fazla mesai talebi"/"Masraf talebi"/"Avans talebi" `text.title` + sağda × `IconCircleButton` kapat), gövde koyu (`sheetTop→sheetBottom`), üst köşeler 28; `kind`'a göre mevcut `LeaveRequestForm` / `OvertimeRequestSection` / `ExpenseClaimSection` / `AdvanceRequestSection` render edilir (bu görevde yalnız kabuğa yerleşir; iç stil Task 7'de). Bu bileşenlerin şu an `LeaveScreen`/`RequestsScreen` içinden nasıl beslendiğine (props, başarı geri çağrısı) bak ve aynı props'u ver; başarıda `navigation.goBack()`.
- AppStack `headerShown:false`; tüm yığın ekranları kaydedilir. `withTopSafeArea` kaldırılır — Home ve CheckIn kendi `Screen` sarmalayıcılarını kullanır (Task 4/9); bu görevde bu iki ekran geçici olarak `SafeAreaView`'le sarılabilir.
- `resolveStatusBarStyle` her zaman `"light"` döner (test güncellenir); kök splash spinner rengi `palette.brand`, zemini `palette.night`.
- Taşınan ekranlardaki (Attendance, Leave, Payroll, Approvals, Profile, Dashboard) `navigation` tipleri `NativeStackNavigationProp<AppStackParamList>`'e çevrilir; `navigate("Leave")` vb. çağrıları yeni rotalarla derlenir (en az değişiklik; görünüm Task 4/6/7/8'de).

- [ ] **Step 1: Failing test** — `layout.test.ts`:
```ts
import { contentBottomInset, buildNotchPath, TAB_BAR_HEIGHT } from "@/navigation/layout";
it("adds tab bar, safe area and breathing room", () => expect(contentBottomInset(24)).toBe(TAB_BAR_HEIGHT + 24 + 16));
it("builds a closed path spanning the width with a centred notch", () => {
  const d = buildNotchPath(390, 92);
  expect(d.startsWith("M0 ")).toBe(true);
  expect(d.endsWith("Z")).toBe(true);
  expect(d).toContain("195");           // notch centre x
});
it("scales the notch centre with the width", () => expect(buildNotchPath(412, 92)).toContain("206"));
```
  Mevcut `notificationNavigation`/`pushNavigation` testlerini yeni `NavigationTarget` dönüşüne göre güncelle (önce testi değiştir, düştüğünü gör).
- [ ] **Step 2:** FAIL gör. **Step 3:** uygula. **Step 4:** mobil kapı yeşil.

### Task 4: Ana sayfa ve `HomeFeedService`

**Files:** Create `src/services/HomeFeedService.ts`, `src/screens/app/home/{BalanceCarousel,ActionGrid,PeopleOffCard,BirthdayCard,EventCard,AnnouncementCard,HomeMenu,homeSections}.tsx|ts` · Modify `src/screens/app/DashboardScreen.tsx` (rota adı `Home`, dosya adı korunur) · Test: `src/__tests__/services/HomeFeedService.test.ts`, `src/__tests__/screens/DashboardScreen.test.ts` (güncelle/genişlet).

**Interfaces — Produces:**
```ts
export interface LeaveCalendarEntry { requestId: string; employeeId: string; employeeName: string; startDate: string; endDate: string;
  calculatedDays: number; status: "pending" | "approved"; isMasked: boolean; leaveTypeCode?: string | null; leaveTypeName?: string | null; overlappingApprovedCount?: number }
export interface UpcomingBirthday { id: string; displayName: string; orgUnitId?: string | null; nextBirthday: string }
export interface CompanyCalendarEntry { id?: string | null; title: string; description?: string | null; date: string;
  type: "companyEvent" | "meeting" | "birthday" | "leaveStart"; employeeId?: string | null }
export interface AnnouncementFeedItem { id: string; title: string; content: string; type: string; priority: string;
  publishedAt?: string | null; expiresAt?: string | null; isRead: boolean; kind: string; senderEmployeeId?: string | null }
export interface PersonOff { employeeId: string; name: string; typeLabel: string; days: number; startDate: string; endDate: string; isRemote: boolean }
export function selectPeopleOffToday(entries: LeaveCalendarEntry[], selfEmployeeId: string | undefined, max?: number /*5*/): PersonOff[];
export function selectUpcomingBirthdays(items: UpcomingBirthday[], today: string, max?: number /*3*/): Array<UpcomingBirthday & { daysLeft: number }>;
export function selectUpcomingEvents(entries: CompanyCalendarEntry[], max?: number /*3*/): CompanyCalendarEntry[];
export function selectAnnouncements(items: AnnouncementFeedItem[], max?: number /*2*/): AnnouncementFeedItem[];
export function sortBalancesAnnualFirst<T extends { leaveTypeCode: string }>(items: T[]): T[];
export class HomeFeedService {
  getLeaveCalendar(from: string, to: string): Promise<LeaveCalendarEntry[]>;       // GET /api/v1/leave/calendar
  getUpcomingBirthdays(days?: number): Promise<UpcomingBirthday[]>;                 // GET /api/v1/employees/upcoming-birthdays
  getCompanyCalendar(from: string, to: string): Promise<CompanyCalendarEntry[]>;    // GET /api/v1/announcements/events/calendar
  getAnnouncementFeed(): Promise<AnnouncementFeedItem[]>;                           // GET /api/v1/announcements/feed
}
export const homeFeedService: HomeFeedService;
// screens/app/home/homeSections.ts
export function sectionFrom<T>(result: PromiseSettledResult<T[]>, select: (items: T[]) => unknown[]): unknown[] | null; // rejected/boş → null
```
- `selectPeopleOffToday`: yalnız `approved`, `selfEmployeeId` hariç; `isMasked` → typeLabel "İzinli"; `leaveTypeCode === "remoteWork"` → `isRemote: true`, typeLabel "Uzaktan çalışıyor"; aksi halde `leaveTypeName ?? "İzinli"`.
- `selectAnnouncements`: `kind.toLowerCase() === "announcement"`; `publishedAt` azalan.
- Kullanıcının `employeeId`'si `useAuthStore().user`'dan (alan adını `authStore.ts`'te kontrol et).
- `DashboardScreen` (spec §4 + mockup): `Screen withTabBar` + başlık satırı (`BrandMark size=30 animated=false` + wordmark 23px 800 "Sense"+"HR" brand, `IconCircleButton` takvim → `Leave`, zil → `Notifications` rozetli, ⋮ → `HomeMenu`); selamlama (`Avatar self size=60 radius=18`, "Hoş geldin, {firstName}" `text.title`, "Profili gör" `brandText` → `Profile`); `SectionHeader "Kalan bakiyem"` + `BalanceCarousel` (kart genişliği ekranın %78'i, `snapToInterval`, "40,30 / 44,00 gün" kesir tipografisi + 5px ilerleme çubuğu, dokununca `Leave`); `SectionHeader "İşlemlerim"` + `ActionGrid` (yatay `FlatList pagingEnabled`, sayfa başına 3×2, nokta göstergesi; Puantaj `time-outline` blue→Attendance, İzinlerim `sunny-outline` green→Leave, Bordro `wallet-outline` coral→Payroll, Onaylarım `checkmark-done-outline` purple badge→Approvals, Taleplerim `document-text-outline` amber→Requests, Kişiler `people-outline` teal→People | Vardiyam `calendar-outline` indigo→Shift, Hedeflerim `flag-outline` pink→Goals, Destek `headset-outline` gray→Support); ardından bölümler (null olan render edilmez). `Promise.allSettled`; çekerek yenileme. Puantaj özeti kartı Ana sayfadan kalkar (Puantaj ekranında var). Eski kırmızı "Çıkış yap" butonu kalkar (⋮ menüde).
- `HomeMenu`: sağ üstte açılan cam menü (Modal transparent, dışına dokununca kapanır): Profilim, Bildirim tercihleri, Destek, Çıkış yap (coral, mevcut `logout`).

- [ ] **Step 1: Failing test** — `HomeFeedService.test.ts`:
```ts
import { selectPeopleOffToday, selectUpcomingBirthdays, selectUpcomingEvents, selectAnnouncements, sortBalancesAnnualFirst } from "@/services/HomeFeedService";
const base = { calculatedDays: 5, startDate: "2026-09-29", endDate: "2026-10-03", isMasked: false };
it("keeps approved others, masks hidden types, flags remote work", () => {
  const out = selectPeopleOffToday([
    { ...base, requestId: "1", employeeId: "me", employeeName: "Ali Şahin", status: "approved", leaveTypeCode: "annual", leaveTypeName: "Yıllık izin" },
    { ...base, requestId: "2", employeeId: "a", employeeName: "Selin Aydın", status: "approved", leaveTypeCode: "annual", leaveTypeName: "Yıllık izin" },
    { ...base, requestId: "3", employeeId: "b", employeeName: "Murat Kaya", status: "pending", leaveTypeCode: "annual", leaveTypeName: "Yıllık izin" },
    { ...base, requestId: "4", employeeId: "c", employeeName: "Ece Demir", status: "approved", isMasked: true },
    { ...base, requestId: "5", employeeId: "d", employeeName: "Can Uz", status: "approved", leaveTypeCode: "remoteWork", leaveTypeName: "Uzaktan çalışma" },
  ], "me");
  expect(out.map((p) => p.employeeId)).toEqual(["a", "c", "d"]);
  expect(out[1].typeLabel).toBe("İzinli");
  expect(out[2]).toMatchObject({ isRemote: true, typeLabel: "Uzaktan çalışıyor" });
});
it("counts days left to birthdays and caps the list", () => {
  const out = selectUpcomingBirthdays([
    { id: "1", displayName: "Ece Demir", nextBirthday: "2026-10-02" },
    { id: "2", displayName: "Can Uz", nextBirthday: "2026-09-27" },
    { id: "3", displayName: "X", nextBirthday: "2026-10-10" }, { id: "4", displayName: "Y", nextBirthday: "2026-10-11" },
  ], "2026-09-27");
  expect(out).toHaveLength(3);
  expect(out[0]).toMatchObject({ id: "2", daysLeft: 0 });
  expect(out[1]).toMatchObject({ id: "1", daysLeft: 5 });
});
it("keeps only company events and meetings", () => {
  expect(selectUpcomingEvents([
    { title: "Doğum günü", date: "2026-10-02", type: "birthday" },
    { title: "İSG eğitimi", date: "2026-10-08", type: "companyEvent" },
    { title: "Aylık toplantı", date: "2026-10-09", type: "meeting" },
  ]).map((e) => e.title)).toEqual(["İSG eğitimi", "Aylık toplantı"]);
});
it("keeps announcements regardless of casing and drops recognitions", () => {
  const a = { content: "", type: "General", priority: "Normal", isRead: false };
  expect(selectAnnouncements([
    { ...a, id: "1", title: "Eski", kind: "Announcement", publishedAt: "2026-09-01T08:00:00Z" },
    { ...a, id: "2", title: "Tebrik", kind: "Recognition", publishedAt: "2026-09-26T08:00:00Z" },
    { ...a, id: "3", title: "Yeni", kind: "announcement", publishedAt: "2026-09-25T08:00:00Z" },
  ]).map((x) => x.id)).toEqual(["3", "1"]);
});
it("puts annual leave first", () => {
  expect(sortBalancesAnnualFirst([{ leaveTypeCode: "excuse" }, { leaveTypeCode: "annual" }])[0].leaveTypeCode).toBe("annual");
});
```
  Review Focus #1 — `DashboardScreen.test.ts`'e:
```ts
import { sectionFrom } from "@/screens/app/home/homeSections";
it("hides a section whose request failed or came back empty", () => {
  expect(sectionFrom({ status: "rejected", reason: new Error("403") }, (x) => x)).toBeNull();
  expect(sectionFrom({ status: "fulfilled", value: [] }, (x) => x)).toBeNull();
  expect(sectionFrom({ status: "fulfilled", value: [1, 2] }, (x) => x)).toEqual([1, 2]);
});
```
  Mevcut `formatUnreadBadge` testi korunur.
- [ ] **Step 2:** FAIL gör. **Step 3:** uygula (servis `apiClient.get` kalıbıyla, `LeaveService` gibi sınıf + singleton). **Step 4:** mobil kapı yeşil.

### Task 5: Backend — mola başı/sonu (Attendance)

**Files:** `src/Modules/Attendance/Sense.Modules.Attendance.Domain/**` (`PunchDirection`, `SelfCheckIn`), `…Application/SelfCheckIns/SelfCheckInFeatures.cs` (gerekirse yeni `SelfCheckInBreaks.cs` partial/yardımcı), `…Infrastructure/Persistence/**` (yalnız gerekiyorsa migration), `src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource{,.en}.resx` (sona ekleme), testler: modülün mevcut test projeleri (Attendance birim + entegrasyon).

**Interfaces — Produces (HTTP sözleşmesi, Task 9 bunu tüketir):**
```
POST /api/v1/attendance/self-checkin/gps   body { latitude, longitude, locationId?, direction: "in"|"out"|"breakStart"|"breakEnd" }
POST /api/v1/attendance/self-checkin/qr    body { code, direction: aynı küme, latitude?, longitude? }
  → 200 SelfCheckInResultDto (değişmez)
  → 400 hata kodları: SelfCheckInBreakNotCheckedIn | SelfCheckInBreakAlreadyOpen | SelfCheckInBreakNotOpen
GET  /api/v1/attendance/self-checkin/status
  → { isCheckedIn, date, checkIn?, checkOut?, onBreak: bool, breakStartedAt?: "HH:mm:ss", breakMinutesToday: int }
```
Kurallar (spec §6): `BreakStart` → bugün giriş var, çıkış yok, açık mola yok; `BreakEnd` → açık mola var; açık molayla `Out` → mola `Out` anında kapanır ve süresi eklenir. Süre `AttendanceDay.ActualBreakMinutes`'e eklenir; `DailyWorkCalculator` ile `BreakMinutesApplied`/`NetWorkedMinutes` yeniden hesaplanır (mevcut `AttendanceDayUpsert` yolu). Açık mola `SelfCheckIn` satırlarından türetilir. `BreakStart`/`BreakEnd` `AttendanceDay.CheckIn/CheckOut`'a dokunmaz. `PunchDirection` persist biçimini kontrol et: int ise yeni değerler (2,3) migration gerektirmez; string + uzunluk/check constraint ise modülün kendi migration'ını üret: `dotnet ef migrations add AddSelfCheckInBreaks --project src/Modules/Attendance/Sense.Modules.Attendance.Infrastructure --startup-project src/Sense.Migrator --context AttendanceDbContext`.

- [ ] **Step 1: Failing testler** (mevcut SelfCheckIn test sınıflarının yanına, aynı kalıpta): girişsiz `BreakStart` → `SelfCheckInBreakNotCheckedIn`; çift `BreakStart` → `SelfCheckInBreakAlreadyOpen`; molasız `BreakEnd` → `SelfCheckInBreakNotOpen`; 10:00 In, 12:00 BreakStart, 12:40 BreakEnd → `ActualBreakMinutes` 40 ve status `onBreak=false, breakMinutesToday=40`; 12:00 BreakStart iken 12:30 Out → mola kapanır (30) ve `CheckOut` 12:30; 12:10'da status `onBreak=true, breakStartedAt=12:00`; çıkış yapılmış günde `BreakStart` → `SelfCheckInBreakNotCheckedIn`.
- [ ] **Step 2:** yalnız Attendance test projelerini koş → FAIL.
- [ ] **Step 3:** uygula; hata kodlarını ve TR/EN metinleri resx'e **sona** ekle (TR: "Molaya başlamak için önce giriş yapmalısınız." / "Zaten açık bir molanız var." / "Bitirilecek açık bir mola yok."; EN: "Check in before starting a break." / "You already have an open break." / "There is no open break to end.").
- [ ] **Step 4:** Attendance testleri PASS; ardından takım test kapısı `node .claude/takim/betikler/test-kapisi.mjs` yeşil.

### Task 6: Ekranlar — Bordro, Profil, Bildirimler, Bildirim tercihleri, Kişiler, Hedefler, Destek

**Files:** `src/screens/app/{Payroll,Profile,Notifications,NotificationPreferences,People,Goals,Support}Screen.tsx` (+ gerekirse ekran başına küçük alt bileşen dosyası `src/screens/app/<ekran>/...`) + bunların mevcut testleri. **Consumes:** Task 1–3 (`Screen`, `ScreenHeader`, `GlassCard`, `SectionHeader`, `StatusPill`, `PrimaryButton`, `Avatar`, `BottomSheet`, `SegmentedControl`, `TextField`, `EmptyState`, `ErrorBanner`, `IconCircleButton`, `text`, `palette`, `tones`, `accents`, `radius`, `navigateToTarget`).

- **Bordro:** `ScreenHeader "Bordrom"`; en yeni bordro için vurgulu kart (`palette.brand` zemin, radius 20, beyaz metin): "Geçen ay net maaşın" caption, ay-yıl, büyük net tutar (`text.number` 30px); altında "Belgeyi aç" ve (onaylanmamışsa) "Okudum, onaylıyorum". Diğer aylar `GlassCard` satırları: sol ay adı (heading) + yıl (caption), sağ net tutar (heading, tabular), okundu/onay `StatusPill`. Mevcut indir/paylaş/önbellek temizleme ve onay akışı korunur.
- **Profil:** üstte büyük `Avatar` (self, 84) + ad (`text.title`) + ünvan/departman (bodyMuted); `SegmentedControl` sekmeleri mevcut profil verisinin gruplarına göre (Kişisel / İletişim / İş); her grup `GlassCard` içinde etiket (caption) + değer (body) satırları; altında menü kartı: Kişiler, Hedeflerim, Taleplerim, İzinlerim, Bildirim tercihleri, Destek (ikonlu satırlar, ›); en altta `PrimaryButton variant="danger"` "Çıkış yap".
- **Bildirimler:** okunmamış kartta sol mavi nokta + 800 başlık; kategori ikonu `accents`; sağ üstte "Tümünü okundu yap" hapı; boşsa `EmptyState`. Dokunuş: `resolveNotificationNavigationTarget` + `navigateToTarget`.
- **Bildirim tercihleri, Kişiler (arama `TextField`, kişi kartı `Avatar employeeId` + ad + departman), Hedefler (ilerleme çubuğu `palette.brand`), Destek (talep kartları + yeni talep formu `BottomSheet`):** `Screen` + `ScreenHeader` + cam kartlar, işlev aynen.
- Her ekranın `StyleSheet`'i `theme.ts` token'larına taşınır; `styles/colors`/`styles/typography` import'u kalmaz.

- [ ] **Step 1:** Ekranların mevcut testlerini oku; test ettikleri saf fonksiyonların sözleşmesini koru. Görünüm değişikliğiyle kırılacak bir test varsa önce testi yeni davranışa göre güncelle (silme yok).
- [ ] **Step 2:** Ekranları tek tek taşı; her ekrandan sonra `pnpm exec tsc --noEmit`.
- [ ] **Step 3:** Mobil kapı yeşil.

### Task 7: Ekranlar — Puantaj, İzinlerim, Taleplerim, Vardiyam + talep formları + tarih aralığı

**Files:** `src/screens/app/{Attendance,Leave,Requests,Shift}Screen.tsx`, `src/screens/app/NewRequestScreen.tsx` (yalnız gövde düzeni), `src/components/leave/*`, `src/components/requests/*`, `src/components/form/{DateField,TimeField}.tsx`, Create `src/components/form/DateRangeSheet.tsx`, `src/components/form/dateRange.ts` · Test: `src/__tests__/components/form/dateRange.test.ts` + mevcut testler.

**Interfaces — Produces:**
```ts
// components/form/dateRange.ts
export type DateRangeMode = "single" | "range";
export function daysInclusive(from: string, to: string): number;                     // "2026-01-11","2026-01-23" → 13
export function monthMatrix(year: number, month0: number): (string | null)[][];      // Pazartesi başlangıçlı 6x7 "yyyy-MM-dd"
export function applyDayTap(state: { from?: string; to?: string; mode: DateRangeMode }, day: string): { from?: string; to?: string };
// single: from=to=day · range: from yoksa ya da ikisi doluysa yeni başlangıç (to undefined); ikinci dokunuş day<from ise yer değiştirir
// DateRangeSheet props: { visible: boolean; mode: DateRangeMode; allowModeSwitch?: boolean; from?: string; to?: string; minDate?: string;
//   onApply(from: string, to: string): void; onClose(): void }
```
- **Puantaj:** ay seçici (‹ Eylül 2026 ›) + "Filtre" hapı (Tümü / Eksik / Anomali — istemci tarafı süzme); üstte özet kartı (bu ay gün/saat); gün kartı: sol büyük gün (28px 800) + "Eyl 2026" (caption, brandText), dikey ayraç, "Çalışma süresi" + "X sa Y dk", ›; altında `StatusPill` (Tamamlandı=success, Eksik=danger, Anomali=warning, İzinli=info) ve "Giriş 09:02 · Çıkış 18:10"; "Giriş / Çıkış" (→ `MainTabs{screen:"CheckIn"}`) ve "Vardiyam" kısayolları hap olarak.
- **İzinlerim:** bakiye yatay kartları (Task 4 `BalanceCarousel` yeniden kullanılır), "Yeni izin talebi" `PrimaryButton` → `NewRequest{kind:"leave"}`, talepler `LeaveRequestListItem` yeni kart (tür ikonu, tarih aralığı "29 Eyl → 3 Eki", gün, `StatusPill`; geri çekme/ek görüntüleme akışı aynen).
- **Talep formları (NewRequest gövdesi):** alanlar `TextField`/`SelectField`; izin türü seçimi `BottomSheet` listesi; tarih alanları `DateRangeSheet` (izin: Tek gün/Birden çok gün segmenti; mesai/masraf/avans: tek gün); "Süre: 10,00 gün" satırı mevcut önizlemeden; ek dosya (kamera/dosya ikon kareleri, mevcut picker akışları); en altta tam genişlik `PrimaryButton` "Talebi gönder".
- **Taleplerim:** tür filtresi hapları (Tümü/İzin/Fazla mesai/Masraf/Avans) + kartlar (tür ikonu, tarih, tutar/süre, `StatusPill`).
- **Vardiyam:** hafta şeridi + vardiya kartları, cam.

- [ ] **Step 1: Failing test** — `dateRange.test.ts`:
```ts
import { daysInclusive, monthMatrix, applyDayTap } from "@/components/form/dateRange";
it("counts inclusive days", () => { expect(daysInclusive("2026-01-11","2026-01-23")).toBe(13); expect(daysInclusive("2026-01-11","2026-01-11")).toBe(1); });
it("builds a Monday-first month grid", () => {
  const m = monthMatrix(2026, 8); // Eylül 2026, 1 Eylül Salı
  expect(m[0][0]).toBeNull(); expect(m[0][1]).toBe("2026-09-01"); expect(m.flat().filter(Boolean)).toHaveLength(30);
});
it("selects ranges and swaps reversed taps", () => {
  let s = applyDayTap({ mode: "range" }, "2026-01-23");
  s = applyDayTap({ mode: "range", ...s }, "2026-01-11");
  expect(s).toEqual({ from: "2026-01-11", to: "2026-01-23" });
  expect(applyDayTap({ mode: "range", from: "2026-01-11", to: "2026-01-23" }, "2026-02-01")).toEqual({ from: "2026-02-01", to: undefined });
  expect(applyDayTap({ mode: "single" }, "2026-01-05")).toEqual({ from: "2026-01-05", to: "2026-01-05" });
});
```
- [ ] **Step 2:** FAIL gör. **Step 3:** uygula, ekranları taşı. **Step 4:** mobil kapı yeşil.

### Task 8: Onaylarım + auth ekranları marka adı

**Files:** `src/screens/app/ApprovalsScreen.tsx`, `src/screens/app/approvalsRejectFlow.ts` (yalnız gerekirse), `src/screens/auth/*`, `src/screens/SplashScreen.tsx`, `src/components/auth/*` + mevcut testleri · Test: `src/__tests__/screens/ApprovalsScreen.test.ts` (genişlet).

**Interfaces — Produces:**
```ts
export type ApprovalFilter = "all" | "leave" | "overtime" | "expense" | "advance";
export function countByFilter(tasks: InboxTask[]): Record<ApprovalFilter, number>;
export function filterTasks(tasks: InboxTask[], filter: ApprovalFilter): InboxTask[];
```
- `ScreenHeader "Onaylarım"`; "Tür" etiketi (caption) + `brandText` renkli seçili değer "Fazla mesai (1) ⌄" → `BottomSheet` seçenek listesi (sayılarla). Kart: `Avatar` + ad (heading) + ünvan/departman (caption, brandText), sağ ›; "2,00 saat" / "5 gün" / "₺1.250" (heading) + tür (brandText); sağda `StatusPill "Bekliyor" warning`; ayraç; Başlangıç / Bitiş / Tutar satırları (etiket 700, değer 500); alt satırda `PrimaryButton variant="danger" "Reddet"` + `PrimaryButton "Onayla"` yan yana. Red gerekçesi mevcut akışla `BottomSheet` içinde. Boşsa `EmptyState "Onay bekleyen talep yok"`.
- Tür ayrımı: `ApprovalsService.ts`'deki `InboxTask`/`InboxTaskSummary` alanlarından (hangi alanın tür taşıdığını oku; bilinmeyen tür yalnız "Tümü"de sayılır).
- Auth: wordmark "Sense" + accent `HR`; metinler `fontFor`; `LoginScreen` düzeni korunur; wordmark'ı doğrulayan test varsa "Sense HR"a güncelle.

- [ ] **Step 1: Failing test** — `ApprovalsScreen.test.ts`'e: izin, mesai, masraf türünde birer görevle `countByFilter` → `{all:3, leave:1, overtime:1, expense:1, advance:0}`; `filterTasks(tasks,"overtime")` yalnız mesai; `filterTasks(tasks,"all")` hepsi (görev nesnelerini `InboxTask` tipine uygun kur).
- [ ] **Step 2:** FAIL. **Step 3:** uygula. **Step 4:** mobil kapı yeşil.

### Task 9: Giriş / Çıkış ekranı (harita + mola)

**Files:** `src/screens/app/CheckInScreen.tsx`, `src/services/AttendanceService.ts`, Create `src/lib/punch.ts`, `src/screens/app/checkin/{MapHeader,ClockCard,PunchTiles}.tsx` · Test: `src/__tests__/lib/punch.test.ts` (+ varsa AttendanceService testi).

**Interfaces — Consumes:** Task 5 HTTP sözleşmesi; Task 1 `app.config.js` `extra.hasMapsKey` (`expo-constants` `Constants.expoConfig?.extra`). **Produces:**
```ts
// services/AttendanceService.ts
export type PunchDirection = "in" | "out" | "breakStart" | "breakEnd";
export interface MyCheckInStatus { isCheckedIn: boolean; date: string; checkIn?: string | null; checkOut?: string | null;
  onBreak?: boolean; breakStartedAt?: string | null; breakMinutesToday?: number }
export interface GpsCheckInInput { latitude: number; longitude: number; locationId?: string; direction: PunchDirection }
gpsCheckIn(input: GpsCheckInInput): Promise<SelfCheckInResult>;   // POST self-checkin/gps
// lib/punch.ts
export type PunchAction = "in" | "out" | "breakStart" | "breakEnd";
export function nextPunchAction(s: MyCheckInStatus): PunchAction | "done"; // !checkIn→in · onBreak→breakEnd · checkIn&&!checkOut→out · checkOut→done
export function availableActions(s: MyCheckInStatus): Record<PunchAction, boolean>;
export function elapsedSinceCheckIn(checkIn: string | null | undefined, now: Date): string | null; // "2 sa 12 dk"
export function accuracyLabel(meters: number | null | undefined): "Doğru" | "Zayıf" | null;        // ≤50 Doğru
export function shouldRenderMap(extra: { hasMapsKey?: boolean } | undefined, platform: string): boolean; // ios → true, android → hasMapsKey===true
```
- Ekran (`Screen withTabBar`, başlık yok): üstte `MapHeader` (≈%35 yükseklik; harita varsa `MapView` + `Marker` + `Circle` 60 m, `userInterfaceStyle="dark"`; yoksa cam konum kartı "Konumun" + konum adı veya koordinat); `ClockCard` (canlı saat HH:mm:ss 1 sn aralık — reduced-motion'dan bağımsız, tarih "27 Eyl 2026", doğruluk `StatusPill` Doğru=success/Zayıf=warning); "Girişten beri …" satırı; `PunchTiles` 2×2 (Giriş/Çıkış/Mola başı/Mola sonu, saat veya "--:--"; sıradaki eylem `palette.brand` 2px kenarlık, uygun olmayan opaklık 0.4); birincil düğme sıradaki eylemin adı ("Giriş yap", "Molaya başla" değil — sıradaki `out` iken birincil "Çıkış yap", ikincil "Molaya başla"; `breakEnd` iken birincil "Molayı bitir"; `done` iken devre dışı "Bugün tamamlandı"); "QR ile giriş yap" ikincil (mevcut kamera akışı). Konum izni reddedilirse `ErrorBanner` + "Ayarları aç" (`Linking.openSettings`). Hata kodları (`SelfCheckInBreak*`, geofence) mevcut `apiError` yerelleştirmesiyle banner'da.

- [ ] **Step 1: Failing test** — `punch.test.ts`:
```ts
import { nextPunchAction, availableActions, elapsedSinceCheckIn, accuracyLabel, shouldRenderMap } from "@/lib/punch";
const d = "2026-09-27";
it("walks the day in order", () => {
  expect(nextPunchAction({ isCheckedIn: false, date: d })).toBe("in");
  expect(nextPunchAction({ isCheckedIn: true, date: d, checkIn: "09:00:00" })).toBe("out");
  expect(nextPunchAction({ isCheckedIn: true, date: d, checkIn: "09:00:00", onBreak: true, breakStartedAt: "12:00:00" })).toBe("breakEnd");
  expect(nextPunchAction({ isCheckedIn: false, date: d, checkIn: "09:00:00", checkOut: "18:00:00" })).toBe("done");
});
it("only allows a break while checked in", () => {
  expect(availableActions({ isCheckedIn: false, date: d }).breakStart).toBe(false);
  expect(availableActions({ isCheckedIn: true, date: d, checkIn: "09:00:00" })).toMatchObject({ breakStart: true, breakEnd: false, out: true, in: false });
  expect(availableActions({ isCheckedIn: true, date: d, checkIn: "09:00:00", onBreak: true })).toMatchObject({ breakStart: false, breakEnd: true });
});
it("formats elapsed time", () => expect(elapsedSinceCheckIn("10:00:00", new Date(2026, 8, 27, 12, 12))).toBe("2 sa 12 dk"));
it("labels accuracy", () => { expect(accuracyLabel(12)).toBe("Doğru"); expect(accuracyLabel(300)).toBe("Zayıf"); expect(accuracyLabel(null)).toBeNull(); });
it("never renders a keyless Android map", () => {
  expect(shouldRenderMap({ hasMapsKey: false }, "android")).toBe(false);
  expect(shouldRenderMap(undefined, "android")).toBe(false);
  expect(shouldRenderMap({ hasMapsKey: true }, "android")).toBe(true);
  expect(shouldRenderMap(undefined, "ios")).toBe(true);
});
```
- [ ] **Step 2:** FAIL. **Step 3:** uygula. **Step 4:** mobil kapı yeşil.

### Task 10: Bütün dal incelemesi, emülatör doğrulaması, PR

- [ ] **Step 1:** `takim-gozden-gecirici` tüm dalı inceler (test kapısını kendisi koşturur + mobil kapı); RET bulguları ilgili görevin geliştiricisine döner.
- [ ] **Step 2:** Native rebuild: `apps/senseik-mobile`'da `cd android && ./gradlew --stop` → `EXPO_NO_TYPESCRIPT_SETUP=1 CI=1 pnpm exec expo prebuild --clean --platform android` → `EXPO_PUBLIC_API_BASE_URL=http://10.0.2.2:18080 EXPO_NO_TYPESCRIPT_SETUP=1 CI=1 pnpm exec expo run:android --port 8081` (JAVA_HOME/ANDROID_HOME, CMake 3.31.6; `adb reverse tcp:8081 tcp:8081`, `tcp:19000`). Backend değiştiği için `docker compose build api && docker compose up -d --no-deps api` (+ migration varsa Migrator, temiz `git archive HEAD` export'undan).
- [ ] **Step 3:** Emülatörde her ekranın ekran görüntüsü; Review Focus 1–5'i elle dene (mola sırası, haritasız açılış, uzun ad, alt boşluk, bölüm gizleme).
- [ ] **Step 4:** `docs/analysis/kolayik-video/faz1a-durum-takibi.md`'ye mobil yeniden tasarım satırı; `design-log.md`'ye bir satır (ders-kaydet PR'ında).
- [ ] **Step 5:** `dev`'e PR aç (`gh pr create --base dev`), gövdede ekran görüntüleri ve "Google Maps anahtarı insan aksiyonu" notu.
