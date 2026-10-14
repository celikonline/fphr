// User and Authentication Types
/** Mirrors the `tenant` object on `MeDto` (Sense.Modules.Identity.Application/Dtos.cs). */
export interface MeTenant {
  id: string;
  name: string;
  slug: string;
}

/** Mapped from `GET /api/v1/identity/me` (`MeDto`). */
export interface User {
  id: string;
  tenantId: string;
  email: string;
  firstName: string;
  lastName: string;
  employeeId?: string | null;
  preferredLanguage: string;
  mfaEnabled: boolean;
  roles: string[];
  permissions: string[];
  tenant: MeTenant;
  /** Disabled tenant modules from `MeDto.features.disabledModules`; absent means all are enabled. */
  disabledModules?: string[];
}

/** Mirrors `AuthTokensDto` (Sense.Modules.Identity.Application/Dtos.cs). */
export interface AuthTokens {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  tokenType: string;
}

/** Mirrors an entry of `LoginResultDto.tenants` (`TenantChoiceDto`) when login returns `select_tenant`. */
export interface TenantChoice {
  tenantId: string;
  slug: string;
  name: string;
}

export interface BiometricAvailable {
  available: boolean;
  enrolled: boolean;
  supportedTypes: BiometricType[];
}

export enum BiometricType {
  Fingerprint = "fingerprint",
  FaceRecognition = "face",
  Iris = "iris",
}

// API Error Response
export interface ApiError {
  statusCode: number;
  message: string;
  errors?: Record<string, string[]>;
  timestamp: string;
}

// Network State
export interface NetworkState {
  isConnected: boolean;
  isInternetReachable: boolean;
  type: NetworkType;
}

export enum NetworkType {
  None = "none",
  Unknown = "unknown",
  Cellular = "cellular",
  Wifi = "wifi",
  Bluetooth = "bluetooth",
  Ethernet = "ethernet",
  VPN = "vpn",
}

// Push Notification Types
export interface PushToken {
  token: string;
  type: "ios" | "android" | "web";
  userId?: string;
  registeredAt?: string;
}

export interface NotificationPayload {
  title: string;
  body: string;
  data?: Record<string, string>;
  screen?: string;
  id?: string;
}

// Generic Response Wrapper
export interface ApiResponse<T> {
  data?: T;
  success: boolean;
  message?: string;
}
