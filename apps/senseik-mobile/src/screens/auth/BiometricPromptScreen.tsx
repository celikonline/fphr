import React, { useCallback, useEffect, useRef, useState } from "react";
import { ActivityIndicator, StyleSheet, Text, View } from "react-native";
import { NativeStackScreenProps } from "@react-navigation/native-stack";
import { AuthStackParamList } from "@/types/navigation";
import { useAuthStore } from "@/store/authStore";
import { AuthFormError } from "@/components/auth/AuthForm";
import { AuthPanel } from "@/components/auth/AuthPanel";
import { AuthShell } from "@/components/auth/AuthShell";
import { authStyles } from "@/components/auth/authStyles";
import { PrimaryButton } from "@/components/ui";
import { palette } from "@/theme/theme";

type Props = NativeStackScreenProps<AuthStackParamList, "BiometricPrompt">;

const FAILURE_MESSAGE =
  "Biyometrik giriş başarısız. Lütfen tekrar deneyin veya şifrenizle giriş yapın.";

export default function BiometricPromptScreen({ navigation, route }: Props) {
  const { loginWithBiometric } = useAuthStore();
  const { email } = route.params;
  const [failed, setFailed] = useState(false);
  // A successful login swaps the whole navigator (this screen unmounts) - never set state after it.
  const mountedRef = useRef(true);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const attemptBiometric = useCallback(async () => {
    try {
      await loginWithBiometric(email);
    } catch {
      if (mountedRef.current) setFailed(true);
    }
  }, [email, loginWithBiometric]);

  useEffect(() => {
    void attemptBiometric();
  }, [attemptBiometric]);

  const handleRetry = () => {
    setFailed(false);
    void attemptBiometric();
  };

  return (
    <AuthShell>
      <AuthPanel>
        <Text style={authStyles.panelTitle}>Biyometrik giriş</Text>
        {failed ? (
          <>
            <AuthFormError message={FAILURE_MESSAGE} />
            <View style={styles.actions}>
              <PrimaryButton label="Tekrar dene" onPress={handleRetry} />
              <PrimaryButton
                label="Şifreyle giriş yap"
                variant="secondary"
                onPress={() => navigation.goBack()}
              />
            </View>
          </>
        ) : (
          <>
            <View style={styles.status} accessibilityLiveRegion="polite">
              <ActivityIndicator size="large" color={palette.brand} />
              <Text style={authStyles.panelSubtitle}>Kimlik doğrulanıyor...</Text>
            </View>
            <View style={styles.actions}>
              <PrimaryButton
                label="Vazgeç"
                variant="secondary"
                onPress={() => navigation.goBack()}
              />
            </View>
          </>
        )}
      </AuthPanel>
    </AuthShell>
  );
}

const styles = StyleSheet.create({
  status: { marginTop: 24, alignItems: "center", gap: 12 },
  actions: { marginTop: 24, gap: 12 },
});
