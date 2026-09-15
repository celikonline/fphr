// User and Authentication Types
export interface User {
  id: string;
  tenantId: string;
  email: string;
  firstName: string;
  lastName: string;
  phoneNumber?: string;
  profileImageUrl?: string;
  role: UserRole;
  permissions: string[];
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export enum UserRole {
  Admin = "Admin",
  Manager = "Manager",
  Employee = "Employee",
  Approver = "Approver"
}

export interface AuthTokens {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
}

export interface AuthResponse {
  user: User;
  tokens: AuthTokens;
  tenantId: string;
}

export interface BiometricAvailable {
  available: boolean;
  enrolled: boolean;
  supportedTypes: BiometricType[];
}

export enum BiometricType {
  Fingerprint = "fingerprint",
  FaceRecognition = "face",
  Iris = "iris"
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
  VPN = "vpn"
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
