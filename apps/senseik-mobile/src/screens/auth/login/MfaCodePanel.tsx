import React from "react";
import { Text, TextInput, TouchableOpacity } from "react-native";
import {
  AuthField,
  AuthFormError,
  AuthSubmitButton,
  authFormStyles,
  authInputColors,
} from "@/components/auth/AuthForm";
import { AuthPanel } from "@/components/auth/AuthPanel";
import { authStyles } from "@/components/auth/authStyles";

type Props = {
  code: string;
  error: string | null;
  focused: boolean;
  isLoading: boolean;
  onChangeCode: (value: string) => void;
  onFocus: () => void;
  onBlur: () => void;
  onSubmit: () => void;
  onCancel: () => void;
};

/** Login's MFA step panel (6-digit code entry). Presentation only - LoginScreen owns the logic. */
export function MfaCodePanel({
  code,
  error,
  focused,
  isLoading,
  onChangeCode,
  onFocus,
  onBlur,
  onSubmit,
  onCancel,
}: Props) {
  return (
    <AuthPanel>
      <Text style={authStyles.panelTitle}>İki adımlı doğrulama</Text>
      <Text style={authStyles.panelSubtitle}>
        Kimlik doğrulama uygulamanızdaki 6 haneli kodu girin.
      </Text>
      {error && <AuthFormError message={error} />}
      <AuthField label="Doğrulama kodu" focused={focused}>
        <TextInput
          style={authFormStyles.input}
          placeholder="000000"
          {...authInputColors}
          accessibilityLabel="Doğrulama kodu"
          value={code}
          onChangeText={onChangeCode}
          onFocus={onFocus}
          onBlur={onBlur}
          editable={!isLoading}
          keyboardType="number-pad"
          maxLength={6}
          autoComplete="one-time-code"
          textContentType="oneTimeCode"
          returnKeyType="done"
          onSubmitEditing={onSubmit}
        />
      </AuthField>
      <AuthSubmitButton
        label="Doğrula"
        onPress={onSubmit}
        disabled={isLoading || code.length !== 6}
        loading={isLoading}
      />
      <TouchableOpacity
        onPress={onCancel}
        disabled={isLoading}
        style={authStyles.secondaryLink}
        accessibilityRole="button"
        accessibilityLabel="Farklı hesapla giriş yap"
      >
        <Text style={authStyles.secondaryLinkText}>Farklı hesapla giriş yap</Text>
      </TouchableOpacity>
    </AuthPanel>
  );
}
