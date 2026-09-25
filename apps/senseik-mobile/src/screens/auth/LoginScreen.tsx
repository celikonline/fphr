import React, { useState } from "react";
import { View, Text, TextInput, TouchableOpacity, Alert, ActivityIndicator } from "react-native";
import { NativeStackScreenProps } from "@react-navigation/native-stack";
import { AuthStackParamList } from "@/types/navigation";
import { useAuthStore } from "@/store/authStore";
import { biometricService } from "@/lib/biometric";
import { TenantChoice } from "@/types";

type Props = NativeStackScreenProps<AuthStackParamList, "Login">;

export default function LoginScreen({ navigation }: Props) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [biometricAvailable, setBiometricAvailable] = useState(false);
  const { login, isLoading, tenantChoices, clearTenantChoices } = useAuthStore();

  React.useEffect(() => {
    const checkBiometric = async () => {
      const available = await biometricService.isBiometricAvailable();
      setBiometricAvailable(available.available);
    };
    checkBiometric();
  }, []);

  const handleLogin = async (tenantSlug?: string) => {
    if (!email || !password) {
      Alert.alert("Hata", "Lütfen e-posta ve şifrenizi girin");
      return;
    }

    try {
      // Resolves to "ok" (store marks the session authenticated - navigation reacts to that) or
      // "select_tenant" (store fills `tenantChoices`, rendered below instead of the form).
      await login(email, password, tenantSlug);
    } catch (error) {
      // Generic message for credential failures: backend/axios errors can leak status codes, hosts or
      // endpoint details into screenshots, and precise messages enable user enumeration. The MFA-not-
      // supported message is safe to show verbatim (no account/session details).
      const message = error instanceof Error && error.message ? error.message : "";
      Alert.alert(
        "Giriş başarısız",
        message.startsWith("İki adımlı doğrulama") ? message : "E-posta veya şifre hatalı. Lütfen tekrar deneyin."
      );
    }
  };

  const handleSelectTenant = (tenant: TenantChoice) => {
    void handleLogin(tenant.slug);
  };

  const handleBiometricLogin = async () => {
    if (!email) {
      Alert.alert("Hata", "Lütfen önce e-posta adresinizi girin");
      return;
    }
    navigation.navigate("BiometricPrompt", { email });
  };

  if (tenantChoices) {
    return (
      <View style={{ flex: 1, padding: 20, justifyContent: "center" }}>
        <Text style={{ fontSize: 28, fontWeight: "bold", marginBottom: 20 }}>SenseİK Mobile</Text>
        <Text style={{ fontSize: 16, marginBottom: 15 }}>Devam etmek için şirket seçin</Text>

        {tenantChoices.map(tenant => (
          <TouchableOpacity
            key={tenant.tenantId}
            style={{
              borderWidth: 1,
              borderColor: "#ccc",
              padding: 15,
              borderRadius: 8,
              marginBottom: 10
            }}
            onPress={() => handleSelectTenant(tenant)}
            disabled={isLoading}
          >
            <Text style={{ fontSize: 16 }}>{tenant.name}</Text>
          </TouchableOpacity>
        ))}

        {isLoading && <ActivityIndicator style={{ marginBottom: 10 }} />}

        <TouchableOpacity onPress={clearTenantChoices} disabled={isLoading}>
          <Text style={{ color: "#0066cc", textAlign: "center" }}>Geri</Text>
        </TouchableOpacity>
      </View>
    );
  }

  return (
    <View style={{ flex: 1, padding: 20, justifyContent: "center" }}>
      <Text style={{ fontSize: 28, fontWeight: "bold", marginBottom: 20 }}>SenseİK Mobile</Text>

      <TextInput
        style={{
          borderWidth: 1,
          borderColor: "#ccc",
          padding: 12,
          marginBottom: 15,
          borderRadius: 8
        }}
        placeholder="E-posta"
        value={email}
        onChangeText={setEmail}
        editable={!isLoading}
        keyboardType="email-address"
        autoCapitalize="none"
        autoCorrect={false}
        autoComplete="email"
        textContentType="emailAddress"
      />

      <TextInput
        style={{
          borderWidth: 1,
          borderColor: "#ccc",
          padding: 12,
          marginBottom: 20,
          borderRadius: 8
        }}
        placeholder="Şifre"
        secureTextEntry
        value={password}
        onChangeText={setPassword}
        editable={!isLoading}
        autoCapitalize="none"
      />

      <TouchableOpacity
        style={{
          backgroundColor: "#0066cc",
          padding: 15,
          borderRadius: 8,
          alignItems: "center",
          marginBottom: 10
        }}
        onPress={() => handleLogin()}
        disabled={isLoading}
      >
        {isLoading ? (
          <ActivityIndicator color="white" />
        ) : (
          <Text style={{ color: "white", fontSize: 16, fontWeight: "bold" }}>Giriş yap</Text>
        )}
      </TouchableOpacity>

      {biometricAvailable && (
        <TouchableOpacity
          style={{
            backgroundColor: "#34c759",
            padding: 15,
            borderRadius: 8,
            alignItems: "center",
            marginBottom: 10
          }}
          onPress={handleBiometricLogin}
          disabled={isLoading}
        >
          <Text style={{ color: "white", fontSize: 16, fontWeight: "bold" }}>Parmak izi ile giriş yap</Text>
        </TouchableOpacity>
      )}

      <TouchableOpacity onPress={() => navigation.navigate("ForgotPassword")}>
        <Text style={{ color: "#0066cc", textAlign: "center" }}>Şifremi unuttum</Text>
      </TouchableOpacity>
    </View>
  );
}
