import React from "react";
import { Text, TextInput, TouchableOpacity } from "react-native";
import { useTranslation } from "react-i18next";
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
  const { t } = useTranslation("auth");
  return (
    <AuthPanel>
      <Text style={authStyles.panelTitle}>{t("mfa.title")}</Text>
      <Text style={authStyles.panelSubtitle}>{t("mfa.subtitle")}</Text>
      {error && <AuthFormError message={error} />}
      <AuthField label={t("mfa.codeLabel")} focused={focused}>
        <TextInput
          style={authFormStyles.input}
          placeholder="000000"
          {...authInputColors}
          accessibilityLabel={t("mfa.codeLabel")}
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
        label={t("mfa.submit")}
        onPress={onSubmit}
        disabled={isLoading || code.length !== 6}
        loading={isLoading}
      />
      <TouchableOpacity
        onPress={onCancel}
        disabled={isLoading}
        style={authStyles.secondaryLink}
        accessibilityRole="button"
        accessibilityLabel={t("login.switchAccount")}
      >
        <Text style={authStyles.secondaryLinkText}>{t("login.switchAccount")}</Text>
      </TouchableOpacity>
    </AuthPanel>
  );
}
