import React, { useEffect, useRef, useState } from "react";
import {
  ActivityIndicator,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";
import Animated, { FadeInDown } from "react-native-reanimated";
import { NativeStackScreenProps } from "@react-navigation/native-stack";
import { AuthStackParamList } from "@/types/navigation";
import { useAuthStore } from "@/store/authStore";
import { biometricService } from "@/lib/biometric";
import { getApiProblem } from "@/lib/apiError";
import { TenantChoice } from "@/types";
import { AuthBackdrop } from "@/components/auth/AuthBackdrop";
import { AuthPanel as Panel } from "@/components/auth/AuthPanel";
import { BrandMark } from "@/components/auth/BrandMark";
import { Wordmark } from "@/components/auth/Wordmark";

type Props = NativeStackScreenProps<AuthStackParamList, "Login">;

// Design tokens for this screen only - shared UI colors live in `theme/theme.ts`, but this
// dark auth shell (night/navy/brand gradient) is specific to the login/tenant-select flow.
const palette = {
  night: "#0E0033",
  navy: "#280087",
  brand: "#008CF0",
  ice: "#D3ECFD",
  coral: "#FF8A80",
  white: "#FFFFFF",
  glassBorder: "rgba(255,255,255,0.14)",
};

function Footer() {
  return <Text style={styles.footer}>Algosense güvencesiyle · © {new Date().getFullYear()}</Text>;
}

function ErrorBanner({ message }: { message: string }) {
  return (
    <View style={styles.errorBanner} accessibilityLiveRegion="polite" accessibilityRole="alert">
      <Text style={styles.errorText}>{message}</Text>
    </View>
  );
}

type AuthFieldProps = { label: string; focused: boolean; children: React.ReactNode };

function AuthField({ label, focused, children }: AuthFieldProps) {
  return (
    <View style={styles.fieldGroup}>
      <Text style={styles.fieldLabel}>{label}</Text>
      <View style={[styles.inputWrapper, focused && styles.inputWrapperFocused]}>{children}</View>
    </View>
  );
}

export default function LoginScreen({ navigation }: Props) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [focusedField, setFocusedField] = useState<"email" | "password" | "mfaCode" | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [biometricAvailable, setBiometricAvailable] = useState(false);
  const [mfaCode, setMfaCode] = useState("");
  const [mfaError, setMfaError] = useState<string | null>(null);
  const passwordRef = useRef<TextInput>(null);
  const {
    login,
    completeMfa,
    clearMfaChallenge,
    isLoading,
    tenantChoices,
    clearTenantChoices,
    mfaToken,
  } = useAuthStore();

  useEffect(() => {
    const checkBiometric = async () => {
      const available = await biometricService.isBiometricAvailable();
      setBiometricAvailable(available.available);
    };
    checkBiometric();
  }, []);

  const handleEmailChange = (value: string) => {
    setEmail(value);
    if (formError) setFormError(null);
  };

  const handlePasswordChange = (value: string) => {
    setPassword(value);
    if (formError) setFormError(null);
  };

  const handleLogin = async (tenantSlug?: string) => {
    if (!email || !password) {
      setFormError("Lütfen e-posta ve şifrenizi girin");
      return;
    }

    try {
      // Resolves to "ok" (store marks the session authenticated - navigation reacts to that),
      // "select_tenant" (store fills `tenantChoices`, rendered below instead of the form), or
      // "mfaRequired" (store fills `mfaToken`, the code-entry step below replaces the form).
      await login(email, password, tenantSlug);
    } catch {
      // Generic message: backend/axios errors can leak status codes, hosts or endpoint details into
      // screenshots, and precise messages enable user enumeration.
      setFormError("E-posta veya şifre hatalı. Lütfen tekrar deneyin.");
    }
  };

  const handleSelectTenant = (tenant: TenantChoice) => {
    void handleLogin(tenant.slug);
  };

  const handleMfaCodeChange = (value: string) => {
    setMfaCode(value.replace(/[^0-9]/g, "").slice(0, 6));
    if (mfaError) setMfaError(null);
  };

  const handleMfaSubmit = async () => {
    if (mfaCode.length !== 6) return;

    try {
      await completeMfa(mfaCode);
    } catch (error) {
      const problem = getApiProblem(error);
      if (problem?.code === "identity.mfa.token_expired") {
        // The challenge itself expired (not the code): only a fresh login can issue a new one.
        clearMfaChallenge();
        setMfaCode("");
        setFormError("Doğrulama süresi doldu. Lütfen tekrar giriş yapın.");
      } else {
        setMfaCode("");
        setMfaError("Doğrulama kodu hatalı. Lütfen tekrar deneyin.");
      }
    }
  };

  const handleCancelMfa = () => {
    setFormError(null);
    setMfaError(null);
    setMfaCode("");
    clearMfaChallenge();
  };

  const handleBiometricLogin = () => {
    if (!email) {
      setFormError("Lütfen önce e-posta adresinizi girin");
      return;
    }
    navigation.navigate("BiometricPrompt", { email });
  };

  const handleClearTenantChoices = () => {
    setFormError(null);
    clearTenantChoices();
  };

  return (
    <AuthBackdrop>
      <SafeAreaView style={styles.flexOne} edges={["top", "bottom"]}>
        <KeyboardAvoidingView
          style={styles.flexOne}
          behavior={Platform.OS === "ios" ? "padding" : undefined}
        >
          <ScrollView
            contentContainerStyle={styles.scrollContent}
            keyboardShouldPersistTaps="handled"
          >
            {mfaToken ? (
              <>
                <View style={styles.heroCompact}>
                  <BrandMark size={56} />
                  <Wordmark />
                </View>
                <Panel>
                  <Text style={styles.panelTitle}>İki adımlı doğrulama</Text>
                  <Text style={styles.panelSubtitle}>
                    Kimlik doğrulama uygulamanızdaki 6 haneli kodu girin.
                  </Text>
                  {mfaError && <ErrorBanner message={mfaError} />}
                  <AuthField label="DOĞRULAMA KODU" focused={focusedField === "mfaCode"}>
                    <TextInput
                      style={styles.input}
                      placeholder="000000"
                      placeholderTextColor="rgba(211,236,253,0.45)"
                      selectionColor={palette.brand}
                      value={mfaCode}
                      onChangeText={handleMfaCodeChange}
                      onFocus={() => setFocusedField("mfaCode")}
                      onBlur={() => setFocusedField(null)}
                      editable={!isLoading}
                      keyboardType="number-pad"
                      maxLength={6}
                      autoComplete="one-time-code"
                      textContentType="oneTimeCode"
                      returnKeyType="done"
                      onSubmitEditing={() => handleMfaSubmit()}
                    />
                  </AuthField>
                  <Pressable
                    style={({ pressed }) => [
                      styles.primaryButton,
                      pressed && styles.primaryButtonPressed,
                    ]}
                    onPress={() => handleMfaSubmit()}
                    disabled={isLoading || mfaCode.length !== 6}
                  >
                    {isLoading ? (
                      <ActivityIndicator color={palette.white} />
                    ) : (
                      <Text style={styles.primaryButtonText}>Doğrula</Text>
                    )}
                  </Pressable>
                  <TouchableOpacity
                    onPress={handleCancelMfa}
                    disabled={isLoading}
                    style={styles.secondaryLink}
                  >
                    <Text style={styles.secondaryLinkText}>Farklı hesapla giriş yap</Text>
                  </TouchableOpacity>
                </Panel>
                <Footer />
              </>
            ) : tenantChoices ? (
              <>
                <View style={styles.heroCompact}>
                  <BrandMark size={56} />
                  <Wordmark />
                </View>
                <Panel>
                  <Text style={styles.panelTitle}>Şirket seçin</Text>
                  <Text style={styles.panelSubtitle}>
                    Bu e-posta adresi birden fazla şirkete kayıtlı. Devam etmek için bir şirket
                    seçin.
                  </Text>
                  {tenantChoices.map((tenant) => (
                    <TouchableOpacity
                      key={tenant.tenantId}
                      style={styles.tenantRow}
                      onPress={() => handleSelectTenant(tenant)}
                      disabled={isLoading}
                    >
                      <View style={styles.tenantBadge}>
                        <Text style={styles.tenantBadgeText}>
                          {tenant.name.charAt(0).toLocaleUpperCase("tr")}
                        </Text>
                      </View>
                      <Text style={styles.tenantName}>{tenant.name}</Text>
                      <Text style={styles.tenantChevron}>›</Text>
                    </TouchableOpacity>
                  ))}
                  {isLoading && (
                    <ActivityIndicator color={palette.brand} style={styles.tenantLoading} />
                  )}
                  <TouchableOpacity
                    onPress={handleClearTenantChoices}
                    disabled={isLoading}
                    style={styles.secondaryLink}
                  >
                    <Text style={styles.secondaryLinkText}>Farklı hesapla giriş yap</Text>
                  </TouchableOpacity>
                </Panel>
                <Footer />
              </>
            ) : (
              <>
                <Animated.View entering={FadeInDown.delay(250).duration(500)} style={styles.hero}>
                  <BrandMark size={56} />
                  <Text style={styles.eyebrow}>İK & BORDRO · MOBİL</Text>
                  <Wordmark />
                  <Text style={styles.heroSubtitle}>
                    İzin, puantaj ve bordronuz artık cebinizde.
                  </Text>
                </Animated.View>
                <Panel>
                  <Text style={styles.panelTitle}>Tekrar hoş geldiniz</Text>
                  <Text style={styles.panelSubtitle}>Devam etmek için hesabınıza giriş yapın.</Text>
                  {formError && <ErrorBanner message={formError} />}
                  <AuthField label="E-POSTA" focused={focusedField === "email"}>
                    <TextInput
                      style={styles.input}
                      placeholder="ad.soyad@sirket.com"
                      placeholderTextColor="rgba(211,236,253,0.45)"
                      selectionColor={palette.brand}
                      value={email}
                      onChangeText={handleEmailChange}
                      onFocus={() => setFocusedField("email")}
                      onBlur={() => setFocusedField(null)}
                      editable={!isLoading}
                      keyboardType="email-address"
                      autoCapitalize="none"
                      autoCorrect={false}
                      autoComplete="email"
                      textContentType="emailAddress"
                      returnKeyType="next"
                      onSubmitEditing={() => passwordRef.current?.focus()}
                    />
                  </AuthField>
                  <AuthField label="ŞİFRE" focused={focusedField === "password"}>
                    <View style={styles.passwordRow}>
                      <TextInput
                        ref={passwordRef}
                        style={[styles.input, styles.passwordInput]}
                        placeholder="Şifrenizi girin"
                        placeholderTextColor="rgba(211,236,253,0.45)"
                        selectionColor={palette.brand}
                        value={password}
                        onChangeText={handlePasswordChange}
                        onFocus={() => setFocusedField("password")}
                        onBlur={() => setFocusedField(null)}
                        editable={!isLoading}
                        secureTextEntry={!showPassword}
                        autoCapitalize="none"
                        textContentType="password"
                        autoComplete="password"
                        returnKeyType="go"
                        onSubmitEditing={() => handleLogin()}
                      />
                      <TouchableOpacity
                        onPress={() => setShowPassword((value) => !value)}
                        accessibilityRole="button"
                        accessibilityLabel={showPassword ? "Şifreyi gizle" : "Şifreyi göster"}
                        style={styles.showHideButton}
                      >
                        <Text style={styles.showHideText}>{showPassword ? "Gizle" : "Göster"}</Text>
                      </TouchableOpacity>
                    </View>
                  </AuthField>
                  <Pressable
                    style={({ pressed }) => [
                      styles.primaryButton,
                      pressed && styles.primaryButtonPressed,
                    ]}
                    onPress={() => handleLogin()}
                    disabled={isLoading}
                  >
                    {isLoading ? (
                      <ActivityIndicator color={palette.white} />
                    ) : (
                      <Text style={styles.primaryButtonText}>Giriş yap</Text>
                    )}
                  </Pressable>
                  {biometricAvailable && (
                    <TouchableOpacity
                      style={styles.secondaryButton}
                      onPress={handleBiometricLogin}
                      disabled={isLoading}
                    >
                      <Text style={styles.secondaryButtonText}>Biyometrik ile giriş yap</Text>
                    </TouchableOpacity>
                  )}
                  <TouchableOpacity
                    style={styles.secondaryLink}
                    onPress={() => navigation.navigate("ForgotPassword")}
                  >
                    <Text style={styles.secondaryLinkText}>Şifremi unuttum</Text>
                  </TouchableOpacity>
                </Panel>
                <Footer />
              </>
            )}
          </ScrollView>
        </KeyboardAvoidingView>
      </SafeAreaView>
    </AuthBackdrop>
  );
}

