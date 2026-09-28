import React, { useEffect, useRef, useState } from "react";
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, TextInput } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";
import { useTranslation } from "react-i18next";
import { NativeStackScreenProps } from "@react-navigation/native-stack";
import { AuthStackParamList } from "@/types/navigation";
import { useAuthStore } from "@/store/authStore";
import { biometricService } from "@/lib/biometric";
import { getApiProblem } from "@/lib/apiError";
import { TenantChoice } from "@/types";
import { AuthBackdrop } from "@/components/auth/AuthBackdrop";
import { AuthFooter as Footer } from "@/components/auth/AuthForm";
import { AuthHero } from "@/components/auth/AuthShell";
import { LoginFormPanel } from "./login/LoginFormPanel";
import { LoginHero } from "./login/LoginHero";
import { MfaCodePanel } from "./login/MfaCodePanel";
import { TenantChoicePanel } from "./login/TenantChoicePanel";

type Props = NativeStackScreenProps<AuthStackParamList, "Login">;

export default function LoginScreen({ navigation }: Props) {
  const { t } = useTranslation("auth");
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
      setFormError(t("errors.credentialsRequired"));
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
      setFormError(t("errors.invalidCredentials"));
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
        setFormError(t("errors.mfaExpired"));
      } else {
        setMfaCode("");
        setMfaError(t("errors.mfaInvalidCode"));
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
      setFormError(t("errors.emailRequiredForBiometric"));
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
                <TenantChoicePanel
                  tenants={tenantChoices}
                  onSelect={handleSelectTenant}
                  isLoading={isLoading}
                  onCancel={handleClearTenantChoices}
                />
                <Footer />
              </>
            ) : (
              <>
                <LoginHero />
                <LoginFormPanel
                  email={email}
                  password={password}
                  showPassword={showPassword}
                  focusedField={focusedField}
                  formError={formError}
                  isLoading={isLoading}
                  biometricAvailable={biometricAvailable}
                  passwordRef={passwordRef}
                  onEmailChange={handleEmailChange}
                  onPasswordChange={handlePasswordChange}
                  onFocusField={(field) => setFocusedField(field)}
                  onBlurField={() => setFocusedField(null)}
                  onToggleShowPassword={() => setShowPassword((value) => !value)}
                  onSubmit={() => handleLogin()}
                  onBiometricLogin={handleBiometricLogin}
                  onForgotPassword={() => navigation.navigate("ForgotPassword")}
                />
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
});
