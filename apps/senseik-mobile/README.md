# Sense HR Mobile

React Native mobile app for the Sense HR platform with Expo, TypeScript, JWT auth, biometric login, and push notifications.

## Features

- JWT Authentication with token refresh
- Biometric login (fingerprint/face)
- Push notifications via Expo
- Secure token storage (keychain/keystore)
- Zustand state management
- Dark glass design system (`src/theme/theme.ts` + `src/components/ui`), Manrope font
- 3-item notched tab bar (Home · brand-mark "+" FAB · Check-in) with stack screens
- Full TypeScript strict mode

## Ekran görüntüleri

Android emülatörü (Pixel 7 Pro, dev build), yerel demo verisiyle — 2026-09-28. Tasarım: [spec](../../docs/superpowers/specs/2026-09-27-mobil-yeniden-tasarim-design.md).

### Giriş

| Açılış | Giriş | Şifremi unuttum | Sıfırlama gönderildi |
|---|---|---|---|
| <img src="docs/screenshots/01-acilis.jpg" width="200" alt="Açılış ekranı"> | <img src="docs/screenshots/02-giris.jpg" width="200" alt="Giriş"> | <img src="docs/screenshots/03-sifremi-unuttum.jpg" width="200" alt="Şifremi unuttum"> | <img src="docs/screenshots/04-sifirlama-gonderildi.jpg" width="200" alt="Sıfırlama bağlantısı gönderildi"> |

### Ana sayfa ve gezinme

| Ana sayfa | Ana sayfa akışı | Onay rozeti (yönetici) | ⋮ menü | Çıkış onayı |
|---|---|---|---|---|
| <img src="docs/screenshots/05-ana-sayfa.jpg" width="170" alt="Ana sayfa"> | <img src="docs/screenshots/06-ana-sayfa-akis.jpg" width="170" alt="İzindekiler ve doğum günleri"> | <img src="docs/screenshots/07-ana-sayfa-onay-rozeti.jpg" width="170" alt="Onay rozeti"> | <img src="docs/screenshots/08-menu.jpg" width="170" alt="Menü"> | <img src="docs/screenshots/09-cikis-onayi.jpg" width="170" alt="Çıkış onayı"> |

### Talepler

| Yeni talep (+) | İzin talebi | Tarih seç | Taleplerim |
|---|---|---|---|
| <img src="docs/screenshots/10-yeni-talep.jpg" width="200" alt="Yeni talep alt sayfası"> | <img src="docs/screenshots/11-izin-talebi.jpg" width="200" alt="İzin talebi formu"> | <img src="docs/screenshots/12-tarih-sec.jpg" width="200" alt="Tarih aralığı seçimi"> | <img src="docs/screenshots/16-taleplerim.jpg" width="200" alt="Taleplerim"> |

### Puantaj, izin, bordro

| Giriş / Çıkış | Puantaj | İzinlerim | Bordro |
|---|---|---|---|
| <img src="docs/screenshots/13-giris-cikis.jpg" width="200" alt="Giriş / Çıkış ve mola"> | <img src="docs/screenshots/14-puantaj.jpg" width="200" alt="Puantaj"> | <img src="docs/screenshots/15-izinlerim.jpg" width="200" alt="İzinlerim"> | <img src="docs/screenshots/17-bordro.jpg" width="200" alt="Bordro"> |

### Onaylar ve diğer ekranlar

| Onaylarım | Tür filtresi | Profil | Bildirimler |
|---|---|---|---|
| <img src="docs/screenshots/18-onaylarim.jpg" width="200" alt="Onaylarım"> | <img src="docs/screenshots/19-onay-turu-filtresi.jpg" width="200" alt="Onay türü filtresi"> | <img src="docs/screenshots/20-profil.jpg" width="200" alt="Profil"> | <img src="docs/screenshots/21-bildirimler.jpg" width="200" alt="Bildirimler"> |

| Kişiler | Vardiyam | Destek |
|---|---|---|
| <img src="docs/screenshots/22-kisiler.jpg" width="200" alt="Kişiler"> | <img src="docs/screenshots/23-vardiyam.jpg" width="200" alt="Vardiyam"> | <img src="docs/screenshots/24-destek.jpg" width="200" alt="Destek"> |

