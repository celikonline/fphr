import React, { useEffect, useRef, useState } from "react";
import {
  KeyboardAvoidingView,
  Platform,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
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
import {
  AuthField,
  AuthFooter as Footer,
  AuthFormError as ErrorBanner,
  AuthSubmitButton,
  authFormStyles,
  authInputColors,
} from "@/components/auth/AuthForm";
import { AuthPanel as Panel } from "@/components/auth/AuthPanel";
import { AuthHero } from "@/components/auth/AuthShell";
import { authStyles } from "@/components/auth/authStyles";
import { BrandMark } from "@/components/auth/BrandMark";
import { Wordmark } from "@/components/auth/Wordmark";
import { sectionTitleText } from "@/components/ui/SectionHeader";
import { fontFor, palette, radius, text } from "@/theme/theme";
import { MfaCodePanel } from "./login/MfaCodePanel";
import { TenantChoiceList } from "./login/TenantChoiceList";

type Props = NativeStackScreenProps<AuthStackParamList, "Login">;

// Turkish casing via "tr-TR" - never `textTransform`, which Android applies with the device locale.
const EYEBROW = sectionTitleText("İK & Bordro · Mobil");

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
                <AuthHero />
                <MfaCodePanel
                  code={mfaCode}
                  error={mfaError}
                  focused={focusedField === "mfaCode"}
                  isLoading={isLoading}
                  onChangeCode={handleMfaCodeChange}
                  onFocus={() => setFocusedField("mfaCode")}
                  onBlur={() => setFocusedField(null)}
                  onSubmit={() => handleMfaSubmit()}
                  onCancel={handleCancelMfa}
                />
                <Footer />
              </>
            ) : tenantChoices ? (
              <>
                <AuthHero />
                <Panel>
                  <Text style={authStyles.panelTitle}>Şirket seçin</Text>
                  <Text style={authStyles.panelSubtitle}>
                    Bu e-posta adresi birden fazla şirkete kayıtlı. Devam etmek için bir şirket
                    seçin.
                  </Text>
                  <TenantChoiceList
                    tenants={tenantChoices}
                    onSelect={handleSelectTenant}
                    isLoading={isLoading}
                  />
                  <TouchableOpacity
                    onPress={handleClearTenantChoices}
                    disabled={isLoading}
                    style={authStyles.secondaryLink}
                    accessibilityRole="button"
                    accessibilityLabel="Farklı hesapla giriş yap"
                  >
                    <Text style={authStyles.secondaryLinkText}>Farklı hesapla giriş yap</Text>
                  </TouchableOpacity>
                </Panel>
                <Footer />
              </>
            ) : (
              <>
                <Animated.View entering={FadeInDown.delay(250).duration(500)} style={styles.hero}>
                  <BrandMark size={56} />
                  <Text style={styles.eyebrow}>{EYEBROW}</Text>
                  <Wordmark />
                  <Text style={styles.heroSubtitle}>
                    İzin, puantaj ve bordronuz artık cebinizde.
                  </Text>
                </Animated.View>
                <Panel>
                  <Text style={authStyles.panelTitle}>Tekrar hoş geldiniz</Text>
                  <Text style={authStyles.panelSubtitle}>
                    Devam etmek için hesabınıza giriş yapın.
                  </Text>
                  {formError && <ErrorBanner message={formError} />}
                  <AuthField label="E-posta" focused={focusedField === "email"}>
                    <TextInput
                      style={authFormStyles.input}
                      placeholder="ad.soyad@sirket.com"
                      {...authInputColors}
                      accessibilityLabel="E-posta"
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
                  <AuthField label="Şifre" focused={focusedField === "password"}>
                    <TextInput
                      ref={passwordRef}
                      style={authFormStyles.input}
                      placeholder="Şifrenizi girin"
                      {...authInputColors}
                      accessibilityLabel="Şifre"
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
                  </AuthField>
                  <AuthSubmitButton
                    label="Giriş yap"
                    onPress={() => handleLogin()}
                    disabled={isLoading}
                    loading={isLoading}
                  />
                  {biometricAvailable && (
                    <TouchableOpacity
                      style={styles.secondaryButton}
                      onPress={handleBiometricLogin}
                      disabled={isLoading}
                      accessibilityRole="button"
                      accessibilityLabel="Biyometrik ile giriş yap"
                    >
                      <Text style={styles.secondaryButtonText}>Biyometrik ile giriş yap</Text>
                    </TouchableOpacity>
                  )}
                  <TouchableOpacity
                    style={authStyles.secondaryLink}
                    onPress={() => navigation.navigate("ForgotPassword")}
                    accessibilityRole="button"
                    accessibilityLabel="Şifremi unuttum"
                  >
                    <Text style={authStyles.secondaryLinkText}>Şifremi unuttum</Text>
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
  eyebrow: { ...text.section, marginTop: 20, fontSize: 11, letterSpacing: 2.4, color: palette.ice },
  heroSubtitle: { ...text.body, marginTop: 10, color: palette.ice },
  showHideButton: {
    paddingHorizontal: 4,
    paddingVertical: 8,
    minWidth: 44,
    minHeight: 44,
    alignItems: "center",
    justifyContent: "center",
  },
  showHideText: { fontSize: 13, color: palette.ice, ...fontFor(600) },
  secondaryButton: {
    marginTop: 12,
    height: 52,
    borderRadius: radius.button,
    borderWidth: 1,
    borderColor: palette.glassBorder,
    backgroundColor: palette.glass,
    alignItems: "center",
    justifyContent: "center",
  },
  secondaryButtonText: { fontSize: 15, color: palette.white, ...fontFor(700) },
});
