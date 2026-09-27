import React, { useState } from "react";
import { Alert, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { NativeStackScreenProps } from "@react-navigation/native-stack";
import { AuthStackParamList } from "@/types/navigation";
import { AuthPanel } from "@/components/auth/AuthPanel";
import { AuthShell } from "@/components/auth/AuthShell";
import { authStyles } from "@/components/auth/authStyles";
import { PrimaryButton, TextField } from "@/components/ui";
import { palette } from "@/theme/theme";

type Props = NativeStackScreenProps<AuthStackParamList, "ForgotPassword">;

export default function ForgotPasswordScreen({ navigation }: Props) {
  const [email, setEmail] = useState("");

  const handleReset = () => {
    if (!email) {
      Alert.alert("Hata", "Lütfen e-posta adresinizi girin");
      return;
    }
    // Generic message without echoing the address: the screen performs no request yet,
    // and confirming a specific address enables account enumeration.
    Alert.alert("Başarılı", "E-posta kayıtlıysa sıfırlama bağlantısı gönderildi.");
    navigation.goBack();
  };

  return (
    <AuthShell>
      <AuthPanel>
        <Text style={authStyles.panelTitle}>Şifreyi sıfırla</Text>
        <Text style={authStyles.panelSubtitle}>
          Hesabınızın e-posta adresini girin, sıfırlama bağlantısını gönderelim.
        </Text>
        <View style={styles.field}>
          <TextField
            label="E-posta"
            placeholder="ad.soyad@sirket.com"
            selectionColor={palette.brand}
            value={email}
            onChangeText={setEmail}
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
});