> Giriş / Çıkış ekranında harita, yalnız `GOOGLE_MAPS_ANDROID_API_KEY` ile derlenen yapılarda görünür (aşağıdaki "Google Maps key" bölümü); görüntüdeki yapıda anahtar yok ve emülatörde konum sinyali alınamadığından konum kartı "Konum alınamadı" durumunda.

## Prerequisites

- Node.js 20.x (pinned in .nvmrc)
- pnpm 9.0.0+
- Expo CLI: `npm install -g expo-cli`
- EAS CLI: `npm install -g eas-cli` (for building)

## Setup

### Install dependencies

```bash
cd apps/senseik-mobile
pnpm install
```

### Configure environment

```bash
cp .env.example .env.local
```

Edit .env.local:

```
EXPO_PUBLIC_API_BASE_URL=http://localhost:5000
EXPO_PUBLIC_APP_NAME=SenseİK Mobile
```

### Verify TypeScript

```bash
pnpm type-check
```

## Development

Start Expo dev server:

```bash
pnpm start
```

Run on simulators:

```bash
pnpm ios         # iOS simulator
pnpm android     # Android emulator
pnpm web         # Web (experimental)
```

### Android development build

Expo Go can't run this app (Android push notifications were removed from Expo Go in SDK 53),
so `pnpm android` runs `expo run:android`: it generates `android/` with `expo prebuild` and
installs a development build. `android/` and `ios/` are generated and gitignored; native tweaks
live in `plugins/withAndroidBuildFixes.js`.

Prerequisites (Windows): Android Studio, an AVD, and **CMake 3.31.6** in the Android SDK
(SDK Manager → SDK Tools → Show Package Details → CMake 3.31.6). The SDK's default CMake 3.22
ships ninja 1.10, which fails on pnpm's long `node_modules` paths.

```bash
# JAVA_HOME = Android Studio's bundled JDK, ANDROID_HOME = %LOCALAPPDATA%\Android\Sdk
EXPO_PUBLIC_API_BASE_URL=http://10.0.2.2:18080 pnpm android
```

`10.0.2.2` is the host machine as seen from the emulator (`18080` is the dockerized API).
To work in Android Studio, open `apps/senseik-mobile/android` after the first prebuild and keep
Metro running with `pnpm start --dev-client`.

Document upload (leave requests that `requiresDocument`) PUTs straight to MinIO's presigned URL, which
in dev points at `localhost:19000` (not `10.0.2.2`, since the API - not the app - generates that URL);
run `adb reverse tcp:19000 tcp:19000` once per emulator session so the emulator's `localhost:19000`
reaches the host's MinIO. Do not rewrite the presigned URL in code to work around this.

## Architecture

### Authentication

- Email/password login → JWT tokens (access: 15min, refresh: 7d)
- Tokens stored in SecureStore (encrypted keychain)
- Auto-refresh on 401 via axios interceptor
- Biometric login (requires prior password login)

### Storage

- **SecureStore**: tokens, tenant ID, user ID, biometric enabled flag
- **AsyncStorage**: theme, language, last email, app state

### State Management

Zustand store in `src/store/authStore.ts`:

```typescript
const { isAuthenticated, user, login, logout, loginWithBiometric } = useAuthStore();
```

### Push Notifications

- Token registration on app launch
- Sent to backend: POST /api/users/{userId}/push-tokens
- Listeners for foreground/background/tap events

### API Client

Axios instance with:
- Automatic token refresh on 401
- 30-second timeout
- Retry queue for failed requests during refresh

## Project Structure

```
src/
├── screens/
│   ├── auth/         LoginScreen, BiometricPromptScreen, ForgotPasswordScreen
│   └── app/          DashboardScreen, AttendanceScreen, LeaveScreen, etc.
├── navigation/       RootNavigator (stacks + tabs)
├── lib/
│   ├── apiClient.ts  Axios with refresh interceptor
│   ├── storage.ts    SecureStore + AsyncStorage wrappers
│   └── biometric.ts  Fingerprint/face authentication
├── services/
│   ├── AuthService.ts        Login/logout/refresh
│   └── PushNotificationService.ts
├── store/
│   └── authStore.ts  Zustand auth state
├── types/            TypeScript definitions
└── hooks/            Custom hooks (useAuth)
```