const styles = StyleSheet.create({
  flexOne: { flex: 1 },
  scrollContent: {
    flexGrow: 1,
    justifyContent: "space-between",
    paddingHorizontal: 16,
    paddingVertical: 24,
  },
  hero: { paddingHorizontal: 12 },
  heroCompact: { paddingHorizontal: 12, alignItems: "flex-start", gap: 12 },
  eyebrow: {
    marginTop: 20,
    fontSize: 11,
    fontWeight: "700",
    letterSpacing: 2.4,
    textTransform: "uppercase",
    color: palette.ice,
  },
  heroSubtitle: { marginTop: 10, fontSize: 15, color: palette.ice },
  panelTitle: { fontSize: 22, fontWeight: "800", color: palette.white },
  panelSubtitle: { marginTop: 4, fontSize: 15, color: palette.ice },
  errorBanner: {
    marginTop: 16,
    padding: 12,
    borderRadius: 12,
    backgroundColor: "rgba(255,138,128,0.12)",
    borderWidth: 1,
    borderColor: "rgba(255,138,128,0.4)",
  },
  errorText: { fontSize: 14, color: palette.coral },
  fieldGroup: { marginTop: 18 },
  fieldLabel: {
    fontSize: 12,
    fontWeight: "600",
    letterSpacing: 0.6,
    textTransform: "uppercase",
    color: palette.ice,
    marginBottom: 6,
  },
  inputWrapper: {
    height: 52,
    borderRadius: 14,
    backgroundColor: "rgba(255,255,255,0.06)",
    borderWidth: 1,
    borderColor: palette.glassBorder,
    flexDirection: "row",
    alignItems: "center",
    paddingHorizontal: 16,
  },
  inputWrapperFocused: { borderColor: palette.brand, boxShadow: "0 0 0 3px rgba(0,140,240,0.25)" },
  input: { flex: 1, height: "100%", fontSize: 15, color: palette.white },
  passwordRow: { flex: 1, flexDirection: "row", alignItems: "center" },
  passwordInput: { flex: 1 },
  showHideButton: {
    paddingHorizontal: 4,
    paddingVertical: 8,
    minWidth: 44,
    minHeight: 44,
    alignItems: "center",
    justifyContent: "center",
  },
  showHideText: { fontSize: 13, fontWeight: "600", color: palette.ice },
  primaryButton: {
    marginTop: 24,
    height: 54,
    borderRadius: 16,
    backgroundColor: palette.brand,
    alignItems: "center",
    justifyContent: "center",
    boxShadow: "0 10px 24px rgba(0,140,240,0.45)",
  },
  primaryButtonPressed: { transform: [{ scale: 0.98 }] },
  primaryButtonText: { fontSize: 16, fontWeight: "700", color: palette.white },
  secondaryButton: {
    marginTop: 12,
    height: 52,
    borderRadius: 16,
    borderWidth: 1,
    borderColor: palette.glassBorder,
    backgroundColor: "transparent",
    alignItems: "center",
    justifyContent: "center",
  },
  secondaryButtonText: { fontSize: 15, fontWeight: "600", color: palette.white },
  secondaryLink: {
    marginTop: 16,
    alignSelf: "center",
    minHeight: 44,
    justifyContent: "center",
    paddingHorizontal: 12,
  },
  secondaryLinkText: { fontSize: 14, color: palette.ice },
  tenantRow: { marginTop: 16, flexDirection: "row", alignItems: "center", gap: 12 },
  tenantBadge: {
    width: 40,
    height: 40,
    borderRadius: 10,
    backgroundColor: palette.navy,
    alignItems: "center",
    justifyContent: "center",
  },
  tenantBadgeText: { fontSize: 16, fontWeight: "700", color: palette.white },
  tenantName: { flex: 1, fontSize: 16, fontWeight: "600", color: palette.white },
  tenantChevron: { fontSize: 18, color: palette.ice },
  tenantLoading: { marginTop: 16 },
  footer: { marginTop: 16, textAlign: "center", fontSize: 12, color: "rgba(211,236,253,0.6)" },
});
