import React, { useEffect, useRef, useState } from "react";
import {
  AccessibilityInfo,
  Platform,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { NativeStackScreenProps } from "@react-navigation/native-stack";
import { AuthStackParamList } from "@/types/navigation";
import { authService } from "@/services/AuthService";
import { AuthFormError } from "@/components/auth/AuthForm";
import { AuthPanel } from "@/components/auth/AuthPanel";
import { AuthShell } from "@/components/auth/AuthShell";
import { authStyles } from "@/components/auth/authStyles";
import { PrimaryButton, TextField } from "@/components/ui";
import { palette, tones } from "@/theme/theme";

type Props = NativeStackScreenProps<AuthStackParamList, "ForgotPassword">;

const EMPTY_EMAIL_ERROR = "Lütfen e-posta adresinizi girin";
// Generic message without echoing the address, and shown regardless of whether the request
// succeeds for a registered or unregistered e-mail: the backend always answers 204 either way, so
// confirming a specific address here would enable account enumeration.
const SENT_MESSAGE = "E-posta kayıtlıysa sıfırlama bağlantısı gönderildi.";
const NETWORK_ERROR = "İstek gönderilemedi. Lütfen tekrar deneyin.";

export default function ForgotPasswordScreen({ navigation }: Props) {
  const [email, setEmail] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  // Ref guard (not state): a second rapid tap must be rejected synchronously, before React has a
  // chance to re-render with the disabled button - see BiometricPromptScreen's `inFlightRef`.
  const submittingRef = useRef(false);

  // The success text below uses `accessibilityLiveRegion`, which Android reads automatically but
  // iOS ignores - announce it to VoiceOver explicitly once the panel appears.
  useEffect(() => {
    if (sent && Platform.OS === "ios") {
      AccessibilityInfo.announceForAccessibility(SENT_MESSAGE);
    }
  }, [sent]);

  const handleEmailChange = (value: string) => {
    setEmail(value);
    if (error) setError(null);
  };

  const handleReset = async () => {
    if (submittingRef.current) return;
    if (!email) {
      setError(EMPTY_EMAIL_ERROR);
      return;
    }
    submittingRef.current = true;
    setIsSubmitting(true);
    try {
      await authService.forgotPassword(email);
      setSent(true);
    } catch {
      setError(NETWORK_ERROR);
    } finally {
      submittingRef.current = false;
      setIsSubmitting(false);
    }
  };

  if (sent) {
    return (
      <AuthShell>
        <AuthPanel>
          <View style={styles.sentIcon}>
            <Ionicons name="mail-outline" size={26} color={tones.success.fg} />
          </View>
          <Text style={authStyles.panelTitle}>Talebiniz alındı</Text>
          <Text style={authStyles.panelSubtitle} accessibilityLiveRegion="polite">
            {SENT_MESSAGE}
          </Text>
          <View style={styles.submit}>
            <PrimaryButton label="Girişe dön" onPress={() => navigation.goBack()} />
          </View>
        </AuthPanel>
      </AuthShell>
    );
  }

  return (
    <AuthShell>
      <AuthPanel>
        <Text style={authStyles.panelTitle}>Şifreyi sıfırla</Text>
        <Text style={authStyles.panelSubtitle}>
          Hesabınızın e-posta adresini girin, sıfırlama bağlantısını gönderelim.
        </Text>
        {error ? <AuthFormError message={error} /> : null}
        <View style={styles.field}>
          <TextField
            label="E-posta"
            placeholder="ad.soyad@sirket.com"
            selectionColor={palette.brand}
            value={email}
            onChangeText={handleEmailChange}
            keyboardType="email-address"
            autoCapitalize="none"
            autoCorrect={false}
            autoComplete="email"
            textContentType="emailAddress"
          />
        </View>
        <View style={styles.submit}>
          <PrimaryButton
            label="Sıfırlama bağlantısı gönder"
            onPress={() => void handleReset()}
            loading={isSubmitting}
            disabled={isSubmitting}
          />
        </View>
        <TouchableOpacity
          style={authStyles.secondaryLink}
          onPress={() => navigation.goBack()}
          accessibilityRole="button"
          accessibilityLabel="Girişe dön"
        >
          <Text style={authStyles.secondaryLinkText}>Girişe dön</Text>
        </TouchableOpacity>
      </AuthPanel>
    </AuthShell>
  );
}

const styles = StyleSheet.create({
  field: { marginTop: 18 },
  submit: { marginTop: 24 },
  sentIcon: {
    width: 52,
    height: 52,
    borderRadius: 16,
    marginBottom: 16,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: tones.success.bg,
  },
});