## Key Files

| File | Purpose |
|------|---------|
| src/lib/apiClient.ts | Axios client with token refresh interceptor |
| src/services/AuthService.ts | Auth API operations |
| src/store/authStore.ts | Global auth state (Zustand) |
| src/lib/biometric.ts | Biometric authentication |
| src/services/PushNotificationService.ts | Push token & notification handling |
| app.json | Expo configuration |
| tsconfig.json | TypeScript config (strict mode, path aliases) |

## Authentication Flow

### Login

1. Email + password → POST /api/v1/identity/auth/login
2. Receive access + refresh tokens
3. Store in SecureStore
4. Navigate to AppStack

### Biometric

1. User taps "Use Fingerprint" on login screen
2. Prompts native biometric
3. On success: refresh tokens → login user
4. On failure: fallback to password

### Logout

1. POST /api/v1/identity/auth/logout (optional)
2. Clear SecureStore
3. Redirect to LoginScreen

### Token Refresh

- Automatic on 401 via axios interceptor
- Calls POST /api/v1/identity/auth/refresh with refresh token
- Stores new tokens
- Retries original request
- Transparent to UI

## Testing

Run tests:

```bash
pnpm test
```

Jest setup with mocked:
- expo-secure-store
- expo-local-authentication
- expo-notifications

## Building

### EAS Build

iOS:

```bash
eas build --platform ios
```

Android:

```bash
eas build --platform android
```

Production:

```bash
eas build --platform ios --auto-submit
eas build --platform android --auto-submit
```

## Troubleshooting

**App won't load**: Check network, verify EXPO_PUBLIC_API_BASE_URL, ensure backend running

**Token refresh failing**: Verify refresh token in SecureStore, check backend /api/v1/identity/auth/refresh

**Biometric not showing**: Verify device has biometric hardware and enrolled, test on real device

**Push notifications not received**: Verify push token registered, check permissions, test with Expo tool

## Environment Variables

- **EXPO_PUBLIC_API_BASE_URL**: Backend API endpoint (required)
- **EXPO_PUBLIC_APP_NAME**: App display name (optional)

Variables starting with EXPO_PUBLIC_ are embedded in build.

### Google Maps key (Android)

`GOOGLE_MAPS_ANDROID_API_KEY` is read by `app.config.js` at prebuild/build time and written into the
Android manifest (`android.config.googleMaps.apiKey`). It is not an `EXPO_PUBLIC_` variable and is
never read by JS; JS only sees `extra.hasMapsKey`.

- **Without the key** the Android Giriş/Çıkış tab shows the location card (address/coordinates)
  instead of the map, because the Google Maps SDK crashes without a key. iOS uses Apple Maps and
  needs no key.
- **Local builds**: put `GOOGLE_MAPS_ANDROID_API_KEY=...` in `.env.local` (gitignored) or export it
  in the shell, then rebuild the native app (`pnpm exec expo run:android`). A Metro reload is not
  enough; the key lives in the native manifest.
- **EAS builds**: store it as a secret EAS environment variable, e.g.
  `eas env:create --name GOOGLE_MAPS_ANDROID_API_KEY --value <key> --visibility secret --environment production`
  (repeat for `preview`/`development` as needed).
- **Never commit the key** (not in `app.json`, `eas.json`, `.env.example` or docs). Restrict it in
  Google Cloud to "Maps SDK for Android" and the app's package name + signing SHA-1.

## Best Practices

1. Never store tokens in AsyncStorage - use SecureStore
2. Test biometric and notifications on real device
3. Handle 401 errors with logout + redirect to login
4. Use Zustand store for global state
5. Add TypeScript types for all API responses
6. Test token refresh manually (set short expiry)
7. Mock APIs during development
8. Never commit .env.local
9. Use path aliases (@/) for imports
10. Run type-check before committing

## Next Steps (Item 20+)

- UI components and screens
- Attendance/leave/payroll screen implementation
- Approval workflows
- Settings and preferences
- Offline mode
- Advanced navigation patterns
