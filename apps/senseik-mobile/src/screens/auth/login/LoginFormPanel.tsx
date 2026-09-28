import React from "react";
import { StyleSheet, Text, TextInput, TouchableOpacity } from "react-native";
import {
  AuthField,
  AuthFormError as ErrorBanner,
  AuthSubmitButton,
  authFormStyles,
  authInputColors,
} from "@/components/auth/AuthForm";
import { AuthPanel as Panel } from "@/components/auth/AuthPanel";
import { authStyles } from "@/components/auth/authStyles";
import { fontFor, palette, radius } from "@/theme/theme";

type FocusableField = "email" | "password";

type Props = {
  email: string;
  password: string;
  showPassword: boolean;
  focusedField: FocusableField | "mfaCode" | null;
  formError: string | null;
  isLoading: boolean;
  biometricAvailable: boolean;
  passwordRef: React.RefObject<TextInput | null>;
  onEmailChange: (value: string) => void;
  onPasswordChange: (value: string) => void;
  onFocusField: (field: FocusableField) => void;
  onBlurField: () => void;
  onToggleShowPassword: () => void;
  onSubmit: () => void;
  onBiometricLogin: () => void;
  onForgotPassword: () => void;
};

/** Login screen's credentials panel: e-mail/password fields, submit, biometric shortcut, and the
 * forgot-password link. Presentation only - LoginScreen owns all state and handlers. */
export function LoginFormPanel({
  email,
  password,
  showPassword,
  focusedField,
  formError,
  isLoading,
  biometricAvailable,
  passwordRef,
  onEmailChange,
  onPasswordChange,
  onFocusField,
  onBlurField,
  onToggleShowPassword,
  onSubmit,
  onBiometricLogin,
  onForgotPassword,
}: Props) {
  return (
    <Panel>
      <Text style={authStyles.panelTitle}>Tekrar hoş geldiniz</Text>
      <Text style={authStyles.panelSubtitle}>Devam etmek için hesabınıza giriş yapın.</Text>
      {formError && <ErrorBanner message={formError} />}
      <AuthField label="E-posta" focused={focusedField === "email"}>
        <TextInput
          style={authFormStyles.input}
          placeholder="ad.soyad@sirket.com"
          {...authInputColors}
          accessibilityLabel="E-posta"
          value={email}
          onChangeText={onEmailChange}
          onFocus={() => onFocusField("email")}
          onBlur={onBlurField}
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
          onChangeText={onPasswordChange}
          onFocus={() => onFocusField("password")}
          onBlur={onBlurField}
          editable={!isLoading}
          secureTextEntry={!showPassword}
          autoCapitalize="none"
          textContentType="password"
          autoComplete="password"
          returnKeyType="go"
          onSubmitEditing={onSubmit}
        />
        <TouchableOpacity
          onPress={onToggleShowPassword}
          accessibilityRole="button"
          accessibilityLabel={showPassword ? "Şifreyi gizle" : "Şifreyi göster"}
          style={styles.showHideButton}
        >
          <Text style={styles.showHideText}>{showPassword ? "Gizle" : "Göster"}</Text>
        </TouchableOpacity>
      </AuthField>
      <AuthSubmitButton
        label="Giriş yap"
        onPress={onSubmit}
        disabled={isLoading}
        loading={isLoading}
      />
      {biometricAvailable && (
        <TouchableOpacity
          style={styles.secondaryButton}
          onPress={onBiometricLogin}
          disabled={isLoading}
          accessibilityRole="button"
          accessibilityLabel="Biyometrik ile giriş yap"
        >
          <Text style={styles.secondaryButtonText}>Biyometrik ile giriş yap</Text>
        </TouchableOpacity>
      )}
      <TouchableOpacity
        style={authStyles.secondaryLink}
        onPress={onForgotPassword}
        accessibilityRole="button"
        accessibilityLabel="Şifremi unuttum"
      >
        <Text style={authStyles.secondaryLinkText}>Şifremi unuttum</Text>
      </TouchableOpacity>
    </Panel>
  );
}

const styles = StyleSheet.create({
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
