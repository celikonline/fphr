# SenseİK Mobile

React Native mobile app for SenseİK HR platform with Expo, TypeScript, JWT auth, biometric login, and push notifications.

## Features

- JWT Authentication with token refresh
- Biometric login (fingerprint/face)
- Push notifications via Expo
- Secure token storage (keychain/keystore)
- Zustand state management
- Tab-based navigation
- Full TypeScript strict mode

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

1. Email + password → POST /api/auth/login
2. Receive access + refresh tokens
3. Store in SecureStore
4. Navigate to AppStack

### Biometric

1. User taps "Use Fingerprint" on login screen
2. Prompts native biometric
3. On success: refresh tokens → login user
4. On failure: fallback to password

### Logout

1. POST /api/auth/logout (optional)
2. Clear SecureStore
3. Redirect to LoginScreen

### Token Refresh

- Automatic on 401 via axios interceptor
- Calls POST /api/auth/refresh with refresh token
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

**Token refresh failing**: Verify refresh token in SecureStore, check backend /api/auth/refresh

**Biometric not showing**: Verify device has biometric hardware and enrolled, test on real device

**Push notifications not received**: Verify push token registered, check permissions, test with Expo tool

## Environment Variables

- **EXPO_PUBLIC_API_BASE_URL**: Backend API endpoint (required)
- **EXPO_PUBLIC_APP_NAME**: App display name (optional)

Variables starting with EXPO_PUBLIC_ are embedded in build.

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
