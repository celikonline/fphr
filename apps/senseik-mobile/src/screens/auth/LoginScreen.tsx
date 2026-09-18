import React, { useState } from "react";
import { View, Text, TextInput, TouchableOpacity, Alert, ActivityIndicator } from "react-native";
import { NativeStackScreenProps } from "@react-navigation/native-stack";
import { AuthStackParamList } from "@/types/navigation";
import { useAuthStore } from "@/store/authStore";
import { biometricService } from "@/lib/biometric";

type Props = NativeStackScreenProps<AuthStackParamList, "Login">;

export default function LoginScreen({ navigation }: Props) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [biometricAvailable, setBiometricAvailable] = useState(false);
  const { login, isLoading } = useAuthStore();

  React.useEffect(() => {
    const checkBiometric = async () => {
      const available = await biometricService.isBiometricAvailable();
      setBiometricAvailable(available.available);
    };
    checkBiometric();
  }, []);

  const handleLogin = async () => {
    if (!email || !password) {
      Alert.alert("Hata", "Lütfen e-posta ve şifrenizi girin");
      return;
    }

    try {
      await login(email, password);
    } catch {
      // Generic message: backend/axios errors can leak status codes, hosts or endpoint
      // details into screenshots, and precise messages enable user enumeration.
      Alert.alert("Giriş başarısız", "E-posta veya şifre hatalı. Lütfen tekrar deneyin.");
    }
  };

  const handleBiometricLogin = async () => {
    if (!email) {
      Alert.alert("Hata", "Lütfen önce e-posta adresinizi girin");
      return;
    }
    navigation.navigate("BiometricPrompt", { email });
  };

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
      />

      <TouchableOpacity
        style={{
          backgroundColor: "#0066cc",
          padding: 15,
          borderRadius: 8,
          alignItems: "center",
          marginBottom: 10
        }}
        onPress={handleLogin}
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
