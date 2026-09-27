import React, { useState } from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { NativeStackScreenProps } from "@react-navigation/native-stack";
import { AuthStackParamList } from "@/types/navigation";
import { AuthFormError } from "@/components/auth/AuthForm";
import { AuthPanel } from "@/components/auth/AuthPanel";
import { AuthShell } from "@/components/auth/AuthShell";
import { authStyles } from "@/components/auth/authStyles";
import { PrimaryButton, TextField } from "@/components/ui";
import { palette, tones } from "@/theme/theme";

type Props = NativeStackScreenProps<AuthStackParamList, "ForgotPassword">;

const EMPTY_EMAIL_ERROR = "Lütfen e-posta adresinizi girin";
// Generic message without echoing the address: the screen performs no request yet, and
// confirming a specific address enables account enumeration.
const SENT_MESSAGE = "E-posta kayıtlıysa sıfırlama bağlantısı gönderildi.";

export default function ForgotPasswordScreen({ navigation }: Props) {
  const [email, setEmail] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);

  const handleEmailChange = (value: string) => {
    setEmail(value);
    if (error) setError(null);
  };

  const handleReset = () => {
    if (!email) {
      setError(EMPTY_EMAIL_ERROR);
      return;
    }
    setSent(true);
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
          <PrimaryButton label="Sıfırlama bağlantısı gönder" onPress={handleReset} />
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
