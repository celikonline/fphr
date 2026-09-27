import React, { useEffect } from "react";
import { ActivityIndicator, Alert, StyleSheet, Text, View } from "react-native";
import { NativeStackScreenProps } from "@react-navigation/native-stack";
import { AuthStackParamList } from "@/types/navigation";
import { useAuthStore } from "@/store/authStore";
import { AuthPanel } from "@/components/auth/AuthPanel";
import { AuthShell } from "@/components/auth/AuthShell";
import { authStyles } from "@/components/auth/authStyles";
import { PrimaryButton } from "@/components/ui";
import { palette } from "@/theme/theme";

type Props = NativeStackScreenProps<AuthStackParamList, "BiometricPrompt">;

export default function BiometricPromptScreen({ navigation, route }: Props) {
  const { loginWithBiometric } = useAuthStore();
  const { email } = route.params;

  useEffect(() => {
    const attemptBiometric = async () => {
      try {
        await loginWithBiometric(email);
      } catch {
        Alert.alert(
          "Biyometrik giriş başarısız",
          "Lütfen tekrar deneyin veya şifrenizle giriş yapın."
        );
        navigation.goBack();
      }
    };

    attemptBiometric();
  }, [email, loginWithBiometric, navigation]);

  return (
    <AuthShell>
      <AuthPanel>
        <Text style={authStyles.panelTitle}>Biyometrik giriş</Text>
        <View style={styles.status} accessibilityLiveRegion="polite">
          <ActivityIndicator size="large" color={palette.brand} />
          <Text style={authStyles.panelSubtitle}>Kimlik doğrulanıyor...</Text>
        </View>
        <View style={styles.cancel}>
          <PrimaryButton label="Vazgeç" variant="secondary" onPress={() => navigation.goBack()} />
        </View>
      </AuthPanel>
    </AuthShell>
  );
}

const styles = StyleSheet.create({
  status: { marginTop: 24, alignItems: "center", gap: 12 },
  cancel: { marginTop: 24 },
});
